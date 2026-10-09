using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

public class LoudnessMatchingTests
{
    private const int SampleRate = AudioSourceRules.SampleRate;
    private static readonly AudioSourceLibrary Library = new(TempAudioSources.BundledRoot);

    public static TheoryData<string, double, double> CatalogCases
    {
        get
        {
            var cases = new TheoryData<string, double, double>();
            foreach (var source in Library.Sources.Where(s => s.Uses.Contains("eq")))
            foreach (var frequency in new[] { 100.0, 500, 1000, 5000, 10000 })
            foreach (var gain in new[] { -12.0, 12 })
            {
                cases.Add(source.Key, frequency, gain);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task CatalogReferenceAndEq_MeetTheSameLoudnessTarget(string key, double frequency, double gain)
    {
        var input = await Library.ReadAsync(Library.Find(key)!);
        var target = AudioProcessing.DefaultLoudnessLufs;
        var reference = AudioProcessing.Process(input, [new LoudnessMatchProcessor(target)]);
        var processed = AudioProcessing.Process(input, [new PeakingEqProcessor(frequency, gain, 1), new LoudnessMatchProcessor(target)]);

        var referenceLufs = Loudness.IntegratedLufs(reference);
        var processedLufs = Loudness.IntegratedLufs(processed);
        referenceLufs.Should().BeApproximately(target, AudioProcessing.LoudnessToleranceLu);
        processedLufs.Should().BeApproximately(target, AudioProcessing.LoudnessToleranceLu);
        Math.Abs(referenceLufs - processedLufs).Should().BeLessThanOrEqualTo(2 * AudioProcessing.LoudnessToleranceLu);
        Loudness.IntegratedLufs(WavFile.Read(WavFile.Write(processed)))
            .Should().BeApproximately(target, AudioProcessing.LoudnessToleranceLu);
        processed.Samples.Should().OnlyContain(s => float.IsFinite(s));
        processed.Frames.Should().Be(input.Frames);
        processed.Channels.Should().Be(input.Channels);
    }

    [Theory]
    [InlineData(-36)]
    [InlineData(-23)]
    [InlineData(-14)]
    public void Targets_AreExplicit_AndUseTheCatalogLoudnessRange(double target)
    {
        var input = Tone(997, 0.1);
        AudioProcessing.Validate(new("pink-noise", [new LoudnessMatchProcessor(target)]), Library.Find("pink-noise")!)
            .Should().Be(1);

        var output = AudioProcessing.Process(input, [new LoudnessMatchProcessor(target)]);

        Loudness.IntegratedLufs(output).Should().BeApproximately(target, AudioProcessing.LoudnessToleranceLu);
        Loudness.IntegratedLufs(WavFile.Read(WavFile.Write(output)))
            .Should().BeApproximately(target, AudioProcessing.LoudnessToleranceLu);
    }

    [Fact]
    public void MatchingUsesKWeightedLoudness_NotPeakOrRmsNormalization()
    {
        var low = AudioProcessing.Process(Tone(30, 0.1), [new LoudnessMatchProcessor(-23)]);
        var high = AudioProcessing.Process(Tone(10000, 0.1), [new LoudnessMatchProcessor(-23)]);

        Loudness.IntegratedLufs(low).Should().BeApproximately(-23, AudioProcessing.LoudnessToleranceLu);
        Loudness.IntegratedLufs(high).Should().BeApproximately(-23, AudioProcessing.LoudnessToleranceLu);
        (Loudness.SamplePeakDbfs(low) - Loudness.SamplePeakDbfs(high)).Should().BeGreaterThan(6);
    }

    [Fact]
    public void MatchingDoesNotEraseTheSpectralDifference_AndLeavesTheInputAlone()
    {
        var input = new PcmAudio(SampleRate, 1,
            [.. Enumerable.Range(0, 3 * SampleRate).Select(f => (float)(0.025
                * (Math.Sin(2 * Math.PI * 100 * f / SampleRate)
                    + Math.Sin(2 * Math.PI * 1000 * f / SampleRate)
                    + Math.Sin(2 * Math.PI * 5000 * f / SampleRate))))]);
        var original = input.Samples.ToArray();
        var reference = AudioProcessing.Process(input, [new LoudnessMatchProcessor(-23)]);
        AudioProcessor[] chain = [new PeakingEqProcessor(1000, 6, 1), new LoudnessMatchProcessor(-23)];
        var processed = AudioProcessing.Process(input, chain);

        (BandRatioDb(processed) - BandRatioDb(reference)).Should().BeInRange(5, 6.1);
        Loudness.IntegratedLufs(processed).Should().BeApproximately(-23, AudioProcessing.LoudnessToleranceLu);
        AudioProcessing.Process(input, chain).Samples.Should().Equal(processed.Samples);
        input.Samples.Should().Equal(original);
    }

    [Fact]
    public void WithoutTheExplicitStage_EqKeepsItsLevelChange()
    {
        var input = Tone(1000, 0.1);
        var processed = AudioProcessing.Process(input, [new PeakingEqProcessor(1000, 6, 1)]);

        (Loudness.IntegratedLufs(processed) - Loudness.IntegratedLufs(input)).Should().BeApproximately(6, 0.02);
    }

    [Fact]
    public void QuietPassagesAndSilence_DoNotDiluteTheMatchingTarget()
    {
        var input = new PcmAudio(SampleRate, 1,
            [.. Tone(997, 0.04).Samples, .. Tone(997, 0.0001).Samples, .. new float[3 * SampleRate]]);

        var output = AudioProcessing.Process(input, [new LoudnessMatchProcessor(-23)]);

        Loudness.IntegratedLufs(output).Should().BeApproximately(-23, AudioProcessing.LoudnessToleranceLu);
    }

    public static TheoryData<AudioProcessor[]> InvalidPlans => new()
    {
        { [new LoudnessMatchProcessor(double.NaN)] },
        { [new LoudnessMatchProcessor(double.PositiveInfinity)] },
        { [new LoudnessMatchProcessor(double.NegativeInfinity)] },
        { [new LoudnessMatchProcessor(-36.01)] },
        { [new LoudnessMatchProcessor(-13.99)] },
        { [new LoudnessMatchProcessor(-23), new GainProcessor(0)] },
        { [new LoudnessMatchProcessor(-23), new PeakingEqProcessor(1000, 6, 1)] },
        { [new LoudnessMatchProcessor(-23), new PanProcessor(0)] },
        { [new LoudnessMatchProcessor(-23), new LoudnessMatchProcessor(-23)] },
    };

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void InvalidTargetsAndStageOrder_AreRefused(AudioProcessor[] processors)
    {
        FluentActions.Invoking(() => AudioProcessing.Validate(new("pink-noise", processors), Library.Find("pink-noise")!))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AudioProcessing.Process(Tone(997, 0.1), processors))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.000001, 1)]
    [InlineData(0.1, 0.3)]
    public void SilenceAndUnmeasurableAudio_AreNotSuccessShapedFallbacks(double amplitude, double seconds)
    {
        var input = Tone(997, amplitude, seconds);

        FluentActions.Invoking(() => AudioProcessing.Process(input, [new LoudnessMatchProcessor(-23)]))
            .Should().Throw<InvalidOperationException>().WithMessage("*measurable audio*");
    }

    [Theory]
    [InlineData(0.002, -23)]
    [InlineData(0.9, -36)]
    public void UnsafeMatchingGain_IsRefused(double amplitude, double target)
    {
        var input = Tone(997, amplitude);

        FluentActions.Invoking(() => AudioProcessing.Process(input, [new LoudnessMatchProcessor(target)]))
            .Should().Throw<InvalidOperationException>().WithMessage("*gain within*");
    }

    private static PcmAudio Tone(double frequency, double amplitude, double seconds = 3) => new(SampleRate, 1,
        [.. Enumerable.Range(0, (int)(seconds * SampleRate))
            .Select(f => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * f / SampleRate)))]);

    private static double BandRatioDb(PcmAudio audio)
    {
        double Amplitude(double frequency)
        {
            var real = 0.0;
            var imaginary = 0.0;
            for (var f = SampleRate; f < 2 * SampleRate; f++)
            {
                var angle = 2 * Math.PI * frequency * f / SampleRate;
                real += audio.Samples[f] * Math.Cos(angle);
                imaginary += audio.Samples[f] * Math.Sin(angle);
            }
            return Math.Sqrt(real * real + imaginary * imaginary);
        }
        return 20 * Math.Log10(Amplitude(1000) / Amplitude(5000));
    }
}
