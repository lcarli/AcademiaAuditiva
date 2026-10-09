using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// Makes every delivery of a round clip unique. The mixer caches clips by
/// note plan, so the same notes would otherwise always produce the same bytes,
/// and a script could learn which bytes (or which file length) belong to which
/// answer — from answered rounds, free practice or the explore page — and look
/// the answer up. Each response gets a random gain, a short random lead-in and
/// tail of silence, and inaudible dither, so neither the bytes nor the length
/// repeat. Recognising the sound itself stays the student's job.
/// </summary>
/// <param name="Gain">
/// Linear gain applied to every sample (≤ 1, so it never clips). The audio
/// endpoint uses 1 for processed clips of the audio track, whose level is the
/// question (<see cref="AudioMixerService.ProcessedBlobPrefix"/>).
/// </param>
/// <param name="LeadInMilliseconds">Silence added before the clip.</param>
/// <param name="TailMilliseconds">Silence added after the clip.</param>
/// <param name="DitherSeed">Seed of the ±1 LSB triangular dither.</param>
public readonly record struct ClipVariation(float Gain, int LeadInMilliseconds, int TailMilliseconds, int DitherSeed)
{
    /// <summary>About −2 dB: quiet enough to vary, too small to notice between rounds.</summary>
    public const float MinGain = 0.79f;

    public const int MaxLeadInMilliseconds = 25;
    public const int MaxTailMilliseconds = 250;

    private const int HeaderBytes = 44;

    /// <summary>Draws unpredictable parameters from the system CSPRNG.</summary>
    public static ClipVariation CreateRandom() => new(
        MinGain + (1f - MinGain) * RandomNumberGenerator.GetInt32(0, 10_001) / 10_000f,
        RandomNumberGenerator.GetInt32(0, MaxLeadInMilliseconds + 1),
        RandomNumberGenerator.GetInt32(0, MaxTailMilliseconds + 1),
        RandomNumberGenerator.GetInt32(int.MaxValue));

    /// <summary>
    /// Returns a new 16-bit PCM WAV with this variation applied, or <c>null</c>
    /// when <paramref name="wav"/> is not a 16-bit PCM WAV.
    /// </summary>
    public byte[]? Apply(ReadOnlySpan<byte> wav)
    {
        if (!TryReadPcm16(wav, out var channels, out var sampleRate, out var data))
        {
            return null;
        }

        var frameBytes = 2 * channels;
        var leadBytes = (int)((long)sampleRate * LeadInMilliseconds / 1000) * frameBytes;
        var tailBytes = (int)((long)sampleRate * TailMilliseconds / 1000) * frameBytes;
        var dataBytes = leadBytes + data.Length + tailBytes;

        var output = new byte[HeaderBytes + dataBytes];
        WriteHeader(output, channels, sampleRate, dataBytes);

        // The silence is dithered too, so the clip boundaries aren't exact runs of zeros.
        var dither = new Random(DitherSeed);
        var samples = output.AsSpan(HeaderBytes);
        for (var i = 0; i < dataBytes; i += 2)
        {
            var source = i - leadBytes;
            var sample = source >= 0 && source < data.Length
                ? BinaryPrimitives.ReadInt16LittleEndian(data.Slice(source, 2))
                : (short)0;
            var value = sample * Gain + (float)(dither.NextDouble() - dither.NextDouble());
            var rounded = Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(samples.Slice(i, 2), (short)rounded);
        }

        return output;
    }

    /// <summary>
    /// Walks the RIFF chunks for a PCM <c>fmt </c> chunk with 16-bit samples
    /// followed by the <c>data</c> chunk. A <c>data</c> size larger than the
    /// file (streamed WAVs) is clamped to what is there.
    /// </summary>
    private static bool TryReadPcm16(
        ReadOnlySpan<byte> wav,
        out int channels,
        out int sampleRate,
        out ReadOnlySpan<byte> data)
    {
        channels = 0;
        sampleRate = 0;
        data = default;

        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var haveFormat = false;
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var id = wav.Slice(offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(offset + 4, 4));
            var body = offset + 8;

            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > wav.Length)
                {
                    return false;
                }
                var format = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(body + 4, 4));
                var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 14, 2));
                if (format != 1 || bitsPerSample != 16 || channels is < 1 or > 8 || sampleRate is < 8_000 or > 192_000)
                {
                    return false;
                }
                haveFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!haveFormat)
                {
                    return false;
                }
                var length = (int)Math.Min(size, (uint)(wav.Length - body));
                length -= length % (2 * channels);
                data = wav.Slice(body, length);
                return true;
            }

            // Chunks are word-aligned: odd sizes carry one pad byte.
            var next = (long)body + size + (size & 1);
            if (next > wav.Length)
            {
                return false;
            }
            offset = (int)next;
        }

        return false;
    }

    private static void WriteHeader(Span<byte> header, int channels, int sampleRate, int dataBytes)
    {
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);
    }
}
