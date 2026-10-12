using System.Collections;
using System.Globalization;
using System.Resources;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class ExpandedAudioExerciseTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    internal static readonly string[] Levels = ["beginner", "intermediate", "advanced"];

    public static TheoryData<string, string, int, int, int> Cases
    {
        get
        {
            var data = new TheoryData<string, string, int, int, int>();
            foreach (var definition in AudioComparisonCatalog.Expansion)
            foreach (var level in Levels)
            for (var choice = 0; choice < (definition.ReferenceIsA ? definition.Answers.Count : 1); choice++)
            for (var side = 0; side < 2; side++)
            foreach (var target in new[] { 0, 8 })
                data.Add(definition.Name, level, choice, side, target);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GeneratedRounds_AreMatchedSafeAndHaveTheCorrectValidator(
        string name, string level, int choice, int side, int target)
    {
        var plan = Plan(Sources, name, level, 0, choice, side, target);
        var source = Sources.Find(plan.Clips[0].Plan.SourceKey)!;
        var input = await Sources.ReadAsync(source);
        var rendered = RenderAndCheck(plan, input, name, -30 - target / 2.0);
        var expected = JObject.Parse(plan.ExpectedAnswerJson);
        var answer = expected["answer"]!.Value<string>()!;
        var definition = AudioComparisonCatalog.Find(name)!;
        definition.Answers.Should().Contain(answer);
        var validator = new ExpandedAudioExerciseValidator(name);
        var validation = validator.Validate(answer, plan.ExpectedAnswerJson);
        validation.IsCorrect.Should().BeTrue();
        validation.Detail.Should().BeEquivalentTo(new ExpandedAudioValidationDetail(answer, expected["parameters"]!.ToObject<double[]>()!));
        foreach (var wrong in definition.Answers.Where(option => option != answer).Append("invalid").Append(""))
            validator.Validate(wrong, plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
        if (name is "StereoWidth" or "ChannelPolarity" or "ReverbMix")
        {
            var unchanged = name switch
            {
                "StereoWidth" => answer == "same",
                "ChannelPolarity" => answer == "normal",
                _ => answer == "dry",
            };
            if (unchanged) rendered[0].Samples.Should().Equal(rendered[1].Samples);
        }
    }

    [Theory]
    [InlineData("beginner")]
    [InlineData("intermediate")]
    [InlineData("advanced")]
    public async Task MixProblem_CorrectRepairMovesTheAudioTowardTheReference(string level)
    {
        var definition = AudioComparisonCatalog.Find("MixProblem")!;
        foreach (var choice in Enumerable.Range(0, definition.Answers.Count))
        {
            var plan = Plan(Sources, "MixProblem", level, 0, choice);
            var source = Sources.Find(plan.Clips[0].Plan.SourceKey)!;
            var input = await Sources.ReadAsync(source);
            var rendered = RenderAndCheck(plan, input, "MixProblem");
            var answer = JObject.Parse(plan.ExpectedAnswerJson)["answer"]!.Value<string>()!;
            answer.Should().Be(definition.Answers[choice]);

            var problemDistance = NewAudioExerciseTests.RelativeDifference(rendered[0], rendered[1]);
            var repairIndex = Array.IndexOf(definition.Answers.ToArray(), answer) + 2;
            NewAudioExerciseTests.RelativeDifference(rendered[0], rendered[repairIndex])
                .Should().BeLessThan(problemDistance, $"{answer} should repair the introduced mix issue");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public void EveryNewExercise_HasCompleteLocalizedResources(string culture)
    {
        var entries = new ResourceManager(typeof(SharedResources))
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false)!
            .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        AudioComparisonCatalog.Expansion.Should().HaveCount(20);
        foreach (var definition in AudioComparisonCatalog.Expansion)
        {
            entries.Should().ContainKey(definition.Name);
            foreach (var key in new[] { "Subtitle", "Instructions", "Tip1", "Tip2", "Question", "Feedback",
                "Level.Beginner", "Level.Intermediate", "Level.Advanced" }.Concat(definition.Answers.Select(answer => $"Option.{answer}")))
                entries.Should().ContainKey($"Exercise.{definition.Name}.{key}");
            entries.Should().ContainKey($"LearningPath.Unit.{definition.Name}.Title")
                .And.ContainKey($"LearningPath.Unit.{definition.Name}.Description");
        }
        foreach (var pair in entries.Where(entry => AudioComparisonCatalog.Expansion.Any(
            definition => entry.Key.StartsWith($"Exercise.{definition.Name}.", StringComparison.Ordinal))))
        {
            pair.Value.Should().NotBeNullOrWhiteSpace();
            if (culture == "fr-CA") pair.Value.Should().NotContain("'").And.NotMatchRegex(@"[ \S][:!?]");
        }
    }

    internal static TechnicalListeningRoundPlan Plan(
        AudioSourceLibrary sources, string name, string level, int source, int choice, int side = 0, int target = 0) =>
        new TechnicalListeningRoundGenerator(sources, new NewAudioExerciseTests.SequenceRandom(source, target, side, choice))
            .Plan(new Exercise { Name = name }, new Dictionary<string, string>
            {
                [TechnicalListeningRoundGenerator.LevelFilterFor(name)] = level,
            });

    internal static PcmAudio[] RenderAndCheck(TechnicalListeningRoundPlan plan, PcmAudio input, string name, double target = -30)
    {
        var output = plan.Clips.Select(clip => AudioProcessing.Process(input, clip.Plan.Processors)).ToArray();
        plan.Clips.Select(clip => clip.Key).Should().Equal(name == "MixProblem"
            ? ["A", "B", "C", "D", "E", "F", "G"] : ["A", "B"]);
        plan.Clips.Select(clip => clip.Plan.SourceKey).Distinct().Should().ContainSingle();
        foreach (var audio in output)
        {
            audio.Frames.Should().Be(input.Frames);
            audio.Channels.Should().Be(2);
            audio.Samples.All(sample => float.IsFinite(sample) && Math.Abs(sample) <= 0.98).Should().BeTrue(name);
            Loudness.IntegratedLufs(audio).Should().BeApproximately(target, 0.1, name);
        }
        var answer = JObject.Parse(plan.ExpectedAnswerJson)["answer"]!.Value<string>();
        if (!(name == "StereoWidth" && answer == "same" || name == "ChannelPolarity" && answer == "normal"
            || name == "ReverbMix" && answer == "dry"))
            NewAudioExerciseTests.RelativeDifference(output[0], output[1]).Should().BeGreaterThan(0.015, name);
        return output;
    }
}
