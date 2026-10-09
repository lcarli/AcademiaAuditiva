using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

public class ParametricEqTests
{
    private const int SampleRate = AudioSourceRules.SampleRate;
    private static readonly AudioSourceLibrary Library = new(TempAudioSources.BundledRoot);

    public static TheoryData<double, double, double> CentreCases
    {
        get
        {
            var cases = new TheoryData<double, double, double>();
            foreach (var frequency in new[] { 20.0, 100, 1000, 10000, 19845 })
            foreach (var gain in new[] { -12.0, 6, 12 })
            foreach (var q in new[] { 0.25, 1, 8 })
            {
                cases.Add(frequency, gain, q);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CentreCases))]
    public void CentreFrequency_HasTheRequestedGain(double frequency, double gain, double q)
    {
        var input = Tone(frequency);
        var output = AudioProcessing.Process(input, [new PeakingEqProcessor(frequency, gain, q)]);

        MeasuredGain(input, output).Should().BeApproximately(gain, 0.03);
        output.Samples.Should().OnlyContain(s => float.IsFinite(s));
    }

    [Theory]
    [InlineData(250, 6, 0.25)]
    [InlineData(500, 6, 1)]
    [InlineData(1000, 6, 1)]
    [InlineData(2000, 6, 1)]
    [InlineData(5000, 6, 1)]
    [InlineData(10000, 6, 8)]
    [InlineData(500, -12, 0.25)]
    [InlineData(2000, -12, 8)]
    public void OffCentreCurve_AgreesWithTheBilinearPeakingResponse(double frequency, double gain, double q)
    {
        var input = Tone(frequency);
        var output = AudioProcessing.Process(input, [new PeakingEqProcessor(1000, gain, q)]);

        MeasuredGain(input, output).Should().BeApproximately(ResponseDb(1000, gain, q, frequency), 0.02);
    }

    [Fact]
    public void HigherQ_NarrowsTheBand_WithoutChangingItsCentreGain()
    {
        var input = Tone(500);
        var broad = AudioProcessing.Process(input, [new PeakingEqProcessor(1000, 6, 0.25)]);
        var narrow = AudioProcessing.Process(input, [new PeakingEqProcessor(1000, 6, 8)]);

        MeasuredGain(input, broad).Should().BeGreaterThan(4);
        MeasuredGain(input, narrow).Should().BeLessThan(0.1);
    }

    [Fact]
    public void Impulse_AgreesWithTheIndependentCookbookReference()
    {
        var input = new PcmAudio(SampleRate, 1, [0.25f, 0, 0, 0, 0, 0, 0, 0]);
        float[] expected =
        [
            0.261907506550f, 0.022445545685f, 0.019634353455f, 0.016713380851f,
            0.013749496190f, 0.010803995587f, 0.007931953563f, 0.005181755865f,
        ];

        var output = AudioProcessing.Process(input, [new PeakingEqProcessor(1000, 6, 1)]);

        output.Samples.Zip(expected, (actual, reference) => Math.Abs(actual - reference))
            .Max().Should().BeLessThan(1e-7f);
    }

    [Fact]
    public void EachStereoChannel_HasItsOwnState()
    {
        var left = Tone(1000);
        var right = Tone(200);
        var stereo = new PcmAudio(SampleRate, 2,
            [.. left.Samples.Zip(right.Samples).SelectMany(pair => new[] { pair.First, pair.Second })]);
        AudioProcessor[] chain = [new PeakingEqProcessor(1000, 6, 8)];

        var output = AudioProcessing.Process(stereo, chain);

        output.Samples.Where((_, i) => i % 2 == 0).Should().Equal(AudioProcessing.Process(left, chain).Samples);
        output.Samples.Where((_, i) => i % 2 == 1).Should().Equal(AudioProcessing.Process(right, chain).Samples);
        output.Channels.Should().Be(2);
        output.Frames.Should().Be(stereo.Frames);
    }

    [Fact]
    public void ZeroGain_IsAnExactWire()
    {
        var input = Tone(997);

        AudioProcessing.Process(input, [new PeakingEqProcessor(1000, 0, 8)]).Samples.Should().Equal(input.Samples);
    }

    [Fact]
    public void EqualBoostAndCut_Cancel_AndProcessingIsDeterministic()
    {
        var input = Tone(500);
        var original = input.Samples.ToArray();
        AudioProcessor[] chain = [new PeakingEqProcessor(1000, 12, 0.25), new PeakingEqProcessor(1000, -12, 0.25)];

        var first = AudioProcessing.Process(input, chain);
        var second = AudioProcessing.Process(input, chain);

        first.Samples.Zip(original, (actual, reference) => Math.Abs(actual - reference)).Max().Should().BeLessThan(1e-6f);
        second.Samples.Should().Equal(first.Samples);
        input.Samples.Should().Equal(original);
    }

    [Theory]
    [InlineData("pink-noise", 20, -12, 0.25, 1)]
    [InlineData("pink-noise", 19845, 12, 8, 1)]
    [InlineData("full-mix", 20, 12, 8, 2)]
    [InlineData("full-mix", 19845, -12, 0.25, 2)]
    public void SafeParameterBoundaries_AreAccepted(string key, double frequency, double gain, double q, int channels)
    {
        AudioProcessing.Validate(new(key, [new PeakingEqProcessor(frequency, gain, q)]), Library.Find(key)!)
            .Should().Be(channels);
    }

    public static TheoryData<PeakingEqProcessor> InvalidParameters => new()
    {
        new(double.NaN, 6, 1),
        new(double.PositiveInfinity, 6, 1),
        new(double.NegativeInfinity, 6, 1),
        new(19.99, 6, 1),
        new(20001, 6, 1),
        new(19846, 6, 1),
        new(SampleRate / 2.0, 6, 1),
        new(1000, double.NaN, 1),
        new(1000, double.PositiveInfinity, 1),
        new(1000, double.NegativeInfinity, 1),
        new(1000, -12.01, 1),
        new(1000, 12.01, 1),
        new(1000, 6, double.NaN),
        new(1000, 6, double.PositiveInfinity),
        new(1000, 6, double.NegativeInfinity),
        new(1000, 6, 0.249),
        new(1000, 6, 8.001),
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void InvalidParameters_AreRefused_BeforeProcessing(PeakingEqProcessor eq)
    {
        FluentActions.Invoking(() => AudioProcessing.Validate(new("pink-noise", [eq]), Library.Find("pink-noise")!))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AudioProcessing.Process(Tone(1000), [eq]))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(1, double.PositiveInfinity, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 0, -1)]
    [InlineData(1, -2, 0.25)]
    [InlineData(1, 2, 0.25)]
    public void NonfiniteCoefficients_AndUnstablePoles_AreRefused(double a0, double a1, double a2)
    {
        FluentActions.Invoking(() => new Biquad(1, 0, 0, a0, a1, a2)).Should().Throw<ArgumentException>();
    }

    private static PcmAudio Tone(double frequency) => new(SampleRate, 1,
        [.. Enumerable.Range(0, 4 * SampleRate).Select(f => (float)(0.025 * Math.Sin(2 * Math.PI * frequency * f / SampleRate)))]);

    private static double MeasuredGain(PcmAudio input, PcmAudio output)
    {
        // The slowest allowed filter (20 Hz, +12 dB, Q=8) has settled by the fourth second.
        var before = input.Samples.Skip(3 * SampleRate).Average(s => (double)s * s);
        var after = output.Samples.Skip(3 * SampleRate).Average(s => (double)s * s);
        return 10 * Math.Log10(after / before);
    }

    private static double ResponseDb(double centre, double gain, double q, double frequency)
    {
        var a = Math.Pow(10, gain / 40);
        var w0 = 2 * Math.PI * centre / SampleRate;
        var w = 2 * Math.PI * frequency / SampleRate;
        var delta = Math.Cos(w) - Math.Cos(w0);
        var quadrature = Math.Sin(w0) / (2 * q) * Math.Sin(w);
        return 10 * Math.Log10((delta * delta + quadrature * quadrature * a * a)
            / (delta * delta + quadrature * quadrature / (a * a)));
    }
}
