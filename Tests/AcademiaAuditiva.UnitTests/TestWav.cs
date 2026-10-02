using System.Buffers.Binary;
using System.Text;

namespace AcademiaAuditiva.UnitTests;

/// <summary>Builds and reads RIFF/WAVE files for the audio tests.</summary>
internal static class TestWav
{
    public static byte[] Pcm16(int channels, int sampleRate, short[] samples) =>
        Riff(Fmt(channels, sampleRate), Chunk("data", Pcm(samples)));

    public static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        var riff = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteInt32LittleEndian(riff.AsSpan(4), 4 + body.Length);
        "WAVE"u8.CopyTo(riff.AsSpan(8));
        body.CopyTo(riff, 12);
        return riff;
    }

    /// <summary>A chunk with its pad byte when the body has an odd length.</summary>
    public static byte[] Chunk(string id, byte[] body, uint? declaredSize = null)
    {
        var chunk = new byte[8 + body.Length + body.Length % 2];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), declaredSize ?? (uint)body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    public static byte[] Fmt(int channels = 1, int sampleRate = 8_000, int format = 1, int bits = 16)
    {
        var body = new byte[16];
        BinaryPrimitives.WriteInt16LittleEndian(body, (short)format);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), sampleRate * channels * bits / 8);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(12), (short)(channels * bits / 8));
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(14), (short)bits);
        return Chunk("fmt ", body);
    }

    public static byte[] Pcm(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }
        return bytes;
    }

    /// <summary>Reads the samples of a canonical 44-byte-header WAV.</summary>
    public static short[] ReadSamples(byte[] wav)
    {
        var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        dataBytes.Should().Be(wav.Length - 44, "the data chunk fills the rest of the file");
        var samples = new short[dataBytes / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2));
        }
        return samples;
    }
}
