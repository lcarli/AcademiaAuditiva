using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class EqMatchingExerciseTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);

    [Theory]
    [InlineData("beginner")]
    [InlineData("intermediate")]
    [InlineData("advanced")]
    public void GeneratedTarget_IsServerOnlyAndUsesMatchedReferenceClips(string level)
    {
        var random = new NewAudioExerciseTests.SequenceRandom(0, 0, 0, 0, 0, 0);
        var plan = new TechnicalListeningRoundGenerator(Sources, random).Plan(
            new Exercise { Name = "EqMatching" }, new Dictionary<string, string> { ["eqmLevel"] = level });
        var target = JObject.Parse(plan.ExpectedAnswerJson);

        plan.Clips.Select(clip => clip.Key).Should().Equal("A", "B");
        plan.AnswerMetadata.Should().BeEmpty();
        target["frequencyHz"]!.Value<double>().Should().BeInRange(60, 12000);
        target["gainDb"]!.Value<double>().Should().BeInRange(-12, 12);
        target["q"]!.Value<double>().Should().BeInRange(0.5, 8);
        plan.Clips[0].Plan.Processors.Should().ContainSingle().Which.Should().BeOfType<LoudnessMatchProcessor>();
        plan.Clips[1].Plan.Processors.Should().HaveCount(2);
        plan.Clips[1].Plan.Processors[0].Should().BeOfType<PeakingEqProcessor>();
        plan.Clips[1].Plan.Processors[1].Should().BeOfType<LoudnessMatchProcessor>();
    }

    [Fact]
    public void Validator_UsesWeightedContinuousTolerancesAndRejectsInvalidValues()
    {
        const string expected = """{"frequencyHz":1000,"gainDb":6,"q":1}""";
        var validator = new EqMatchingValidator();

        var exact = validator.Validate("1000|6|1", expected);
        exact.IsCorrect.Should().BeTrue();
        ((EqMatchingValidationDetail)exact.Detail!).Score.Should().Be(1);

        validator.Validate("2000|6|1", expected).IsCorrect.Should().BeFalse();
        validator.Validate("1000|12|1", expected).IsCorrect.Should().BeFalse();
        validator.Validate("1000|6|2", expected).IsCorrect.Should().BeFalse();
        validator.Validate("1000,5|6|1", expected).IsCorrect.Should().BeFalse();
        validator.Validate("1000|13|1", expected).IsCorrect.Should().BeFalse();
    }
}
