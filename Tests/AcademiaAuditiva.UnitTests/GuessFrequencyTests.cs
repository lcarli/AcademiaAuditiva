using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class GuessFrequencyTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    private static readonly string[] Levels = ["beginner", "intermediate", "advanced"];
    private static readonly Exercise Exercise = new() { Name = "GuessFrequency" };

    public static TheoryData<string, int, int> SourceBands
    {
        get
        {
            var cases = new TheoryData<string, int, int>();
            var generator = new TechnicalListeningRoundGenerator(Sources, new SequenceRandom());
            foreach (var level in Levels)
            foreach (var frequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
            {
                var eligible = generator.FrequencySourcesFor(level, frequency);
                for (var i = 0; i < eligible.Count; i++) cases.Add(level, frequency, i);
            }
            return cases;
        }
    }

    [Fact]
    public void Profiles_HaveTheDocumentedFrequencies_AndMultipleSourcesForEveryAnswer()
    {
        TechnicalListeningRoundGenerator.FrequenciesFor("beginner").Should().Equal(100, 500, 1000, 5000, 10000);
        TechnicalListeningRoundGenerator.FrequenciesFor("intermediate").Should().Equal(125, 250, 500, 1000, 2000, 4000, 8000);
        TechnicalListeningRoundGenerator.FrequenciesFor("advanced").Should().Equal(500, 630, 800, 1000, 1250, 1600, 2000);
        var generator = new TechnicalListeningRoundGenerator(Sources, new SequenceRandom());
        foreach (var level in Levels)
        foreach (var frequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
        {
            var sources = generator.FrequencySourcesFor(level, frequency);
            sources.Should().HaveCountGreaterThan(1, $"{level}, {frequency} Hz cannot identify one source by rote");
            sources.Select(s => s.Key).Should().OnlyHaveUniqueItems();
            sources.Should().OnlyContain(s => s.Uses.Contains("eq")
                && s.Difficulties.Contains(level == "beginner" ? 1 : level == "intermediate" ? 2 : 3));
            sources.Select(s => s.Key).Should().NotContain("bass-line");
            var keys = sources.Select(s => s.Key).ToArray();
            foreach (var otherFrequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
                generator.FrequencySourcesFor(level, otherFrequency).Select(s => s.Key).Should().Equal(keys,
                    "all offered frequencies must be possible on every source in the profile");
        }
    }

    [Theory]
    [MemberData(nameof(SourceBands))]
    public async Task EveryAllowedBand_HasUsefulEnergy_AudibleMatchedChange_AndTheExpectedCurve(
        string level, int frequency, int sourceIndex)
    {
        var plan = Generate(level, frequency, sourceIndex, side: 0, target: 0);
        var source = Sources.Find(plan.Clips[0].Plan.SourceKey)!;
        var input = await Sources.ReadAsync(source);
        var processed = AudioProcessing.Process(input, plan.Clips[0].Plan.Processors);
        var reference = AudioProcessing.Process(input, plan.Clips[1].Plan.Processors);
        var eq = plan.Clips[0].Plan.Processors.OfType<PeakingEqProcessor>().Single();
        var eqOnly = AudioProcessing.Process(input, [eq]);

        foreach (var audio in new[] { processed, reference })
        {
            audio.Channels.Should().Be(input.Channels);
            audio.Frames.Should().Be(input.Frames);
            audio.Samples.All(float.IsFinite).Should().BeTrue();
            audio.Samples.Max(s => Math.Abs(s)).Should().BeLessThanOrEqualTo(0.98f);
            Loudness.IntegratedLufs(audio).Should().BeApproximately(-26, AudioProcessing.LoudnessToleranceLu);
            Loudness.IntegratedLufs(WavFile.Read(WavFile.Write(audio)))
                .Should().BeApproximately(-26, AudioProcessing.LoudnessToleranceLu);
        }
        Math.Abs(Loudness.IntegratedLufs(processed) - Loudness.IntegratedLufs(reference))
            .Should().BeLessThanOrEqualTo(2 * AudioProcessing.LoudnessToleranceLu);

        var before = Spectrum(input);
        var after = Spectrum(processed);
        var referenceSpectrum = Spectrum(reference);
        var targetBins = BandBins(frequency, input.SampleRate).ToArray();
        var bandPower = targetBins.Sum(i => before[i]);
        (bandPower / before.Sum()).Should().BeGreaterThanOrEqualTo(0.001,
            $"{source.Key} must have useful energy in the {frequency} Hz third-octave band");
        var bandChange = 10 * Math.Log10(targetBins.Sum(i => after[i]) / targetBins.Sum(i => referenceSpectrum[i]));
        bandChange.Should().BeGreaterThanOrEqualTo(1,
            $"{source.Key}, {frequency} Hz must remain emphasized after loudness matching");

        var commonDb = 10 * Math.Log10(MeanSquare(processed) / MeanSquare(eqOnly)
            / (MeanSquare(reference) / MeanSquare(input)));
        foreach (var band in TechnicalListeningRoundGenerator.FrequenciesFor(level))
        {
            var bins = BandBins(band, input.SampleRate).ToArray();
            var expectedPower = bins.Sum(i => before[i] * Math.Pow(10, ResponseDb(eq, i * input.SampleRate / (double)FftSize) / 10));
            var expectedDb = 10 * Math.Log10(expectedPower / bins.Sum(i => before[i])) + commonDb;
            var measuredDb = 10 * Math.Log10(bins.Sum(i => after[i]) / bins.Sum(i => referenceSpectrum[i]));
            measuredDb.Should().BeApproximately(expectedDb, 0.25,
                $"{source.Key}, EQ {frequency} Hz, measured band {band} Hz follows the filter, including outside the target");
        }
    }

    [Theory]
    [InlineData("beginner", 9, 1)]
    [InlineData("intermediate", 6, 1)]
    [InlineData("advanced", 6, 2)]
    public void WithinEachProfile_OnlyTheFrequencyChanges_AndEitherClipCanBeProcessed(
        string level, double gain, double q)
    {
        foreach (var frequency in TechnicalListeningRoundGenerator.FrequenciesFor(level))
        foreach (var side in new[] { 0, 1 })
        foreach (var target in new[] { 0, 12 })
        {
            var plan = Generate(level, frequency, 0, side, target);
            plan.Clips.Select(c => c.Key).Should().Equal("A", "B");
            plan.Clips.Select(c => c.Plan.SourceKey).Distinct().Should().ContainSingle();
            var boosted = plan.Clips[side];
            boosted.Plan.Processors[0].Should().Be(new PeakingEqProcessor(frequency, gain, q));
            plan.Clips[1 - side].Plan.Processors.Should().ContainSingle();
            plan.Clips.Select(c => c.Plan.Processors.Last()).Should()
                .OnlyContain(p => p.Equals(new LoudnessMatchProcessor(-26 - target / 2.0)));
            var expected = JObject.Parse(plan.ExpectedAnswerJson);
            expected["boostedClip"]!.Value<string>().Should().Be(boosted.Key);
            expected["frequencyHz"]!.Value<int>().Should().Be(frequency);
            plan.AnswerMetadata["gfFrequencyHz"].Should().Be(frequency.ToString(CultureInfo.InvariantCulture));
            plan.AnswerMetadata["gfSourceKind"].Should().Be(Sources.Find(boosted.Plan.SourceKey)!.Kind);
        }
    }

    [Theory]
    [InlineData("1000", true)]
    [InlineData(" 1000 ", true)]
    [InlineData("500", false)]
    [InlineData("1k", false)]
    [InlineData("1000 Hz", false)]
    [InlineData("1000|A", false)]
    [InlineData("", false)]
    public void Validator_RequiresOneFrequency_AndReturnsRegionAndSourceCueAfterTheAnswer(string guess, bool correct)
    {
        var plan = Generate("advanced", 1000, 0, side: 1, target: 0);

        var result = new GuessFrequencyValidator().Validate(guess, plan.ExpectedAnswerJson);

        result.IsCorrect.Should().Be(correct);
        result.CanonicalAnswer.Should().Be("1000");
        result.Detail.Should().Be(new GuessFrequencyValidationDetail(1000, "B", "mids", "keys"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void Resources_CoverEveryRegionSourceAndNewEngagementText(string culture)
    {
        var resources = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false)!
            .Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        string[] keys =
        [
            "GuessFrequency", "Exercise.GuessFrequency.Subtitle", "Exercise.GuessFrequency.Instructions",
            "Exercise.GuessFrequency.Tip1", "Exercise.GuessFrequency.Tip2", "Exercise.GuessFrequency.Tip3",
            "Exercise.GuessFrequency.Question", "Exercise.GuessFrequency.New", "Exercise.GuessFrequency.Incomplete",
            "Exercise.GuessFrequency.Level.Beginner", "Exercise.GuessFrequency.Level.Intermediate", "Exercise.GuessFrequency.Level.Advanced",
            "Exercise.GuessFrequency.Region.bass", "Exercise.GuessFrequency.Region.lowMids", "Exercise.GuessFrequency.Region.mids",
            "Exercise.GuessFrequency.Region.presence", "Exercise.GuessFrequency.Region.air",
            "Exercise.GuessFrequency.Cue.noise",
            "Exercise.GuessFrequency.Cue.keys", "Exercise.GuessFrequency.Cue.mix",
            "LearningPath.Unit.FrequencyFoundations.Title", "LearningPath.Unit.FrequencyFoundations.Description",
            "Dashboard.TrackHint", "DailyChallenge.Audio.Title",
        ];
        foreach (var key in keys)
        {
            resources[key].Should().NotBeNullOrWhiteSpace().And.NotContain("{", key);
            if (culture == "fr-CA")
                resources[key].Should().NotMatchRegex(@"[ \S][:!?]").And.NotContain("'", key);
        }
        resources["Exercise.GuessFrequency.Feedback"].Should().ContainAll("{0}", "{1}", "{2}", "{3}");
        resources["Exercise.GuessFrequency.Frequency"].Should().Be("{0} Hz");
    }

    private static TechnicalListeningRoundPlan Generate(string level, int frequency, int sourceIndex, int side, int target) =>
        new TechnicalListeningRoundGenerator(Sources, new SequenceRandom(
            TechnicalListeningRoundGenerator.FrequenciesFor(level).ToList().IndexOf(frequency), sourceIndex, side, target))
        .Plan(Exercise, new Dictionary<string, string> { ["gfLevel"] = level });

    private const int FftSize = 32768;

    private static IEnumerable<int> BandBins(int frequency, int sampleRate)
    {
        var halfBand = Math.Pow(2, 1 / 6.0);
        var first = (int)Math.Ceiling(frequency / halfBand * FftSize / sampleRate);
        var last = (int)Math.Floor(frequency * halfBand * FftSize / sampleRate);
        return Enumerable.Range(first, last - first + 1);
    }

    // A Hann-windowed spectrum after one second of settling, summed across
    // channels. Independent of the production biquad and BS.1770 filters.
    private static double[] Spectrum(PcmAudio audio)
    {
        var power = new double[FftSize / 2 + 1];
        for (var channel = 0; channel < audio.Channels; channel++)
        {
            var bins = new Complex[FftSize];
            for (var i = 0; i < FftSize; i++)
                bins[i] = audio.Samples[(audio.SampleRate + i) * audio.Channels + channel]
                    * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
            for (int i = 1, j = 0; i < FftSize; i++)
            {
                var bit = FftSize >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) (bins[i], bins[j]) = (bins[j], bins[i]);
            }
            for (var length = 2; length <= FftSize; length <<= 1)
            {
                var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
                for (var start = 0; start < FftSize; start += length)
                {
                    var phase = Complex.One;
                    for (var i = 0; i < length / 2; i++, phase *= step)
                    {
                        var even = bins[start + i];
                        var odd = bins[start + i + length / 2] * phase;
                        bins[start + i] = even + odd;
                        bins[start + i + length / 2] = even - odd;
                    }
                }
            }
            for (var i = 0; i < power.Length; i++)
                power[i] += bins[i].Magnitude * bins[i].Magnitude;
        }
        return power;
    }

    private static double MeanSquare(PcmAudio audio) =>
        audio.Samples.Sum(s => (double)s * s) / audio.Samples.Length;

    private static double ResponseDb(PeakingEqProcessor eq, double frequency)
    {
        var a = Math.Pow(10, eq.GainDb / 40);
        var w0 = 2 * Math.PI * eq.FrequencyHz / AudioSourceRules.SampleRate;
        var w = 2 * Math.PI * frequency / AudioSourceRules.SampleRate;
        var delta = Math.Cos(w) - Math.Cos(w0);
        var quadrature = Math.Sin(w0) / (2 * eq.Q) * Math.Sin(w);
        return 10 * Math.Log10((delta * delta + quadrature * quadrature * a * a)
            / (delta * delta + quadrature * quadrature / (a * a)));
    }

    private sealed class SequenceRandom(params int[] values) : IAudioExerciseRandom
    {
        private readonly Queue<int> _values = new(values);
        public int Next(int exclusiveMaximum) => Math.Clamp(_values.Dequeue(), 0, exclusiveMaximum - 1);
    }
}
