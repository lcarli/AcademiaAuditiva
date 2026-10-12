using System.Collections;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using AcademiaAuditiva.Interfaces;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class NewAudioExerciseTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    private static readonly string[] Names = ["BoostOrCut", "GuessQ", "Compression"];
    private static readonly string[] Levels = ["beginner", "intermediate", "advanced"];

    public static TheoryData<string, string, int, int, int> Rounds
    {
        get
        {
            var data = new TheoryData<string, string, int, int, int>();
            foreach (var name in Names)
            foreach (var level in Levels)
            for (var first = 0; first < (name == "Compression" ? 2 : name == "BoostOrCut" ? 7 : 3); first++)
            for (var second = 0; second < (name == "GuessQ" ? level == "advanced" ? 5 : 3 : 2); second++)
            for (var target = 0; target <= 8; target += 8)
                data.Add(name, level, first, second, target);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Rounds))]
    public async Task EveryRound_IsMatchedSafeAudiblyChanged_AndItsAnswerIsAccepted(
        string name, string level, int first, int second, int target)
    {
        var values = name == "Compression" ? new[] { first, second, target } : [first, second, 0, target];
        var plan = new TechnicalListeningRoundGenerator(Sources, new SequenceRandom(values)).Plan(
            new Exercise { Name = name }, new Dictionary<string, string> { [TechnicalListeningRoundGenerator.LevelFilterFor(name)] = level });
        plan.Clips.Select(clip => clip.Key).Should().Equal("A", "B");
        plan.Clips.Select(clip => clip.Plan.SourceKey).Distinct().Should().ContainSingle();
        var source = Sources.Find(plan.Clips[0].Plan.SourceKey)!;
        var input = await Sources.ReadAsync(source);
        var rendered = plan.Clips.Select(clip => AudioProcessing.Process(input, clip.Plan.Processors)).ToArray();
        foreach (var audio in rendered)
        {
            audio.Frames.Should().Be(input.Frames);
            audio.Channels.Should().Be(input.Channels);
            audio.Samples.All(sample => float.IsFinite(sample) && Math.Abs(sample) <= 0.98).Should().BeTrue();
            Loudness.IntegratedLufs(audio).Should().BeApproximately(-28 - target / 2.0, 0.1);
        }
        RelativeDifference(rendered[0], rendered[1]).Should().BeGreaterThan(0.015);
        var expected = JObject.Parse(plan.ExpectedAnswerJson);
        var validator = Validator(name);
        var answer = expected["answer"]!.Value<string>()!;
        validator.Validate(answer, plan.ExpectedAnswerJson).IsCorrect.Should().BeTrue();
        foreach (var bad in new[] { "", "nonsense", answer + "|extra" })
            validator.Validate(bad, plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
        if (name != "Compression")
        {
            plan.Clips[0].Plan.Processors.Should().ContainSingle().Which.Should().BeOfType<LoudnessMatchProcessor>();
            plan.Clips[1].Plan.Processors[0].Should().BeOfType<PeakingEqProcessor>();
            if (name == "BoostOrCut")
            {
                var before = GuessFrequencyTests.Spectrum(rendered[0]);
                var after = GuessFrequencyTests.Spectrum(rendered[1]);
                var eq = (PeakingEqProcessor)plan.Clips[1].Plan.Processors[0];
                var band = GuessFrequencyTests.BandBins((int)eq.FrequencyHz, input.SampleRate).ToArray();
                var change = 10 * Math.Log10(band.Sum(index => after[index]) / band.Sum(index => before[index]));
                (Math.Sign(eq.GainDb) * change).Should().BeGreaterThanOrEqualTo(1,
                    "the selected region must remain boosted or cut after loudness matching");
            }
        }
    }

    [Theory]
    [InlineData("beginner", 0)]
    [InlineData("beginner", 1)]
    [InlineData("beginner", 2)]
    [InlineData("intermediate", 0)]
    [InlineData("intermediate", 1)]
    [InlineData("intermediate", 2)]
    [InlineData("advanced", 0)]
    [InlineData("advanced", 1)]
    [InlineData("advanced", 2)]
    public void QAnswers_HaveProgressivelyNarrowerMeasuredCurves_WithTheSameCentreGain(string level, int frequencyIndex)
    {
        var beginnerAndIntermediateWidths = new[] { "broad", "medium", "narrow" };
        var qValues = level == "advanced" ? new[] { 0.5, 1.0, 2.0, 4.0, 8.0 }
            : level == "beginner" ? [0.35, 1.0, 4.0] : [0.5, 1.5, 4.0];
        var offCentreGains = new List<double>();
        for (var width = 0; width < qValues.Length; width++)
        {
            var plan = new TechnicalListeningRoundGenerator(Sources, new SequenceRandom(frequencyIndex, width, 0, 0))
                .Plan(new Exercise { Name = "GuessQ" }, new Dictionary<string, string> { ["gqLevel"] = level });
            var expectedAnswer = level == "advanced" ? qValues[width].ToString(System.Globalization.CultureInfo.InvariantCulture)
                : beginnerAndIntermediateWidths[width];
            JObject.Parse(plan.ExpectedAnswerJson)["answer"]!.Value<string>().Should().Be(expectedAnswer);
            var eq = (PeakingEqProcessor)plan.Clips[1].Plan.Processors[0];
            eq.Q.Should().Be(qValues[width]);
            var centre = ParametricEqTests.Tone(eq.FrequencyHz);
            ParametricEqTests.MeasuredGain(centre, AudioProcessing.Process(centre, [eq]))
                .Should().BeApproximately(9, 0.03);
            var offCentre = ParametricEqTests.Tone(eq.FrequencyHz / 2);
            offCentreGains.Add(ParametricEqTests.MeasuredGain(offCentre, AudioProcessing.Process(offCentre, [eq])));
        }
        for (var index = 1; index < offCentreGains.Count; index++)
            (offCentreGains[index - 1] - offCentreGains[index]).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void NewTexts_AreCompleteAndFollowFrenchTypography(string culture)
    {
        var entries = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false)!
            .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        foreach (var name in Names)
        {
            entries.Should().ContainKey(name);
            foreach (var key in new[] { "Subtitle", "Instructions", "Tip1", "Tip2", "Tip3", "Question", "New", "Incomplete",
                "Level.Beginner", "Level.Intermediate", "Level.Advanced", "Feedback" })
                entries.Should().ContainKey($"Exercise.{name}.{key}");
        }
        foreach (var pair in entries.Where(entry => Names.Any(name => entry.Key.StartsWith($"Exercise.{name}.", StringComparison.Ordinal))))
        {
            pair.Value.Should().NotBeNullOrWhiteSpace();
            if (culture == "fr-CA") pair.Value.Should().NotContain("'").And.NotMatchRegex(@"[ \S][:!?]");
        }
    }

    internal static double RelativeDifference(PcmAudio a, PcmAudio b) =>
        Math.Sqrt(a.Samples.Zip(b.Samples).Sum(pair => Math.Pow(pair.First - pair.Second, 2))
            / a.Samples.Sum(sample => (double)sample * sample));

    internal static IExerciseValidator Validator(string name) => name switch
    {
        "BoostOrCut" => new BoostOrCutValidator(), "GuessQ" => new GuessQValidator(), "Compression" => new CompressionValidator(),
        _ => throw new ArgumentException("Unknown exercise.", nameof(name)),
    };

    internal sealed class SequenceRandom(params int[] values) : IAudioExerciseRandom
    {
        private readonly Queue<int> _values = new(values);
        public int Next(int exclusiveMaximum) => _values.Count > 0 ? _values.Dequeue() % exclusiveMaximum : 0;
    }
}
