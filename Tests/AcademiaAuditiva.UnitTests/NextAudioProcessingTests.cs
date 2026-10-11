using System.Diagnostics;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

public class NextAudioProcessingTests
{
    [Theory]
    [InlineData(false, 80)]
    [InlineData(false, 1000)]
    [InlineData(false, 12000)]
    [InlineData(true, 80)]
    [InlineData(true, 1000)]
    [InlineData(true, 12000)]
    public void CutFilters_AreMinusThreeDbAtCutoff(bool highPass, double cutoff)
    {
        var input = ParametricEqTests.Tone(cutoff);
        var output = AudioProcessing.Process(input, [new CutFilterProcessor(cutoff, highPass)]);
        ParametricEqTests.MeasuredGain(input, output).Should().BeApproximately(-3.0103, 0.02);
    }

    [Theory]
    [InlineData(false, 100, -0.001, 0.02)]
    [InlineData(false, 10000, -43, 2)]
    [InlineData(true, 100, -40, 1)]
    [InlineData(true, 10000, 0, 0.02)]
    public void CutFilters_HaveTheExpectedPassbandAndTwelveDbPerOctaveStopband(
        bool highPass, double frequency, double gain, double tolerance)
    {
        var input = ParametricEqTests.Tone(frequency);
        var output = AudioProcessing.Process(input, [new CutFilterProcessor(1000, highPass)]);
        ParametricEqTests.MeasuredGain(input, output).Should().BeApproximately(gain, tolerance);
    }

    [Theory]
    [InlineData(false, -12)]
    [InlineData(false, 6)]
    [InlineData(false, 12)]
    [InlineData(true, -12)]
    [InlineData(true, 6)]
    [InlineData(true, 12)]
    public void Shelves_HaveHalfTheRequestedGainAtTheirTransition(bool highShelf, double gain)
    {
        var input = ParametricEqTests.Tone(1000);
        var output = AudioProcessing.Process(input, [new ShelfEqProcessor(1000, gain, highShelf)]);
        ParametricEqTests.MeasuredGain(input, output).Should().BeApproximately(gain / 2, 0.02);
    }

    [Theory]
    [InlineData(false, 50, 9)]
    [InlineData(false, 16000, 0)]
    [InlineData(true, 50, 0)]
    [InlineData(true, 16000, 9)]
    public void Shelves_ReachTheirLowAndHighFrequencyPlateaus(bool highShelf, double frequency, double gain)
    {
        var input = ParametricEqTests.Tone(frequency);
        var output = AudioProcessing.Process(input, [new ShelfEqProcessor(1000, 9, highShelf)]);
        ParametricEqTests.MeasuredGain(input, output).Should().BeApproximately(gain, 0.02);
    }

    [Fact]
    public void Shelves_ZeroIsAWireAndOppositeGainsCancel()
    {
        var input = ParametricEqTests.Tone(700);
        AudioProcessing.Process(input, [new ShelfEqProcessor(1000, 0, false)]).Samples.Should().Equal(input.Samples);
        var output = AudioProcessing.Process(input,
            [new ShelfEqProcessor(1000, 9, true), new ShelfEqProcessor(1000, -9, true)]);
        output.Samples.Zip(input.Samples, (left, right) => Math.Abs(left - right)).Max().Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void Filters_PreserveStereoLayoutAndIndependentState()
    {
        var input = new PcmAudio(44100, 2, [0.3f, 0, 0, 0, 0, 0, 0, 0]);
        var output = AudioProcessing.Process(input,
            [new CutFilterProcessor(1000, true), new ShelfEqProcessor(4000, -9, true)]);
        output.Samples.Where((_, index) => index % 2 == 1).Should().OnlyContain(sample => sample == 0);
        output.Frames.Should().Be(input.Frames);
        input.Samples.Should().Equal(0.3f, 0, 0, 0, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(21)]
    [InlineData(30)]
    [InlineData(60)]
    public void WhiteNoise_MeetsItsRmsSnr_IsDeterministicAndDoesNotChangeTheSource(double snr)
    {
        var input = ParametricEqTests.Tone(440);
        var original = input.Samples.ToArray();
        var output = AudioProcessing.Process(input, [new WhiteNoiseProcessor(snr, 17)]);
        var signalPower = input.Samples.Average(sample => (double)sample * sample);
        var noisePower = output.Samples.Zip(original, (sample, reference) => Math.Pow(sample - reference, 2)).Average();
        (10 * Math.Log10(signalPower / noisePower)).Should().BeApproximately(snr, 0.05);
        AudioProcessing.Process(input, [new WhiteNoiseProcessor(snr, 17)]).Samples.Should().Equal(output.Samples);
        AudioProcessing.Process(input, [new WhiteNoiseProcessor(snr, 18)]).Samples.Should().NotEqual(output.Samples);
        input.Samples.Should().Equal(original);
    }

    [Fact]
    public void WhiteNoise_UsesDecorrelatedStereoSamplesAndRefusesSilence()
    {
        var input = new PcmAudio(44100, 2, Enumerable.Repeat(0.1f, 88200).ToArray());
        var output = AudioProcessing.Process(input, [new WhiteNoiseProcessor(12, 42)]);
        var left = output.Samples.Where((_, index) => index % 2 == 0).Select(sample => sample - 0.1).ToArray();
        var right = output.Samples.Where((_, index) => index % 2 == 1).Select(sample => sample - 0.1).ToArray();
        var correlation = left.Zip(right, (a, b) => a * b).Sum()
            / Math.Sqrt(left.Sum(a => a * a) * right.Sum(a => a * a));
        Math.Abs(correlation).Should().BeLessThan(0.02);
        FluentActions.Invoking(() => AudioProcessing.Process(new(44100, 1, new float[44100]),
            [new WhiteNoiseProcessor(12, 42)])).Should().Throw<InvalidOperationException>();
    }

    public static TheoryData<AudioProcessor> InvalidParameters => new()
    {
        new CutFilterProcessor(19, false), new CutFilterProcessor(19846, true),
        new CutFilterProcessor(double.NaN, true), new CutFilterProcessor(double.PositiveInfinity, false),
        new ShelfEqProcessor(0, 3, false), new ShelfEqProcessor(20000, 3, true),
        new ShelfEqProcessor(1000, 12.1, false), new ShelfEqProcessor(1000, -12.1, true),
        new ShelfEqProcessor(double.NaN, 3, true), new ShelfEqProcessor(1000, double.NaN, false),
        new WhiteNoiseProcessor(-0.1, 1), new WhiteNoiseProcessor(60.1, 1),
        new WhiteNoiseProcessor(double.NaN, 1), new WhiteNoiseProcessor(12, 0),
        new WhiteNoiseProcessor(12, -1),
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void InvalidParameters_AreRejectedBeforeProcessing(AudioProcessor processor) =>
        FluentActions.Invoking(() => AudioProcessing.Process(ParametricEqTests.Tone(1000), [processor]))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void CacheKeys_IncludeEveryNewParameterAndOrder()
    {
        AudioProcessor[] processors =
        [
            new CutFilterProcessor(100, false), new CutFilterProcessor(101, false), new CutFilterProcessor(100, true),
            new ShelfEqProcessor(1000, 3, false), new ShelfEqProcessor(1001, 3, false),
            new ShelfEqProcessor(1000, 4, false), new ShelfEqProcessor(1000, 3, true),
            new WhiteNoiseProcessor(12, 1), new WhiteNoiseProcessor(13, 1), new WhiteNoiseProcessor(12, 2),
        ];
        processors.Select(processor => AudioProcessing.Describe(new("source", [processor]), "hash"))
            .Should().OnlyHaveUniqueItems();
        AudioProcessing.Describe(new("source", [processors[0], processors[7]]), "hash")
            .Should().NotBe(AudioProcessing.Describe(new("source", [processors[7], processors[0]]), "hash"));
    }

    [Fact]
    [Trait("Category", "AudioDspBudget")]
    public void FilterShelfNoiseChain_StaysWithinTimeAndMemoryBudgets()
    {
        var input = new PcmAudio(44100, 2,
            [.. Enumerable.Range(0, 20 * 44100).SelectMany(frame =>
                new[] { (float)(0.04 * Math.Sin(2 * Math.PI * 1000 * frame / 44100)), 0.02f })]);
        AudioProcessor[] processors =
        [
            new CutFilterProcessor(150, true), new CutFilterProcessor(8000, false),
            new ShelfEqProcessor(150, 6, false), new ShelfEqProcessor(4000, -6, true),
            new WhiteNoiseProcessor(21, 17), new FadeOutProcessor(15), new LoudnessMatchProcessor(-30),
        ];
        AudioProcessing.Process(input with { Samples = input.Samples[..88200] }, processors);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var output = AudioProcessing.Process(input, processors);
        watch.Stop();
        watch.Elapsed.TotalSeconds.Should().BeLessThan(2);
        (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore).Should().BeLessThan(64 * 1024 * 1024);
        output.Frames.Should().Be(20 * 44100);
        Loudness.IntegratedLufs(output).Should().BeApproximately(-30, 0.1);
    }
}
