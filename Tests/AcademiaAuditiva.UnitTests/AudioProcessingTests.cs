using System.Diagnostics;
using System.Globalization;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Xunit.Abstractions;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The processors of the audio track, measured on generated signals: a gain changes
/// the level by exactly what it says, a pan moves a mono signal without changing its
/// power, and a plan that could not be rendered safely is refused before any audio
/// is read (docs/Audio-Ear-Training.md, slice 3).
/// </summary>
public class AudioProcessingTests(ITestOutputHelper output)
{
    private const int SampleRate = 44100;

    private static readonly AudioSourceLibrary Library = new(TempAudioSources.BundledRoot);

    // pink-noise peaks at −9.51 dBFS, full-mix is stereo.
    private static AudioSource Mono => Library.Find("pink-noise")!;

    private static AudioSource Stereo => Library.Find("full-mix")!;

    [Theory]
    [InlineData(-24)]
    [InlineData(-6)]
    [InlineData(-0.5)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void Gain_ChangesTheLevel_ByThatManyDecibels(double decibels)
    {
        var tone = Sine(channels: 1, amplitude: 0.25);

        var louder = AudioProcessing.Process(tone, [new GainProcessor(decibels)]);

        louder.Channels.Should().Be(1);
        louder.Samples.Should().HaveCount(tone.Samples.Length);
        (Db(Rms(louder.Samples)) - Db(Rms(tone.Samples))).Should().BeApproximately(decibels, 1e-4);
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0.70710678, 0.70710678)]
    [InlineData(1, 0, 1)]
    [InlineData(0.5, 0.38268343, 0.92387953)]
    [InlineData(-0.5, 0.92387953, 0.38268343)]
    public void Pan_SendsAMonoSignal_ToEachSideByThePanLaw(double position, double left, double right)
    {
        var tone = Sine(channels: 1, amplitude: 0.5);

        var panned = AudioProcessing.Process(tone, [new PanProcessor(position)]);

        panned.Channels.Should().Be(2);
        var (l, r) = Split(panned.Samples);
        Rms(l).Should().BeApproximately(left * Rms(tone.Samples), 1e-6);
        Rms(r).Should().BeApproximately(right * Rms(tone.Samples), 1e-6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.75)]
    [InlineData(-0.2)]
    [InlineData(0)]
    [InlineData(0.33)]
    [InlineData(1)]
    public void Pan_KeepsThePower_WhereverTheSignalIsPlaced(double position)
    {
        var tone = Sine(channels: 1, amplitude: 0.5);

        var (l, r) = Split(AudioProcessing.Process(tone, [new PanProcessor(position)]).Samples);

        (Power(l) + Power(r)).Should().BeApproximately(Power(tone.Samples), 1e-6);
        var gains = AudioProcessing.PanGains(position);
        (gains.Left * gains.Left + gains.Right * gains.Right).Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void Processing_IsDeterministic_AndLeavesItsInputAlone()
    {
        var tone = Sine(channels: 1, amplitude: 0.5);
        var original = tone.Samples.ToArray();
        AudioProcessor[] chain = [new GainProcessor(-3.5), new PanProcessor(0.4), new GainProcessor(1.25)];

        var first = AudioProcessing.Process(tone, chain);
        var second = AudioProcessing.Process(tone, chain);

        second.Samples.Should().Equal(first.Samples);
        tone.Samples.Should().Equal(original);
        first.Samples.Should().OnlyContain(s => float.IsFinite(s));
    }

    [Fact]
    public void ProcessorsRun_InTheirOrder()
    {
        var tone = Sine(channels: 1, amplitude: 0.5);

        // A second pan can't follow the first, which made the signal stereo.
        FluentActions.Invoking(() => AudioProcessing.Process(tone, [new PanProcessor(0), new PanProcessor(0)]))
            .Should().Throw<ArgumentException>();
        AudioProcessing.Process(tone, [new PanProcessor(1), new GainProcessor(-6)]).Channels.Should().Be(2);
    }

    [Theory]
    [InlineData("pink-noise", 1)]
    [InlineData("full-mix", 2)]
    public void Validation_GivesTheChannelsAPlanEndsWith(string key, int channels)
    {
        var source = Library.Find(key)!;

        AudioProcessing.Validate(new(key, []), source).Should().Be(channels);
        AudioProcessing.Validate(new(key, [new GainProcessor(-3)]), source).Should().Be(channels);
    }

    [Fact]
    public void PanningAMonoSource_MakesItStereo()
    {
        AudioProcessing.Validate(new("pink-noise", [new GainProcessor(-3), new PanProcessor(-0.25)]), Mono).Should().Be(2);
    }

    public static TheoryData<string, AudioProcessor?[]> InvalidMonoPlans => new()
    {
        { "a gain that is not a number", [new GainProcessor(double.NaN)] },
        { "an infinite gain", [new GainProcessor(double.PositiveInfinity)] },
        { "an infinitely negative gain", [new GainProcessor(double.NegativeInfinity)] },
        { "a gain below the range", [new GainProcessor(AudioProcessing.MinGainDb - 0.01)] },
        { "a gain above the range", [new GainProcessor(AudioProcessing.MaxGainDb + 0.01)] },
        { "gains adding up below the range", [new GainProcessor(-20), new GainProcessor(-20)] },
        { "a pan that is not a number", [new PanProcessor(double.NaN)] },
        { "an infinite pan", [new PanProcessor(double.PositiveInfinity)] },
        { "a pan past hard left", [new PanProcessor(-1.01)] },
        { "a pan past hard right", [new PanProcessor(1.5)] },
        { "a second pan", [new PanProcessor(-0.5), new PanProcessor(0.5)] },
        { "a missing processor", [null] },
        { "too many processors", [.. Enumerable.Repeat<AudioProcessor>(new GainProcessor(0), AudioProcessing.MaxProcessors + 1)] },
        // −9.51 dBFS + 9.4 dB peaks at −0.11 dBFS, above −0.2 dBFS.
        { "a gain that would clip", [new GainProcessor(9.4)] },
        { "gains that would clip together", [new GainProcessor(5), new PanProcessor(0), new GainProcessor(4.4)] },
    };

    [Theory]
    [MemberData(nameof(InvalidMonoPlans))]
    public void InvalidPlans_AreRefused(string why, AudioProcessor?[] processors)
    {
        FluentActions.Invoking(() => AudioProcessing.Validate(new("pink-noise", processors!), Mono))
            .Should().Throw<ArgumentException>(why);
    }

    [Fact]
    public void AStereoSource_CannotBePanned()
    {
        FluentActions.Invoking(() => AudioProcessing.Validate(new("full-mix", [new PanProcessor(0)]), Stereo))
            .Should().Throw<ArgumentException>().WithMessage("Only a mono signal can be panned.*");
    }

    [Fact]
    public void APlan_IsCheckedAgainstItsOwnSource()
    {
        FluentActions.Invoking(() => AudioProcessing.Validate(new("full-mix", []), Mono)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AudioProcessing.Validate(new("pink-noise", null!), Mono)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheLoudestSafeGain_IsAccepted()
    {
        // −9.51 dBFS + 9.3 dB peaks at −0.21 dBFS.
        AudioProcessing.Validate(new("pink-noise", [new GainProcessor(9.3)]), Mono).Should().Be(1);
        AudioProcessing.Validate(new("pink-noise", [new GainProcessor(AudioProcessing.MinGainDb)]), Mono).Should().Be(1);
        AudioProcessing.Validate(new("pink-noise", [new PanProcessor(-1)]), Mono).Should().Be(2);
        AudioProcessing.Validate(new("pink-noise", [new PanProcessor(1)]), Mono).Should().Be(2);
    }

    [Fact]
    public void Refusals_NameTheLimits_NotThePlansValues()
    {
        var refusal = FluentActions.Invoking(() => AudioProcessing.Validate(new("pink-noise", [new GainProcessor(9.75)]), Mono))
            .Should().Throw<ArgumentOutOfRangeException>().Which;

        refusal.Message.Should().NotContain("9.75").And.NotContain("9,75");
    }

    [Fact]
    public void TheDescription_HoldsTheEngine_TheSourceVersion_AndEveryProcessorInOrder()
    {
        var plan = new AudioProcessingPlan("pink-noise", [new GainProcessor(-6.5), new PanProcessor(0.25)]);

        AudioProcessing.Describe(plan, "abc").Should().Be($"{AudioProcessing.EngineVersion}:pink-noise#abc:gain(-6.5)|pan(0.25)");
        AudioProcessing.Describe(plan with { Processors = [new PanProcessor(0.25), new GainProcessor(-6.5)] }, "abc")
            .Should().NotBe(AudioProcessing.Describe(plan, "abc"), "processor order is part of the plan");
        AudioProcessing.Describe(plan, "abd").Should().NotBe(AudioProcessing.Describe(plan, "abc"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void TheDescription_IsTheSame_InEveryLanguage(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            AudioProcessing.Describe(new("pink-noise", [new GainProcessor(-1.5), new PanProcessor(-0.125)]), "v")
                .Should().Be($"{AudioProcessing.EngineVersion}:pink-noise#v:gain(-1.5)|pan(-0.125)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NegativeZero_IsDescribedAsZero()
    {
        AudioProcessing.Describe(new("pink-noise", [new GainProcessor(-0.0), new PanProcessor(-0.0)]), "v")
            .Should().Be(AudioProcessing.Describe(new("pink-noise", [new GainProcessor(0), new PanProcessor(0)]), "v"));
    }

    [Fact]
    public void CloseValues_AreDescribedApart()
    {
        AudioProcessing.Describe(new("pink-noise", [new GainProcessor(1.0000001)]), "v")
            .Should().NotBe(AudioProcessing.Describe(new("pink-noise", [new GainProcessor(1)]), "v"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EqAndMatchingDescriptions_IncludeEveryParameterInvariantly(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            AudioProcessingPlan plan = new("pink-noise",
                [new GainProcessor(-1.5), new PeakingEqProcessor(1000.5, 6.25, 1.125), new LoudnessMatchProcessor(-23.5)]);

            AudioProcessing.Describe(plan, "v").Should()
                .Be($"{AudioProcessing.EngineVersion}:pink-noise#v:gain(-1.5)|peaking-eq(1000.5,6.25,1.125)|loudness-match(-23.5)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(48000, 1, 1)]
    [InlineData(SampleRate, 0, 1)]
    [InlineData(SampleRate, 3, 3)]
    [InlineData(SampleRate, 2, 3)]
    [InlineData(SampleRate, 1, SampleRate * 20 + 1)]
    public void InvalidOrOversizedPcm_IsRefusedBeforeAllocatingOutput(int rate, int channels, int samples)
    {
        var audio = new PcmAudio(rate, channels, new float[samples]);

        FluentActions.Invoking(() => AudioProcessing.Process(audio, [new PeakingEqProcessor(1000, 6, 1)]))
            .Should().Throw<ArgumentException>().WithMessage("*complete mono or stereo frames*");
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(1.01f)]
    public void NonfiniteOrOverrangePcm_IsRefused(float sample)
    {
        FluentActions.Invoking(() => AudioProcessing.Process(new(SampleRate, 1, [sample]), [new PeakingEqProcessor(1000, 6, 1)]))
            .Should().Throw<ArgumentException>().WithMessage("*finite PCM*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Category", "AudioDspBudget")]
    public void LongestSourceAndLargestChain_StayWithinDspTimeAndAllocationBudgets(int channels)
    {
        var frames = (int)(AudioSourceRules.MaxSeconds * SampleRate);
        var samples = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            samples[f * channels] = (float)(0.01 * Math.Sin(2 * Math.PI * 220 * f / SampleRate)
                + 0.025 * Math.Sin(2 * Math.PI * 1000 * f / SampleRate));
            if (channels == 2)
            {
                samples[f * channels + 1] = (float)(0.03 * Math.Sin(2 * Math.PI * 4000 * f / SampleRate));
            }
        }
        var audio = new PcmAudio(SampleRate, channels, samples);
        var processors = new List<AudioProcessor>();
        if (channels == 1)
        {
            processors.Add(new PanProcessor(0.3));
        }
        double[] frequencies = [40, 100, 250, 1000, 2500, 6000, 10000];
        foreach (var frequency in frequencies.Take(AudioProcessing.MaxProcessors - processors.Count - 1))
        {
            processors.Add(new PeakingEqProcessor(frequency, 6, 1));
        }
        processors.Add(new LoudnessMatchProcessor(AudioProcessing.DefaultLoudnessLufs));
        processors.Should().HaveCount(AudioProcessing.MaxProcessors);
        AudioProcessing.Process(audio with { Samples = samples.Take(SampleRate * channels).ToArray() }, processors);

        var timer = new Stopwatch();
        var startAllocation = GC.GetAllocatedBytesForCurrentThread();
        timer.Start();
        var processed = AudioProcessing.Process(audio, processors);
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - startAllocation;
        output.WriteLine("20 s, {0} input channels, 8 stages: {1:F1} ms DSP, {2:F2} MiB allocated.",
            channels, timer.Elapsed.TotalMilliseconds, allocated / (1024.0 * 1024));

        timer.Elapsed.TotalSeconds.Should().BeLessThan(2, "the cold-DSP budget is 2 seconds per clip");
        allocated.Should().BeLessThan(64 * 1024 * 1024, "the per-clip DSP allocation budget is 64 MiB");
        processed.Frames.Should().Be(frames);
        processed.Channels.Should().Be(2);
        Loudness.IntegratedLufs(processed).Should()
            .BeApproximately(AudioProcessing.DefaultLoudnessLufs, AudioProcessing.LoudnessToleranceLu);
    }

    /// <summary>One second of a 1 kHz sine on every channel.</summary>
    private static PcmAudio Sine(int channels, double amplitude)
    {
        var samples = new float[SampleRate * channels];
        for (var f = 0; f < SampleRate; f++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * f / SampleRate));
            for (var c = 0; c < channels; c++)
            {
                samples[f * channels + c] = value;
            }
        }
        return new PcmAudio(SampleRate, channels, samples);
    }

    private static (float[] Left, float[] Right) Split(float[] stereo) =>
        ([.. stereo.Where((_, i) => i % 2 == 0)], [.. stereo.Where((_, i) => i % 2 == 1)]);

    private static double Power(float[] samples) => samples.Average(s => (double)s * s);

    private static double Rms(float[] samples) => Math.Sqrt(Power(samples));

    private static double Db(double amplitude) => 20 * Math.Log10(amplitude);
}
