using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
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

    // Every note fades out over its last 25 ms: a note cut short by the
    // plan (or a guitar string still ringing) would otherwise end in a click.
    private const double FadeOutSeconds = 0.025;

    // Notes that add up past this peak scale the whole mix down instead of
    // clipping: the louder samples of other instruments would distort chords.
    private const float MaxPeak = 0.98f;

    // Part of every mix name. Change it whenever the same plan starts to
    // sound different, so stored mixes made the old way are not reused.
    private const string MixVersion = "v2";

    /// <summary>
    /// Starts the name of every clip rendered from an
    /// <see cref="AudioProcessingPlan"/>. The audio endpoint streams these at
    /// their own level (see <see cref="ClipVariation"/>): their level is what
    /// a round compares.
    /// </summary>
    public const string ProcessedBlobPrefix = "proc-";

    // A note shifted in pitch is resampled with a windowed sinc this many
    // samples wide on each side, from a table of this many sub-sample
    // positions. Linear interpolation would dull the shifted note and give
    // its timbre away next to the untouched one.
    private const int ResampleHalfWidth = 16;
    private const int ResamplePhases = 1024;
    private const double MaxCents = 1200;

    /// <summary>
    /// How long a mixed blob may go without a write before its name is
    /// handed out again unchecked. The storage lifecycle rule deletes mixed
    /// blobs about a day after their last write (storage.bicep), while a
    /// replica can run for days, so older mixes are checked and touched
    /// first. Half a day keeps every token's 15 min far from deletion.
    /// </summary>
    internal static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly BundledSamples _bundledSamples;
    private readonly AudioSourceLibrary _audioSources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AudioMixerService> _logger;

    // Process-local memo: once we've stored a clip, we keep its name in
    // memory so replays don't even need to HEAD the storage account while
    // the blob is fresh. Keyed by blob name, which hashes the whole plan.
    private readonly ConcurrentDictionary<string, MixMemo> _memo = new();

    public AudioMixerService(
        BlobServiceClient blobServiceClient,
        BundledSamples bundledSamples,
        AudioSourceLibrary audioSources,
        TimeProvider timeProvider,
        ILogger<AudioMixerService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _bundledSamples = bundledSamples;
        _audioSources = audioSources;
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
        if (inputs.Any(i => !(Math.Abs(i.Cents) <= MaxCents)))
        {
            throw new ArgumentOutOfRangeException(nameof(inputs), $"A note can be shifted by {MaxCents} cents at most.");
        }

        // Even a single untrimmed note is mixed: the audio endpoint varies
        // every clip it streams (ClipVariation) and needs PCM WAV to do it.
        var planHash = ComputePlanHash(inputs);
        return await StoreAsync($"mix-{planHash}.wav", token => MixInputsAsync(inputs, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<MixedAudio> RenderAsync(
        AudioProcessingPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            var source = (plan.SourceKey is null ? null : _audioSources.Find(plan.SourceKey))
                ?? throw new ArgumentException("The plan names no listed audio source.", nameof(plan));
            AudioProcessing.Validate(plan, source);

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AudioProcessing.Describe(plan, source.Version))));
            return await StoreAsync($"{ProcessedBlobPrefix}{hash}.wav", token => ProcessAsync(source, plan, token), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && LogFailure(ex, plan))
        {
            throw;
        }
    }

    // The parameters can be a round's answer: only the processors' names are logged.
    private bool LogFailure(Exception ex, AudioProcessingPlan plan)
    {
        _logger.LogError(ex, "Processing audio source {Source} with {Processors} failed.", plan.SourceKey, ProcessorNames(plan));
        return false;
    }

    private static string ProcessorNames(AudioProcessingPlan plan) =>
        plan.Processors is null ? "" : string.Join(",", plan.Processors.Select(p => p?.Name ?? "null"));

    private async Task<PcmAudio> ProcessAsync(AudioSource source, AudioProcessingPlan plan, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var audio = await _audioSources.ReadAsync(source, cancellationToken).ConfigureAwait(false);
        var read = _timeProvider.GetElapsedTime(started);

        started = _timeProvider.GetTimestamp();
        var processed = AudioProcessing.Process(audio, plan.Processors);

        // Validate predicts the peak from the source's measurement; this is the
        // guard behind it. Nothing is ever scaled to fit, and nothing unprocessed
        // is stored in its place.
        foreach (var sample in processed.Samples)
        {
            if (!(Math.Abs(sample) <= MaxPeak))
            {
                throw new InvalidOperationException($"Processing audio source '{source.Key}' would clip.");
            }
        }

        _logger.LogDebug("Processed audio source {Source} with {Processors}: read {ReadMs:F1} ms, DSP {DspMs:F1} ms.",
            source.Key, ProcessorNames(plan), read.TotalMilliseconds, _timeProvider.GetElapsedTime(started).TotalMilliseconds);
        return processed;
    }

    /// <summary>
    /// Returns the stored clip named <paramref name="blobName"/>, rendering,
    /// encoding and uploading it first when it is not stored or about to expire.
    /// </summary>
    private async Task<MixedAudio> StoreAsync(
        string blobName,
        Func<CancellationToken, Task<PcmAudio>> render,
        CancellationToken cancellationToken)
    {
        if (_memo.TryGetValue(blobName, out var memo)
            && _timeProvider.GetUtcNow() - memo.WrittenAt < FreshFor)
        {
            return memo.Mix;
        }

        var result = new MixedAudio(MixedContainerName, blobName);
        var blob = _blobServiceClient.GetBlobContainerClient(MixedContainerName).GetBlobClient(blobName);

        // If a previous request (or replica) already produced this exact
        // clip and the lifecycle rule hasn't deleted it, skip the work.
        if (await KeepExistingAsync(blob, cancellationToken).ConfigureAwait(false) is { } writtenAt)
        {
            _memo[blobName] = new MixMemo(result, writtenAt);
            return result;
        }

        var audio = await render(cancellationToken).ConfigureAwait(false);

        var started = _timeProvider.GetTimestamp();
        using var wavStream = EncodeWav(audio);
        var encoded = _timeProvider.GetElapsedTime(started);

        // Upload, overwriting any copy that is about to expire. These blobs
        // are short-lived; the lifecycle rule on the container deletes them,
        // so the default tier is right.
        started = _timeProvider.GetTimestamp();
        try
        {
            await blob.UploadAsync(
                wavStream,
                new BlobHttpHeaders { ContentType = "audio/wav" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobAlreadyExists)
        {
            // Race against another request that produced the same hash.
            // Both blobs would be byte-equivalent; nothing to do.
        }

        _memo[blobName] = new MixMemo(result, _timeProvider.GetUtcNow());
        _logger.LogDebug("Stored {Container}/{Blob}: encode {EncodeMs:F1} ms, upload {UploadMs:F1} ms.",
            result.Container, result.BlobName, encoded.TotalMilliseconds, _timeProvider.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private async Task<PcmAudio> MixInputsAsync(IReadOnlyList<MixInput> inputs, CancellationToken cancellationToken)
    {
        var sourceContainer = _blobServiceClient.GetBlobContainerClient(SourceContainerName);

        // 1) Decode every source mp3 into float PCM. Honour the first
        //    sample's format as canonical; mismatched inputs would
        //    require resampling, which we don't have on Linux Alpine.
        var decoded = new List<DecodedSample>(inputs.Count);
        int? sampleRate = null;
        int? channels = null;
        foreach (var input in inputs)
        {
            var sample = await DecodeAsync(sourceContainer, input.SampleName, cancellationToken).ConfigureAwait(false);
            sampleRate ??= sample.SampleRate;
            channels ??= sample.Channels;
            if (sample.SampleRate != sampleRate.Value || sample.Channels != channels.Value)
            {
                throw new InvalidOperationException(
                    $"Source '{input.SampleName}' has format {sample.SampleRate}Hz/{sample.Channels}ch but the mix " +
                    $"uses {sampleRate.Value}Hz/{channels.Value}ch. All samples must share one format.");
            }
            if (input.Cents != 0)
            {
                int? maxFrames = input.DurationSeconds is { } d ? (int)Math.Round(d * sample.SampleRate) : null;
                sample = sample with { Samples = ShiftPitch(sample.Samples, sample.Channels, input.Cents, maxFrames) };
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

        // 3) Sum into a float accumulator, fading every note out.
        var accum = new float[totalLengthSamples * ch];
        var fadeLength = (int)Math.Round(FadeOutSeconds * sr);
        for (var i = 0; i < inputs.Count; i++)
        {
            var src = decoded[i].Samples;
            var startFrame = (int)Math.Round(inputs[i].StartTimeSeconds * sr);
            var srcFrames = src.Length / ch;
            var maxFrames = inputs[i].DurationSeconds is { } d
                ? (int)Math.Round(d * sr)
                : srcFrames;
            var useFrames = Math.Min(srcFrames, maxFrames);
            var fadeFrames = Math.Min(fadeLength, useFrames);
            var fadeStart = useFrames - fadeFrames;

            var dstOffset = startFrame * ch;
            for (var f = 0; f < useFrames; f++)
            {
                var gain = f < fadeStart ? 1f : (useFrames - f) / (float)fadeFrames;
                for (var c = 0; c < ch; c++)
                {
                    var k = f * ch + c;
                    accum[dstOffset + k] += src[k] * gain;
                }
            }
        }

        // 4) Scale the mix down when the notes add up past full scale.
        var peak = 0f;
        foreach (var s in accum)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }
        if (peak > MaxPeak)
        {
            var scale = MaxPeak / peak;
            for (var k = 0; k < accum.Length; k++)
            {
                accum[k] *= scale;
            }
        }

        _logger.LogDebug("Mixed {InputCount} sources ({Duration:F2}s)", inputs.Count, totalLengthSamples / (double)sr);
        return new PcmAudio(sr, ch, accum);
    }

    private static MemoryStream EncodeWav(PcmAudio audio)
    {
        var wavStream = new MemoryStream(44 + audio.Samples.Length * 2);
        WriteWavHeader(wavStream, audio.SampleRate, audio.Channels, audio.Samples.Length);
        WritePcm16(wavStream, audio.Samples);
        wavStream.Position = 0;
        return wavStream;
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

    private async Task<DecodedSample> DecodeAsync(
        BlobContainerClient sourceContainer,
        string sampleName,
        CancellationToken cancellationToken)
    {
        if (AudioSourceLibrary.IsSource(sampleName))
        {
            var audio = await _audioSources.ReadAsync(_audioSources.Resolve(sampleName), cancellationToken).ConfigureAwait(false);
            return new DecodedSample(audio.SampleRate, audio.Channels, audio.Samples);
        }

        using var ms = new MemoryStream();
        if (BundledSamples.IsBundled(sampleName))
        {
            await using var file = _bundledSamples.Open(sampleName);
            await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var blobClient = sourceContainer.GetBlobClient(sampleName);
            await blobClient.DownloadToAsync(ms, cancellationToken).ConfigureAwait(false);
        }
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
        // The mix is already within ±MaxPeak; the clamp only guards the
        // conversion against rounding.
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

    /// <summary>
    /// Plays <paramref name="samples"/> <paramref name="cents"/> higher or
    /// lower by reading them faster or slower, interpolating between the
    /// recorded samples with a windowed sinc. Stops when the source runs
    /// out or after <paramref name="maxFrames"/> frames.
    /// </summary>
    internal static float[] ShiftPitch(float[] samples, int channels, double cents, int? maxFrames)
    {
        var ratio = Math.Pow(2, cents / 1200);
        var sourceFrames = samples.Length / channels;
        var frames = Math.Max(0, (int)Math.Floor((sourceFrames - 1 - ResampleHalfWidth) / ratio));
        if (maxFrames is { } max)
        {
            frames = Math.Min(frames, max);
        }

        // Read faster and the source has to lose what would fold back above
        // the new Nyquist frequency.
        var kernel = ResampleKernel(Math.Min(1.0, 1.0 / ratio));
        const int taps = 2 * ResampleHalfWidth;
        var output = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var position = f * ratio;
            var whole = (int)position;
            var row = (int)Math.Round((position - whole) * ResamplePhases) * taps;
            var first = whole - ResampleHalfWidth + 1;
            for (var c = 0; c < channels; c++)
            {
                var sum = 0f;
                for (var t = Math.Max(0, -first); t < taps; t++)
                {
                    sum += kernel[row + t] * samples[(first + t) * channels + c];
                }
                output[f * channels + c] = sum;
            }
        }
        return output;
    }

    /// <summary>
    /// The weights of the <c>2 × ResampleHalfWidth</c> samples around each of
    /// <c>ResamplePhases + 1</c> positions between two samples: a low-pass
    /// sinc at <paramref name="cutoff"/> (a fraction of Nyquist) under a
    /// Blackman window, scaled so each position's weights add up to one.
    /// </summary>
    private static float[] ResampleKernel(double cutoff)
    {
        const int taps = 2 * ResampleHalfWidth;
        var kernel = new float[(ResamplePhases + 1) * taps];
        var weights = new double[taps];
        for (var p = 0; p <= ResamplePhases; p++)
        {
            var fraction = p / (double)ResamplePhases;
            var total = 0.0;
            for (var t = 0; t < taps; t++)
            {
                var distance = t - ResampleHalfWidth + 1 - fraction;
                var x = Math.PI * cutoff * distance;
                var sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(x) / x;
                var w = Math.PI * distance / ResampleHalfWidth;
                var window = 0.42 + 0.5 * Math.Cos(w) + 0.08 * Math.Cos(2 * w);
                weights[t] = sinc * window;
                total += weights[t];
            }
            for (var t = 0; t < taps; t++)
            {
                kernel[p * taps + t] = (float)(weights[t] / total);
            }
        }
        return kernel;
    }

    private string ComputePlanHash(IReadOnlyList<MixInput> inputs)
    {
        // Invariant, so a plan maps to the same blob whatever the request culture
        // (pt-BR and fr-CA would otherwise write "0,4000"). Unshifted notes keep
        // the names they had before notes could be shifted, and an audio source
        // adds its version, so a replaced recording is never served from old mixes.
        var canonical = MixVersion + ":" + string.Join("|", inputs
            .Select(i => string.Create(CultureInfo.InvariantCulture,
                $"{i.SampleName}{SourceVersion(i.SampleName)}@{i.StartTimeSeconds:F4}/{i.DurationSeconds?.ToString("F4", CultureInfo.InvariantCulture) ?? "*"}{(i.Cents == 0 ? "" : $"~{i.Cents:F2}")}")));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string SourceVersion(string sampleName) =>
        AudioSourceLibrary.IsSource(sampleName) ? "#" + _audioSources.Resolve(sampleName).Version : "";

    private sealed record DecodedSample(int SampleRate, int Channels, float[] Samples);

    private sealed record MixMemo(MixedAudio Mix, DateTimeOffset WrittenAt);
}
