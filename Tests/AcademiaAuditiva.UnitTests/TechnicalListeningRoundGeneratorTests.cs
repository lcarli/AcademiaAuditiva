using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class TechnicalListeningRoundGeneratorTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    private static readonly Exercise Exercise = new() { Name = "LevelMatch" };

    public static TheoryData<string, double, bool> Differences => new()
    {
        { "beginner", 6, false },
        { "beginner", 9, false },
        { "beginner", 12, false },
        { "intermediate", 2, true },
        { "intermediate", 3, true },
        { "intermediate", 4, true },
        { "intermediate", 6, true },
        { "advanced", 0.5, true },
        { "advanced", 1, true },
        { "advanced", 1.5, true },
        { "advanced", 2, true },
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void EveryConfiguredDifference_IsGeneratedAndValidated(
        string level,
        double differenceDb,
        bool requiresDifference)
    {
        var index = TechnicalListeningRoundGenerator.DifferencesFor(level).ToList().IndexOf(differenceDb);
        var plan = Generate(level, sourceIndex: 0, differenceIndex: index, louderIndex: 1, offsetIndex: 0);
        var expected = JObject.Parse(plan.ExpectedAnswerJson);
        var gains = plan.Clips.Select(Gain).ToArray();

        expected["louder"]!.Value<string>().Should().Be("B");
        expected["differenceDb"]!.Value<double>().Should().Be(differenceDb);
        expected["requiresDifference"]!.Value<bool>().Should().Be(requiresDifference);
        (gains[1] - gains[0]).Should().BeApproximately(differenceDb, 0.0001);
        plan.Clips.Select(c => c.Plan.SourceKey).Distinct().Should().ContainSingle();

        var validator = new LevelMatchValidator();
        var rightGuess = requiresDifference ? $"B|{differenceDb:0.#}" : "B";
        validator.Validate(rightGuess, plan.ExpectedAnswerJson).IsCorrect.Should().BeTrue();
        validator.Validate(requiresDifference ? $"A|{differenceDb:0.#}" : "A", plan.ExpectedAnswerJson)
            .IsCorrect.Should().BeFalse();
        if (requiresDifference)
        {
            validator.Validate("B|99", plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
            validator.Validate($"B|{differenceDb:0.#}|extra", plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
        }
        else
        {
            validator.Validate($"B|{differenceDb:0.#}", plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("beginner", 1)]
    [InlineData("intermediate", 2)]
    [InlineData("advanced", 3)]
    public void Profile_UsesOnlyLevelSourcesForItsDifficulty(string level, int difficulty)
    {
        var eligible = Sources.Sources
            .Where(s => s.Uses.Contains("level") && s.Difficulties.Contains(difficulty))
            .ToArray();

        for (var sourceIndex = 0; sourceIndex < eligible.Length; sourceIndex++)
        {
            var plan = Generate(level, sourceIndex, differenceIndex: 0, louderIndex: 0, offsetIndex: 0);
            plan.Clips.Should().OnlyContain(c => c.Plan.SourceKey == eligible[sourceIndex].Key);
            plan.AnswerMetadata["lmSourceKind"].Should().Be(eligible[sourceIndex].Kind);
        }
    }

    [Theory]
    [MemberData(nameof(Differences))]
    public void LoudestAllowedOffset_NeverClips(
        string level,
        double differenceDb,
        bool _)
    {
        var difficulty = level switch { "beginner" => 1, "intermediate" => 2, _ => 3 };
        var eligible = Sources.Sources
            .Where(s => s.Uses.Contains("level") && s.Difficulties.Contains(difficulty))
            .ToArray();
        var differenceIndex = TechnicalListeningRoundGenerator.DifferencesFor(level).ToList().IndexOf(differenceDb);

        for (var sourceIndex = 0; sourceIndex < eligible.Length; sourceIndex++)
        {
            var plan = Generate(level, sourceIndex, differenceIndex, louderIndex: 0, offsetIndex: int.MaxValue);
            foreach (var clip in plan.Clips)
            {
                AudioProcessing.Validate(clip.Plan, eligible[sourceIndex]).Should().Be(eligible[sourceIndex].Audio.Channels);
                (eligible[sourceIndex].Audio.PeakDbfs + Gain(clip))
                    .Should().BeLessThanOrEqualTo(AudioProcessing.MaxPeakDbfs + 0.0001);
            }
        }
    }

    [Fact]
    public void DeterministicSamples_BalanceWhichSideIsLouder()
    {
        var generator = new TechnicalListeningRoundGenerator(Sources, new AlternatingSideRandom());

        var sides = Enumerable.Range(0, 100)
            .Select(_ => JObject.Parse(generator.Plan(
                Exercise, new Dictionary<string, string> { ["lmLevel"] = "beginner" }).ExpectedAnswerJson)["louder"]!.Value<string>())
            .ToArray();

        sides.Count(side => side == "A").Should().Be(50);
        sides.Count(side => side == "B").Should().Be(50);
    }

    [Fact]
    public void UnknownLevel_UsesBeginnerWithoutEchoingTheInput()
    {
        var plan = Generate("anything", 0, 0, 0, 0);
        var expected = JObject.Parse(plan.ExpectedAnswerJson);

        expected["level"]!.Value<string>().Should().Be("beginner");
        expected["differenceDb"]!.Value<double>().Should().Be(6);
        expected["requiresDifference"]!.Value<bool>().Should().BeFalse();
    }

    private static TechnicalListeningRoundPlan Generate(
        string level,
        int sourceIndex,
        int differenceIndex,
        int louderIndex,
        int offsetIndex) =>
        new TechnicalListeningRoundGenerator(
            Sources,
            new SequenceRandom(sourceIndex, differenceIndex, louderIndex, offsetIndex))
        .Plan(Exercise, new Dictionary<string, string> { ["lmLevel"] = level });

    private static double Gain(NamedAudioProcessingPlan clip) =>
        ((GainProcessor)clip.Plan.Processors.Single()).Decibels;

    private sealed class SequenceRandom(params int[] values) : IAudioExerciseRandom
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int exclusiveMaximum)
        {
            var value = _values.Dequeue();
            return Math.Clamp(value, 0, exclusiveMaximum - 1);
        }
    }

    private sealed class AlternatingSideRandom : IAudioExerciseRandom
    {
        private int _side;

        public int Next(int exclusiveMaximum) =>
            exclusiveMaximum == 2 ? Interlocked.Increment(ref _side) % 2 : 0;
    }
}
