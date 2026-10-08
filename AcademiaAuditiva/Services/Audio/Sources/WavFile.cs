using System.Buffers.Binary;
using System.Text;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>Interleaved samples in [-1, 1].</summary>
public sealed record PcmAudio(int SampleRate, int Channels, float[] Samples)
{
    public int Frames => Samples.Length / Channels;

    public double DurationSeconds => Frames / (double)SampleRate;
}

/// <summary>
/// Reads and writes integer PCM WAV files. Reading is strict: anything but a well-formed 16- or
/// 24-bit integer PCM file is rejected with the reason, never converted.
/// </summary>
public static class WavFile
{
    private const ushort FormatPcm = 1;
    private const ushort FormatExtensible = 0xFFFE;

    // The tail of KSDATAFORMAT_SUBTYPE_PCM; its first two bytes are the format tag.
    private static readonly byte[] PcmSubFormatTail =
        [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    /// <exception cref="InvalidDataException">The bytes are not a 16- or 24-bit integer PCM WAV file.</exception>
    public static PcmAudio Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !Is(bytes[..4], "RIFF") || !Is(bytes.Slice(8, 4), "WAVE"))
            throw new InvalidDataException("Not a WAV file: it doesn't start with RIFF....WAVE.");

        (int Channels, int SampleRate, int Bits)? format = null;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            var body = position + 8;
            if (size > (uint)(bytes.Length - body))
                throw new InvalidDataException($"The '{Encoding.ASCII.GetString(id)}' chunk is cut short.");

            var chunk = bytes.Slice(body, (int)size);
            if (Is(id, "fmt "))
            {
                format = ReadFormat(chunk);
            }
            else if (Is(id, "data"))
            {
                if (format is not { } f)
                    throw new InvalidDataException("The data chunk comes before the fmt chunk.");
                return Decode(chunk, f.Channels, f.SampleRate, f.Bits);
            }

            // Chunks are padded to an even size.
            position = body + (int)size + (int)(size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk.");
    }

    /// <summary>Writes <paramref name="audio"/> as a 16-bit PCM WAV file, clamping it to full scale.</summary>
    public static byte[] Write(PcmAudio audio)
    {
        var dataSize = audio.Samples.Length * 2;
        var bytes = new byte[44 + dataSize];
        var header = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataSize);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], FormatPcm);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)audio.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], audio.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], audio.SampleRate * audio.Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(audio.Channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataSize);

        for (var i = 0; i < audio.Samples.Length; i++)
        {
            var value = Math.Clamp(Math.Round(audio.Samples[i] * 32768.0), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(header[(44 + i * 2)..], (short)value);
        }
        return bytes;
    }

    private static (int Channels, int SampleRate, int Bits) ReadFormat(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 16)
            throw new InvalidDataException("The fmt chunk is too short.");

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
        var byteRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[8..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);

        if (tag == FormatExtensible)
        {
            if (chunk.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]) != FormatPcm
                || !chunk.Slice(26, 14).SequenceEqual(PcmSubFormatTail))
                throw new InvalidDataException("The WAV file is not integer PCM.");
        }
        else if (tag != FormatPcm)
        {
            throw new InvalidDataException($"The WAV file is not integer PCM (format {tag}).");
        }

        if (bits is not (16 or 24))
            throw new InvalidDataException($"The WAV file has {bits}-bit samples; only 16 and 24 bits are read.");
        if (channels == 0 || sampleRate <= 0)
            throw new InvalidDataException("The WAV file has no channels or no sample rate.");
        if (blockAlign != channels * bits / 8 || byteRate != sampleRate * blockAlign)
            throw new InvalidDataException("The WAV file's block alignment or byte rate doesn't match its format.");

        return (channels, sampleRate, bits);
    }

    private static PcmAudio Decode(ReadOnlySpan<byte> data, int channels, int sampleRate, int bits)
    {
        var bytesPerSample = bits / 8;
        if (data.Length % (bytesPerSample * channels) != 0)
            throw new InvalidDataException("The WAV data ends in the middle of a frame.");

        var samples = new float[data.Length / bytesPerSample];
        for (var i = 0; i < samples.Length; i++)
        {
            var at = data.Slice(i * bytesPerSample, bytesPerSample);
            samples[i] = bits == 16
                ? BinaryPrimitives.ReadInt16LittleEndian(at) / 32768f
                : (at[0] | at[1] << 8 | (sbyte)at[2] << 16) / 8388608f;
        }
        return new PcmAudio(sampleRate, channels, samples);
    }

    private static bool Is(ReadOnlySpan<byte> bytes, string text) => bytes.SequenceEqual(Encoding.ASCII.GetBytes(text));
}
