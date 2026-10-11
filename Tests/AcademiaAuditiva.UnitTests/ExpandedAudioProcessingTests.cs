using System.Diagnostics;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

public class ExpandedAudioProcessingTests
{
    [Theory]
    [InlineData(0, 0.3, 0.3)]
    [InlineData(1, 0.4, 0.2)]
    [InlineData(2, 0.5, 0.1)]
    public void Width_PreservesMidAndScalesSide(double width, double left, double right)
    {
        var source = new PcmAudio(44100, 2, [0.4f, 0.2f]);
        var result = AudioProcessing.Process(source, [new StereoWidthProcessor(width)]);
        result.Samples[0].Should().BeApproximately((float)left, 1e-7f);
        result.Samples[1].Should().BeApproximately((float)right, 1e-7f);
        source.Samples.Should().Equal(0.4f, 0.2f);
    }

    [Fact]
    public void Polarity_ChangesOnlyRightSign_AndCancelsCoherentMono()
    {
        var source = new PcmAudio(44100, 2, [0.3f, 0.3f, -0.2f, -0.2f]);
        var result = AudioProcessing.Process(source, [new ChannelPolarityProcessor(true)]);
        result.Samples.Should().Equal(0.3f, -0.3f, -0.2f, 0.2f);
        AudioProcessing.Process(source, [new ChannelPolarityProcessor(false)]).Samples.Should().Equal(source.Samples);
        for (var frame = 0; frame < result.Frames; frame++)
            (result.Samples[2 * frame] + result.Samples[2 * frame + 1]).Should().Be(0);
    }

    [Fact]
    public void Echo_HasExactDelayFeedbackAndIndependentChannelState()
    {
        var source = Impulse(2);
        var result = AudioProcessing.Process(source, [new EchoProcessor(100, 0.3, 0.5)]);
        result.Samples[0].Should().BeApproximately(0.2f, 1e-7f);
        result.Samples[4410 * 2].Should().BeApproximately(0.2f, 1e-7f);
        result.Samples[8820 * 2].Should().BeApproximately(0.06f, 1e-7f);
        result.Samples.Where((_, index) => index % 2 == 1).Should().OnlyContain(sample => sample == 0);
        result.Frames.Should().Be(source.Frames);
    }

    [Fact]
    public void Reverb_PreDelayIsAnExactShift_AndLongerDecayRetainsMoreLateEnergy()
    {
        var source = Impulse(1);
        var shortTail = AudioProcessing.Process(source, [new ReverbProcessor(0.4, 0, 1)]);
        var longTail = AudioProcessing.Process(source, [new ReverbProcessor(3, 0, 1)]);
        var delayed = AudioProcessing.Process(source, [new ReverbProcessor(3, 100, 1)]);
        delayed.Samples.Take(4410).Should().OnlyContain(sample => sample == 0);
        delayed.Samples.Skip(4410).Should().Equal(longTail.Samples.Take(longTail.Samples.Length - 4410));
        var start = 44100 / 2;
        var stop = 44100;
        var shortEnergy = shortTail.Samples[start..stop].Sum(sample => (double)sample * sample);
        var longEnergy = longTail.Samples[start..stop].Sum(sample => (double)sample * sample);
        (10 * Math.Log10(longEnergy / shortEnergy)).Should().BeGreaterThan(10);
        AudioProcessing.Process(source, [new ReverbProcessor(3, 0, 1)]).Samples.Should().Equal(longTail.Samples);
        source.Samples[0].Should().Be(0.4f);
        source.Samples.Skip(1).Should().OnlyContain(sample => sample == 0);
    }

    [Fact]
    public void Reverb_UsesIndependentStereoState_AndWetZeroIsAWire()
    {
        var source = Impulse(2);
        var output = AudioProcessing.Process(source, [new ReverbProcessor(1, 20, 0.6)]);
        output.Samples.Where((_, index) => index % 2 == 1).Should().OnlyContain(sample => sample == 0);
        AudioProcessing.Process(source, [new ReverbProcessor(1, 20, 0)]).Samples.Should().Equal(source.Samples);
        AudioProcessing.Process(source, [new EchoProcessor(200, 0.3, 0)]).Samples.Should().Equal(source.Samples);
    }

    [Fact]
    public void Saturation_IsOddBoundedAndAddsMeasuredHarmonics()
    {
        var source = ParametricEqTests.Tone(1000);
        var boosted = AudioProcessing.Process(source, [new GainProcessor(12)]);
        var result = AudioProcessing.Process(boosted, [new SaturationProcessor(-30)]);
        result.Samples.Max(sample => Math.Abs(sample)).Should().BeLessThan((float)AudioProcessing.LinearGain(-30));
        result.Samples.Average(sample => (double)sample).Should().BeApproximately(0, 1e-6);
        var spectrum = GuessFrequencyTests.Spectrum(result);
        var fundamental = GuessFrequencyTests.BandBins(1000, 44100).Sum(index => spectrum[index]);
        var thirdHarmonic = GuessFrequencyTests.BandBins(3000, 44100).Sum(index => spectrum[index]);
        (thirdHarmonic / fundamental).Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void FadeOut_OnlyChangesTheFinalFrames_AndEndsBothChannelsAtZero()
    {
        var input = new PcmAudio(44100, 2, Enumerable.Repeat(0.2f, 44100 * 2).ToArray());
        var output = AudioProcessing.Process(input, [new FadeOutProcessor(100)]);
        output.Samples.Take(2 * (44100 - 4410)).Should().OnlyContain(sample => sample == 0.2f);
        output.Samples[^2..].Should().Equal(0, 0);
        output.Samples[2 * (44100 - 4410) + 200].Should().BeLessThan(0.2f).And.BeGreaterThan(0);
    }

    [Fact]
    public void CacheKey_DistinguishesEveryNewParameterAndOrder()
    {
        AudioProcessor[] processors =
        [
            new StereoWidthProcessor(0.9), new StereoWidthProcessor(1.1),
            new ChannelPolarityProcessor(false), new ChannelPolarityProcessor(true),
            new EchoProcessor(100, 0.3, 0.5), new EchoProcessor(101, 0.3, 0.5),
            new EchoProcessor(100, 0.4, 0.5), new EchoProcessor(100, 0.3, 0.6),
            new ReverbProcessor(1, 10, 0.5), new ReverbProcessor(2, 10, 0.5),
            new ReverbProcessor(1, 11, 0.5), new ReverbProcessor(1, 10, 0.6),
            new SaturationProcessor(-20), new SaturationProcessor(-21),
            new FadeOutProcessor(15), new FadeOutProcessor(16),
        ];
        processors.Select(processor => AudioProcessing.Describe(new("source", [processor]), "hash"))
            .Should().OnlyHaveUniqueItems();
        AudioProcessing.Describe(new("source", [processors[8], processors[4]]), "hash")
            .Should().NotBe(AudioProcessing.Describe(new("source", [processors[4], processors[8]]), "hash"));
    }

    [Theory]
    [InlineData(0, 60, 20, 350)]
    [InlineData(1, 30, 40, 200)]
    [InlineData(3, 15, 70, 130)]
    public void CompressorProfiles_ControlAttackAndRecoveryInTheRequestedDirection(
        double fastAttack, double slowAttack, double fastRelease, double slowRelease)
    {
        var samples = new float[44100 * 2];
        Array.Fill(samples, 0.001f);
        Array.Fill(samples, 0.5f, 44100 / 2, 44100 / 20);
        var input = new PcmAudio(44100, 1, samples);
        var fast = AudioProcessing.Process(input, [new CompressorProcessor(-28, 6, fastAttack, 80)]);
        var slow = AudioProcessing.Process(input, [new CompressorProcessor(-28, 6, slowAttack, 80)]);
        var leadingFrame = 44100 / 2 + 220;
        (20 * Math.Log10(slow.Samples[leadingFrame] / fast.Samples[leadingFrame])).Should().BeGreaterThan(3);
        fast = AudioProcessing.Process(input, [new CompressorProcessor(-28, 6, 0, fastRelease)]);
        slow = AudioProcessing.Process(input, [new CompressorProcessor(-28, 6, 0, slowRelease)]);
        var recoveryFrame = 44100 / 2 + 44100 / 20 + 4410;
        (20 * Math.Log10(fast.Samples[recoveryFrame] / slow.Samples[recoveryFrame])).Should().BeGreaterThan(2);
    }

    public static TheoryData<AudioProcessor> InvalidParameters => new()
    {
        new StereoWidthProcessor(-0.1), new StereoWidthProcessor(2.1), new StereoWidthProcessor(double.NaN),
        new EchoProcessor(19, 0.3, 0.5), new EchoProcessor(1001, 0.3, 0.5), new EchoProcessor(100, -0.1, 0.5),
        new EchoProcessor(100, 0.751, 0.5), new EchoProcessor(100, 0.3, double.NaN),
        new ReverbProcessor(0.19, 0, 0.5), new ReverbProcessor(4.01, 0, 0.5),
        new ReverbProcessor(1, -1, 0.5), new ReverbProcessor(1, 201, 0.5),
        new ReverbProcessor(1, 0, double.PositiveInfinity), new ReverbProcessor(double.NaN, 0, 0.5),
        new SaturationProcessor(-60.1), new SaturationProcessor(0.1), new SaturationProcessor(double.NegativeInfinity),
        new FadeOutProcessor(4.9), new FadeOutProcessor(501), new FadeOutProcessor(double.NaN),
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void InvalidParameters_AreRefusedBeforeProcessing(AudioProcessor processor) =>
        FluentActions.Invoking(() => AudioProcessing.Process(Impulse(2), [processor]))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StereoOnlyProcessors_RefuseMono(bool polarity) =>
        FluentActions.Invoking(() => AudioProcessing.Process(Impulse(1),
            polarity ? [new ChannelPolarityProcessor(true)] : [new StereoWidthProcessor(1)]))
            .Should().Throw<ArgumentException>();

    [Fact]
    [Trait("Category", "AudioDspBudget")]
    public void LargestMixedChain_StaysWithinTimeAndMemoryBudgets()
    {
        var input = new PcmAudio(44100, 1,
            [.. Enumerable.Range(0, 20 * 44100).Select(frame => (float)(0.08 * Math.Sin(2 * Math.PI * 440 * frame / 44100)))]);
        AudioProcessor[] processors =
        [
            new PanProcessor(0.2), new CompressorProcessor(-28, 3, 1, 80),
            new StereoWidthProcessor(1.3), new ChannelPolarityProcessor(true),
            new EchoProcessor(250, 0.3, 0.35), new ReverbProcessor(3, 100, 0.6),
            new SaturationProcessor(-24), new LoudnessMatchProcessor(-30),
        ];
        AudioProcessing.Process(input with { Samples = input.Samples[..44100] }, processors);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var output = AudioProcessing.Process(input, processors);
        stopwatch.Stop();
        stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(2);
        (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore).Should().BeLessThan(64 * 1024 * 1024);
        output.Frames.Should().Be(20 * 44100);
        output.Channels.Should().Be(2);
        Loudness.IntegratedLufs(output).Should().BeApproximately(-30, 0.1);
    }

    private static PcmAudio Impulse(int channels)
    {
        var samples = new float[3 * 44100 * channels];
        samples[0] = 0.4f;
        return new(44100, channels, samples);
    }
}
