using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

public class CompressorTests
{
    [Fact]
    public void SettledCompression_FollowsTheThresholdAndRatio_WithoutMakeupGain()
    {
        var input = ConstantPeak(-8, 2);
        var original = input.Samples.ToArray();
        var output = AudioProcessing.Process(input, [new CompressorProcessor(-20, 4, 1, 100)]);
        var peakDb = 20 * Math.Log10(output.Samples[^1]);

        peakDb.Should().BeApproximately(-20 + 12 / 4.0, 0.001);
        input.Samples.Should().Equal(original);
        output.Samples.Should().OnlyContain(sample => float.IsFinite(sample) && Math.Abs(sample) <= 1);
        AudioProcessing.Process(input, [new CompressorProcessor(-20, 1, 1, 100)])
            .Samples.Should().Equal(original);
    }

    [Fact]
    public void StereoLink_PreservesChannelBalance_AndSilentChannels()
    {
        var input = ConstantPeak(-8, 1);
        var stereo = input with
        {
            Channels = 2,
            Samples = input.Samples.SelectMany(sample => new[] { sample, sample / 4 }).ToArray(),
        };
        var output = AudioProcessing.Process(stereo, [new CompressorProcessor(-20, 4, 2, 100)]);
        for (var frame = 0; frame < output.Frames; frame++)
            output.Samples[frame * 2 + 1].Should().Be(output.Samples[frame * 2] / 4);
        var silent = stereo with { Samples = new float[stereo.Samples.Length] };
        AudioProcessing.Process(silent, [new CompressorProcessor(-20, 4, 2, 100)])
            .Samples.Should().OnlyContain(sample => sample == 0);
    }

    [Fact]
    public void AttackAndRelease_AreExponentialTimeConstantsOfThePeakEnvelope()
    {
        var sr = AudioSourceRules.SampleRate;
        var input = new PcmAudio(sr, 1,
            Enumerable.Repeat(0.5f, sr).Concat(Enumerable.Repeat(0.001f, sr)).ToArray());
        var output = AudioProcessing.Process(input, [new CompressorProcessor(-20, 4, 10, 100)]);
        var attackGain = 20 * Math.Log10(output.Samples[sr / 100 - 1] / 0.5);
        var releaseGain = 20 * Math.Log10(output.Samples[sr + sr / 10 - 1] / 0.001);
        attackGain.Should().BeApproximately(-Math.Max(0, 20 * Math.Log10(0.5 * (1 - Math.Exp(-1))) + 20) * 0.75, 0.002);
        releaseGain.Should().BeApproximately(-Math.Max(0, 20 * Math.Log10(0.001 + 0.499 * Math.Exp(-1)) + 20) * 0.75, 0.002);
    }

    [Theory]
    [InlineData(double.NaN, 4, 2, 100)]
    [InlineData(-61, 4, 2, 100)]
    [InlineData(1, 4, 2, 100)]
    [InlineData(-20, 0.9, 2, 100)]
    [InlineData(-20, double.PositiveInfinity, 2, 100)]
    [InlineData(-20, 4, -0.1, 100)]
    [InlineData(-20, 4, 201, 100)]
    [InlineData(-20, 4, 2, 4)]
    [InlineData(-20, 4, 2, 2001)]
    public void InvalidParameters_AreRejected(double threshold, double ratio, double attack, double release)
    {
        FluentActions.Invoking(() => AudioProcessing.Process(ConstantPeak(-8, 1),
            [new CompressorProcessor(threshold, ratio, attack, release)]))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CacheKey_IncludesEveryParameterAndStageOrder()
    {
        var compressor = new CompressorProcessor(-24, 2, 3, 100);
        var baseline = Describe(compressor);
        foreach (var changed in new[]
        {
            compressor with { ThresholdDbfs = -25 }, compressor with { Ratio = 3 },
            compressor with { AttackMs = 4 }, compressor with { ReleaseMs = 101 },
        })
            Describe(changed).Should().NotBe(baseline);
        var before = new AudioProcessingPlan("drum-loop", [new GainProcessor(-6), compressor]);
        var after = new AudioProcessingPlan("drum-loop", [compressor, new GainProcessor(-6)]);
        AudioProcessing.Describe(before, "v").Should().NotBe(AudioProcessing.Describe(after, "v"));
    }

    [Fact]
    public void ZeroAttack_UsesTheCurrentPeakImmediately_WithoutClippingOrLookahead()
    {
        var input = new PcmAudio(AudioSourceRules.SampleRate, 1, [0, 0.5f, 0]);
        var output = AudioProcessing.Process(input, [new CompressorProcessor(-20, 4, 0, 20)]);
        var expectedDb = -20 + (20 * Math.Log10(0.5) + 20) / 4;
        (20 * Math.Log10(output.Samples[1])).Should().BeApproximately(expectedDb, 0.001);
        output.Samples[0].Should().Be(0);
        output.Samples[2].Should().Be(0);
    }

    private static string Describe(CompressorProcessor compressor) =>
        AudioProcessing.Describe(new AudioProcessingPlan("drum-loop", [compressor]), "v");

    private static PcmAudio ConstantPeak(double db, int seconds) =>
        new(AudioSourceRules.SampleRate, 1,
            Enumerable.Repeat((float)AudioProcessing.LinearGain(db), AudioSourceRules.SampleRate * seconds).ToArray());
}
