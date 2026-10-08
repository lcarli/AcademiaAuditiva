using System.Buffers.Binary;
using System.Text;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Audio sources are read as they are, never converted: a file that is not 16- or 24-bit
/// integer PCM is refused with the reason, so a bad download can't slip into the catalog.
/// </summary>
public class WavFileTests
{
    [Fact]
    public void Written16Bit_ReadsBackTheSameSamples()
    {
        var random = new Random(7);
        var samples = Enumerable.Range(0, 2000).Select(_ => (float)(random.Next(-32768, 32768) / 32768.0)).ToArray();
        var audio = new PcmAudio(44100, 2, samples);

        var read = WavFile.Read(WavFile.Write(audio));

        read.SampleRate.Should().Be(44100);
        read.Channels.Should().Be(2);
        read.Frames.Should().Be(1000);
        read.Samples.Should().Equal(samples);
    }

    [Fact]
    public void Write_ClampsPastFullScale()
    {
        var read = WavFile.Read(WavFile.Write(new PcmAudio(44100, 1, [1.5f, -1.5f, 0.5f])));

        read.Samples.Should().Equal(32767 / 32768f, -1f, 0.5f);
    }

    [Fact]
    public void Reads24BitSamples()
    {
        var data = new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0x80, 0xFF, 0xFF, 0xFF };

        WavFile.Read(Wav(bits: 24, data: data)).Samples.Should().Equal(0.5f, -1f, -1 / 8388608f);
    }

    [Fact]
    public void ReadsExtensiblePcm_AndSkipsOtherChunks()
    {
        var extensible = Format(0xFFFE, channels: 1, 44100, 16, extra: [
            22, 0, 16, 0, 4, 0, 0, 0,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71]);
        var bytes = Riff(Chunk("fmt ", extensible), Chunk("LIST", [1, 2, 3]), Chunk("data", [0x00, 0x40, 0x00, 0xC0]));

        WavFile.Read(bytes).Samples.Should().Equal(0.5f, -0.5f);
    }

    public static TheoryData<string, byte[]> Refused => new()
    {
        { "start with RIFF", Encoding.ASCII.GetBytes("ID3 not a wav file") },
        { "not integer PCM (format 3)", Riff(Chunk("fmt ", Format(3, 1, 44100, 32)), Chunk("data", new byte[8])) },
        { "8-bit", Wav(bits: 8, data: [1, 2]) },
        { "32-bit", Wav(bits: 32, data: new byte[8]) },
        { "data chunk comes before", Riff(Chunk("data", new byte[4]), Chunk("fmt ", Format(1, 1, 44100, 16))) },
        { "no data chunk", Riff(Chunk("fmt ", Format(1, 1, 44100, 16))) },
        { "cut short", Wav(bits: 16, data: new byte[8])[..^2] },
        { "middle of a frame", Wav(bits: 16, channels: 2, data: new byte[6]) },
        { "block alignment", Riff(Chunk("fmt ", Format(1, 2, 44100, 16, blockAlign: 2)), Chunk("data", new byte[8])) },
        { "not integer PCM", Riff(Chunk("fmt ", Format(0xFFFE, 1, 44100, 16, extra: new byte[24])), Chunk("data", new byte[4])) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void RefusesAnythingElse_SayingWhy(string reason, byte[] bytes)
    {
        FluentActions.Invoking(() => WavFile.Read(bytes))
            .Should().Throw<InvalidDataException>().WithMessage($"*{reason}*");
    }

    private static byte[] Wav(int bits, byte[] data, int channels = 1) =>
        Riff(Chunk("fmt ", Format(1, channels, 44100, bits)), Chunk("data", data));

    private static byte[] Format(ushort tag, int channels, int rate, int bits, int? blockAlign = null, byte[]? extra = null)
    {
        var align = blockAlign ?? channels * bits / 8;
        var chunk = new byte[16 + (extra?.Length ?? 0)];
        BinaryPrimitives.WriteUInt16LittleEndian(chunk, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), rate);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(8), rate * align);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(12), (ushort)align);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(14), (ushort)bits);
        extra?.CopyTo(chunk, 16);
        return chunk;
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new byte[8 + body.Length + body.Length % 2];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(4 + body.Length), .. Encoding.ASCII.GetBytes("WAVE"), .. body];
    }
}
