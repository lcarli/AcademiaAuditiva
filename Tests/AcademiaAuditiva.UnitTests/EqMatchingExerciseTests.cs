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
        target["scoringVersion"]!.Value<int>().Should().Be(EqMatchingValidator.CurrentScoringVersion);
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

    [Theory]
    [InlineData("1000|0|1", 0.9, "direction", "gain")]
    [InlineData("1000|-1|1", 5.0 / 6, "direction", "gain")]
    [InlineData("1000|0.01|1", 0.9006666666666666, "gain", null)]
    public void Pilot_RejectsMissingOrOppositeEq_WithoutRemovingPartialCredit(
        string guess, double score, string firstFailure, string? secondFailure)
    {
        var validator = new EqMatchingValidator();
        const string legacy = """{"frequencyHz":1000,"gainDb":3,"q":1}""";
        const string pilot = """{"frequencyHz":1000,"gainDb":3,"q":1,"scoringVersion":2}""";

        var oldResult = validator.Validate(guess, legacy);
        var result = validator.Validate(guess, pilot);
        oldResult.IsCorrect.Should().BeTrue();
        result.IsCorrect.Should().BeFalse();
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().BeApproximately(score, 1e-12);
        detail.Score.Should().Be(((EqMatchingValidationDetail)oldResult.Detail!).Score);
        detail.ScoringVersion.Should().Be(2);
        detail.FailedCriteria.Should().Equal(secondFailure is null ? [firstFailure] : new[] { firstFailure, secondFailure });
    }

    [Theory]
    [InlineData(3, 1.5, true)]
    [InlineData(3, 4.5, true)]
    [InlineData(3, 1.499999, false)]
    [InlineData(3, 4.500001, false)]
    [InlineData(6, 3, true)]
    [InlineData(6, 9, true)]
    [InlineData(6, 2.999999, false)]
    [InlineData(6, 9.000001, false)]
    [InlineData(12, 9, true)]
    [InlineData(12, 8.999999, false)]
    [InlineData(-3, -1.5, true)]
    [InlineData(-3, -4.5, true)]
    [InlineData(-3, -1.499999, false)]
    [InlineData(-3, -4.500001, false)]
    [InlineData(-12, -9, true)]
    [InlineData(-12, -8.999999, false)]
    public void Pilot_GainLimitIsInclusiveAndUsesTheSmallerOfThreeDbOrHalfTarget(
        double targetGain, double guessGain, bool accepted)
    {
        var expected = FormattableString.Invariant(
            $"{{\"frequencyHz\":1000,\"gainDb\":{targetGain:R},\"q\":1,\"scoringVersion\":2}}");
        var result = new EqMatchingValidator().Validate(FormattableString.Invariant($"1000|{guessGain:R}|1"), expected);
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().BeGreaterThan(0.8);
        result.IsCorrect.Should().Be(accepted);
        detail.FailedCriteria.Should().Equal(accepted ? Array.Empty<string>() : ["gain"]);
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(1, 0, true)]
    [InlineData(-1, 0.000001, false)]
    [InlineData(1, 0.000001, false)]
    public void Pilot_FrequencyLimitIsInclusiveAtFourHundredCents(int direction, double extraOctaves, bool accepted)
    {
        const string expected = """{"frequencyHz":1000,"gainDb":6,"q":1,"scoringVersion":2}""";
        var frequency = 1000 * Math.Pow(2, direction * (400.0 / 1200 + extraOctaves));
        var result = new EqMatchingValidator().Validate(FormattableString.Invariant($"{frequency:R}|6|1"), expected);
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().BeGreaterThan(0.8);
        result.IsCorrect.Should().Be(accepted);
        detail.FailedCriteria.Should().Equal(accepted ? Array.Empty<string>() : ["frequency"]);
    }

    [Theory]
    [InlineData(1.5, true)]
    [InlineData(1.500001, false)]
    [InlineData(0.6666666666666666, true)]
    [InlineData(0.666666, false)]
    public void Pilot_QLimitIsAnInclusiveRatio_NotASymmetricLinearPercentage(double q, bool accepted)
    {
        const string expected = """{"frequencyHz":1000,"gainDb":6,"q":1,"scoringVersion":2}""";
        var result = new EqMatchingValidator().Validate(FormattableString.Invariant($"1000|6|{q:R}"), expected);
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().BeGreaterThan(0.8);
        result.IsCorrect.Should().Be(accepted);
        detail.FailedCriteria.Should().Equal(accepted ? Array.Empty<string>() : ["q"]);
    }

    [Fact]
    public void Pilot_StillRequiresTheWeightedThreshold_WhenEveryIndividualLimitPasses()
    {
        const string expected = """{"frequencyHz":1000,"gainDb":6,"q":1,"scoringVersion":2}""";
        var frequency = 1000 * Math.Pow(2, 1.0 / 3);
        var result = new EqMatchingValidator().Validate(FormattableString.Invariant($"{frequency:R}|3|1.5"), expected);
        result.IsCorrect.Should().BeFalse();
        ((EqMatchingValidationDetail)result.Detail!).FailedCriteria.Should().Equal("score");
    }

    [Theory]
    [InlineData(400, 0.8, false)]
    [InlineData(396, 0.802, true)]
    public void Pilot_GlobalThresholdIsStrictEvenWhenEveryParameterPasses(
        double frequencyCents, double expectedScore, bool accepted)
    {
        var frequency = 1000 * Math.Pow(2, frequencyCents / 1200);
        var result = new EqMatchingValidator().Validate(FormattableString.Invariant($"{frequency:R}|3|1"),
            """{"frequencyHz":1000,"gainDb":6,"q":1,"scoringVersion":2}""");
        result.IsCorrect.Should().Be(accepted);
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().BeApproximately(expectedScore, 1e-12);
        detail.FailedCriteria.Should().Equal(accepted ? Array.Empty<string>() : ["score"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExactlyEightyPercentIsNotCorrect(int? version)
    {
        var expected = JObject.Parse("""{"frequencyHz":1000,"gainDb":6,"q":1}""");
        if (version is not null) expected["scoringVersion"] = version;
        var result = new EqMatchingValidator().Validate("1000|6|2", expected.ToString());
        result.IsCorrect.Should().BeFalse();
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.Score.Should().Be(0.8);
        detail.FailedCriteria.Should().Contain("score");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("\"2\"")]
    [InlineData("2.5")]
    [InlineData("null")]
    public void ExplicitInvalidPolicyVersion_IsRejectedRatherThanTreatedAsLegacy(string version)
    {
        var expected = $"{{\"frequencyHz\":1000,\"gainDb\":6,\"q\":1,\"scoringVersion\":{version}}}";
        var action = () => new EqMatchingValidator().Validate("1000|6|1", expected);
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("1000|NaN|1")]
    [InlineData("1000|Infinity|1")]
    [InlineData("0|3|1")]
    [InlineData("20001|3|1")]
    [InlineData("1000|13|1")]
    [InlineData("1000|3|0")]
    [InlineData("1000|3|9")]
    [InlineData("1000,5|3|1")]
    [InlineData("")]
    public void Pilot_InvalidGuessHasExplicitFeedbackWithoutNumericClaims(string guess)
    {
        var result = new EqMatchingValidator().Validate(guess,
            """{"frequencyHz":1000,"gainDb":3,"q":1,"scoringVersion":2}""");
        result.IsCorrect.Should().BeFalse();
        result.CanonicalAnswer.Should().Be("1000|3|1");
        var detail = (EqMatchingValidationDetail)result.Detail!;
        detail.FailedCriteria.Should().Equal("invalidGuess");
        detail.GuessFrequencyHz.Should().BeNull();
        detail.Score.Should().Be(0);
    }
}
