using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AcademiaAuditiva.Services.Audio;
using static AcademiaAuditiva.UnitTests.TestWav;

namespace AcademiaAuditiva.UnitTests;

public class ClipVariationTests
{
    private static readonly short[] Signal = [0, 1000, -1000, 12345, -12345, short.MaxValue, short.MinValue, 7, -7, 0];

    [Fact]
    public void Apply_WritesACanonicalHeader_AndPadsWholeFramesOfSilence()
    {
        var output = new ClipVariation(Gain: 1f, LeadInMilliseconds: 25, TailMilliseconds: 10, DitherSeed: 1)
            .Apply(Pcm16(channels: 2, sampleRate: 44_100, Signal))!;

        // 25 ms at 44.1 kHz is 1102.5 frames; silence is added in whole stereo frames.
        const int leadSamples = 1102 * 2, tailSamples = 441 * 2;
        var dataBytes = (leadSamples + Signal.Length + tailSamples) * 2;
        output.Should().HaveCount(44 + dataBytes);
        Encoding.ASCII.GetString(output, 0, 4).Should().Be("RIFF");
        BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4)).Should().Be(36 + dataBytes);
        Encoding.ASCII.GetString(output, 8, 8).Should().Be("WAVEfmt ");
        BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(16)).Should().Be(16);
        BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(20)).Should().Be(1);
        BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(22)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(24)).Should().Be(44_100);
        BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(28)).Should().Be(44_100 * 4);
        BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(32)).Should().Be(4);
        BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(34)).Should().Be(16);
        Encoding.ASCII.GetString(output, 36, 4).Should().Be("data");

        var samples = ReadSamples(output);
        samples.Skip(leadSamples).Take(Signal.Length).Zip(Signal)
            .Should().OnlyContain(p => Math.Abs(p.First - p.Second) <= 1);
        samples.Take(leadSamples).Concat(samples.Skip(leadSamples + Signal.Length))
            .Should().OnlyContain(s => Math.Abs((int)s) <= 1);
    }

    [Theory]
    [InlineData(ClipVariation.MinGain)]
    [InlineData(0.9f)]
    [InlineData(1f)]
    public void Apply_ScalesTheSignal_AndOnlyAddsDitherAroundIt(float gain)
    {
        var output = new ClipVariation(gain, LeadInMilliseconds: 1, TailMilliseconds: 1, DitherSeed: 42)
            .Apply(Pcm16(channels: 1, sampleRate: 8_000, Signal))!;

        const int lead = 8;
        var samples = ReadSamples(output);
        samples.Should().HaveCount(lead + Signal.Length + 8);
        for (var i = 0; i < samples.Length; i++)
        {
            var source = i - lead;
            var expected = source >= 0 && source < Signal.Length ? Signal[source] * gain : 0f;
            Math.Abs(samples[i] - expected).Should().BeLessThanOrEqualTo(1.5f, $"sample {i} is the source times the gain plus at most 1 LSB of dither");
        }
    }

    [Fact]
    public void CreateRandom_DrawsParametersWithinTheirBounds()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var variation = ClipVariation.CreateRandom();
            variation.Gain.Should().BeInRange(ClipVariation.MinGain, 1f);
            variation.LeadInMilliseconds.Should().BeInRange(0, ClipVariation.MaxLeadInMilliseconds);
            variation.TailMilliseconds.Should().BeInRange(0, ClipVariation.MaxTailMilliseconds);
        }
    }

    [Fact]
    public void RandomVariations_NeverDeliverTheSameBytesOrAlwaysTheSameLength()
    {
        var wav = Pcm16(channels: 1, sampleRate: 8_000, Signal);

        var deliveries = Enumerable.Range(0, 50).Select(_ => ClipVariation.CreateRandom().Apply(wav)!).ToList();

        deliveries.Select(d => Convert.ToHexString(SHA256.HashData(d))).Should().OnlyHaveUniqueItems();
        deliveries.Select(d => d.Length).Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Apply_SkipsOtherChunks_IncludingOddSizedOnes()
    {
        var wav = Riff(Fmt(), Chunk("LIST", [1, 2, 3]), Chunk("fact", new byte[4]), Chunk("data", Pcm(Signal)));

        var samples = ReadSamples(new ClipVariation(1f, 0, 0, DitherSeed: 3).Apply(wav)!);

        samples.Should().HaveCount(Signal.Length);
        samples.Zip(Signal).Should().OnlyContain(p => Math.Abs(p.First - p.Second) <= 1);
    }

    [Fact]
    public void Apply_ClampsADataChunkThatClaimsMoreThanTheFile_ToWholeFrames()
    {
        // Streamed WAVs often declare 0xFFFFFFFF; here a stray byte also ends the file mid-frame.
        var wav = Riff(Fmt(channels: 2), Chunk("data", [.. Pcm(Signal), 0x7F], declaredSize: uint.MaxValue));

        var samples = ReadSamples(new ClipVariation(1f, 0, 0, DitherSeed: 4).Apply(wav)!);

        samples.Should().HaveCount(Signal.Length);
    }

    public static TheoryData<string, byte[]> NotPcm16Wav => new()
    {
        { "empty", [] },
        { "mp3", [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 0, 0xFF, 0xFB, 0x90, 0x00] },
        { "riff that is not wave", [.. "RIFF"u8, 4, 0, 0, 0, .. "AVI "u8] },
        { "8-bit pcm", Riff(Fmt(bits: 8), Chunk("data", new byte[16])) },
        { "float samples", Riff(Fmt(format: 3, bits: 32), Chunk("data", new byte[16])) },
        { "no channels", Riff(Fmt(channels: 0), Chunk("data", new byte[16])) },
        { "implausible sample rate", Riff(Fmt(sampleRate: 1_000), Chunk("data", new byte[16])) },
        { "data before fmt", Riff(Chunk("data", new byte[16]), Fmt()) },
        { "no data chunk", Riff(Fmt()) },
        { "truncated fmt", Riff(Fmt())[..30] },
        { "chunk running past the end", Riff(Fmt(), Chunk("LIST", new byte[4], declaredSize: 4_000), Chunk("data", new byte[16])) },
    };

    [Theory]
    [MemberData(nameof(NotPcm16Wav))]
    public void Apply_RejectsAnythingButPcm16Wav(string reason, byte[] input)
    {
        ClipVariation.CreateRandom().Apply(input).Should().BeNull(reason);
    }
}
