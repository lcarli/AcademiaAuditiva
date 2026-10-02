using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AcademiaAuditiva.Interfaces;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NLayer;

namespace AcademiaAuditiva.Services.Audio;

/// <inheritdoc />
public sealed class AudioMixerService : IAudioMixerService
{
    private const string SourceContainerName = "piano-audio";
    private const string MixedContainerName = "piano-audio-mixed";

    // Roughly the max audible duration we ever expect for a single
    // round (a four-note melody with rests). Inputs that would extend
    // past this are clamped — protects the mixer from runaway memory
    // if upstream plumbing ever sends a bad plan.
    private const double MaxMixDurationSeconds = 30.0;

    /// <summary>
    /// How long a mixed blob may go without a write before its name is
    /// handed out again unchecked. The storage lifecycle rule deletes mixed
    /// blobs about a day after their last write (storage.bicep), while a
    /// replica can run for days, so older mixes are checked and touched
    /// first. Half a day keeps every token's 15 min far from deletion.
    /// </summary>
    internal static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AudioMixerService> _logger;

    // Process-local memo: once we've mixed a given (sorted) input plan,
    // we keep the resulting blob name in memory so replays don't even
    // need to HEAD the storage account while the blob is fresh.
    private readonly ConcurrentDictionary<string, MixMemo> _planToMix = new();

    public AudioMixerService(BlobServiceClient blobServiceClient, TimeProvider timeProvider, ILogger<AudioMixerService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MixedAudio> MixAsync(
        IReadOnlyList<MixInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("At least one input is required.", nameof(inputs));
        }

        // Even a single untrimmed note is mixed: the audio endpoint varies
        // every clip it streams (ClipVariation) and needs PCM WAV to do it.
        var planHash = ComputePlanHash(inputs);
        if (_planToMix.TryGetValue(planHash, out var memo)
            && _timeProvider.GetUtcNow() - memo.WrittenAt < FreshFor)
        {
            return memo.Mix;
        }

        var mixedBlobName = $"mix-{planHash}.wav";
        var result = new MixedAudio(MixedContainerName, mixedBlobName);
        var sourceContainer = _blobServiceClient.GetBlobContainerClient(SourceContainerName);
        var mixedContainer = _blobServiceClient.GetBlobContainerClient(MixedContainerName);
        var mixedBlob = mixedContainer.GetBlobClient(mixedBlobName);

        // If a previous request (or replica) already produced this exact
        // mix and the lifecycle rule hasn't deleted it, skip the work.
        if (await KeepExistingAsync(mixedBlob, cancellationToken).ConfigureAwait(false) is { } writtenAt)
        {
            _planToMix[planHash] = new MixMemo(result, writtenAt);
            return result;
        }

        // 1) Decode every source mp3 into float PCM. Honour the first
        //    sample's format as canonical; mismatched inputs would
        //    require resampling, which we don't have on Linux Alpine.
        var decoded = new List<DecodedSample>(inputs.Count);
        int? sampleRate = null;
        int? channels = null;
        foreach (var input in inputs)
        {
            var sample = await DecodeAsync(sourceContainer, input.BlobName, cancellationToken).ConfigureAwait(false);
            sampleRate ??= sample.SampleRate;
            channels ??= sample.Channels;
            if (sample.SampleRate != sampleRate.Value || sample.Channels != channels.Value)
            {
                throw new InvalidOperationException(
                    $"Source '{input.BlobName}' has format {sample.SampleRate}Hz/{sample.Channels}ch but the mix " +
                    $"uses {sampleRate.Value}Hz/{channels.Value}ch. All piano-audio sources must share one format.");
            }
            decoded.Add(sample);
        }

        // 2) Compute the mixed buffer length (samples per channel).
        var sr = sampleRate!.Value;
        var ch = channels!.Value;

        var totalLengthSamples = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var startSample = (int)Math.Round(inputs[i].StartTimeSeconds * sr);
            var srcSamples = decoded[i].Samples.Length / ch;
            var maxSamples = inputs[i].DurationSeconds is { } d
                ? (int)Math.Round(d * sr)
                : srcSamples;
            var useSamples = Math.Min(srcSamples, maxSamples);
            totalLengthSamples = Math.Max(totalLengthSamples, startSample + useSamples);
        }

        var maxAllowed = (int)Math.Round(MaxMixDurationSeconds * sr);
        if (totalLengthSamples > maxAllowed)
        {
            throw new InvalidOperationException(
                $"Refusing to mix {totalLengthSamples / (double)sr:F1}s of audio (cap is {MaxMixDurationSeconds}s).");
        }

        // 3) Sum into a float accumulator, then clip to int16.
        var accum = new float[totalLengthSamples * ch];
        for (var i = 0; i < inputs.Count; i++)
        {
            var src = decoded[i].Samples;
            var startFrame = (int)Math.Round(inputs[i].StartTimeSeconds * sr);
            var srcFrames = src.Length / ch;
            var maxFrames = inputs[i].DurationSeconds is { } d
                ? (int)Math.Round(d * sr)
                : srcFrames;
            var useFrames = Math.Min(srcFrames, maxFrames);

            var dstOffset = startFrame * ch;
            var copyLength = useFrames * ch;
            for (var k = 0; k < copyLength; k++)
            {
                accum[dstOffset + k] += src[k];
            }
        }

        // 4) Encode WAV (RIFF/PCM int16).
        using var wavStream = new MemoryStream(44 + accum.Length * 2);
        WriteWavHeader(wavStream, sr, ch, accum.Length);
        WritePcm16(wavStream, accum);
        wavStream.Position = 0;

        // 5) Upload, overwriting any copy that is about to expire. These
        //    blobs are short-lived; the lifecycle rule on the container
        //    deletes them, so the default tier is right.
        try
        {
            await mixedBlob.UploadAsync(
                wavStream,
                new BlobHttpHeaders { ContentType = "audio/wav" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobAlreadyExists)
        {
            // Race against another request that produced the same hash.
            // Both blobs would be byte-equivalent; nothing to do.
        }

        _planToMix[planHash] = new MixMemo(result, _timeProvider.GetUtcNow());
        _logger.LogDebug("Mixed {InputCount} sources → {Container}/{Blob} ({Duration:F2}s)",
            inputs.Count, result.Container, result.BlobName, totalLengthSamples / (double)sr);
        return result;
    }

    /// <summary>
    /// Returns when the stored mix was last written, touching it first when
    /// it is no longer fresh, or <c>null</c> when it has to be mixed again.
    /// </summary>
    private async Task<DateTimeOffset?> KeepExistingAsync(BlobClient mixedBlob, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await mixedBlob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            if (now - properties.Value.LastModified < FreshFor)
            {
                return properties.Value.LastModified;
            }

            // Any write moves Last-Modified, which is what the lifecycle rule reads.
            await mixedBlob.SetMetadataAsync(
                new Dictionary<string, string> { ["touched"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return now;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<DecodedSample> DecodeAsync(
        BlobContainerClient sourceContainer,
        string blobName,
        CancellationToken cancellationToken)
    {
        var blobClient = sourceContainer.GetBlobClient(blobName);
        using var ms = new MemoryStream();
        await blobClient.DownloadToAsync(ms, cancellationToken).ConfigureAwait(false);
        ms.Position = 0;

        // NLayer is a fully managed mp3 decoder — works on Linux/Alpine.
        using var mpeg = new MpegFile(ms);
        var sampleRate = mpeg.SampleRate;
        var channels = mpeg.Channels;

        // Total samples is unknown ahead of time; grow geometrically.
        var buffer = new List<float>(capacity: 1024 * channels);
        var chunk = new float[4096];
        int read;
        while ((read = mpeg.ReadSamples(chunk, 0, chunk.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                buffer.Add(chunk[i]);
            }
        }

        return new DecodedSample(sampleRate, channels, buffer.ToArray());
    }

    private static void WriteWavHeader(Stream stream, int sampleRate, int channels, int totalFrames)
    {
        var byteRate = sampleRate * channels * 2;
        var dataSize = totalFrames * 2; // accum is already (frames*channels) values, each 2 bytes
        var fileSize = 36 + dataSize;

        Span<byte> header = stackalloc byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), fileSize);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header.Slice(8, 4));
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header.Slice(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16, 4), 16);          // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(20, 2), 1);           // format = PCM
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(22, 2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(32, 2), (short)(channels * 2)); // block align
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(34, 2), 16);          // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(header.Slice(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(40, 4), dataSize);
        stream.Write(header);
    }

    private static void WritePcm16(Stream stream, float[] floatSamples)
    {
        // Soft-clip with simple saturation. NLayer outputs in roughly
        // [-1, 1]; summing N notes can exceed that. We accept a small
        // amount of clipping for simplicity — acceptable for practice
        // playback at moderate volumes.
        var pcmBuffer = new byte[floatSamples.Length * 2];
        for (var i = 0; i < floatSamples.Length; i++)
        {
            var s = floatSamples[i];
            if (s > 1f) s = 1f;
            else if (s < -1f) s = -1f;
            var i16 = (short)Math.Round(s * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(pcmBuffer.AsSpan(i * 2, 2), i16);
        }
        stream.Write(pcmBuffer);
    }

    private static string ComputePlanHash(IReadOnlyList<MixInput> inputs)
    {
        // Invariant, so a plan maps to the same blob whatever the request culture
        // (pt-BR and fr-CA would otherwise write "0,4000").
        var canonical = string.Join("|", inputs
            .Select(i => string.Create(CultureInfo.InvariantCulture,
                $"{i.BlobName}@{i.StartTimeSeconds:F4}/{i.DurationSeconds?.ToString("F4", CultureInfo.InvariantCulture) ?? "*"}")));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record DecodedSample(int SampleRate, int Channels, float[] Samples);

    private sealed record MixMemo(MixedAudio Mix, DateTimeOffset WrittenAt);
}
