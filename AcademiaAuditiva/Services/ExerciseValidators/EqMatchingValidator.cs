using System.Globalization;
using AcademiaAuditiva.Interfaces;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.ExerciseValidators;

public sealed class EqMatchingValidator : IExerciseValidator
{
    public const int CurrentScoringVersion = 2;

    private const double FrequencyWeight = 0.5;
    private const double GainWeight = 0.3;
    private const double QWeight = 0.2;
    private const double CorrectThreshold = 0.8;

    public string ExerciseName => "EqMatching";

    public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
    {
        var expected = JObject.Parse(expectedAnswerJson);
        var targetFrequency = expected["frequencyHz"]!.Value<double>();
        var targetGain = expected["gainDb"]!.Value<double>();
        var targetQ = expected["q"]!.Value<double>();
        var scoringVersion = expected["scoringVersion"] switch
        {
            null => 1,
            { Type: JTokenType.Integer } version => version.Value<int>(),
            _ => throw new InvalidOperationException("Invalid EQ Matching scoring version."),
        };
        if (scoringVersion is not (1 or CurrentScoringVersion))
            throw new InvalidOperationException($"Unsupported EQ Matching scoring version: {scoringVersion}.");
        var canonical = FormattableString.Invariant($"{targetFrequency:R}|{targetGain:R}|{targetQ:R}");
        var values = (userGuess ?? string.Empty).Split('|');
        if (values.Length != 3
            || !TryNumber(values[0], 20, 20000, out var frequency)
            || !TryNumber(values[1], -12, 12, out var gain)
            || !TryNumber(values[2], 0.25, 8, out var q))
        {
            return new ExerciseValidationResult(false, canonical,
                new EqMatchingValidationDetail(targetFrequency, targetGain, targetQ, null, null, null, 0,
                    scoringVersion, ["invalidGuess"]));
        }

        var frequencyDistance = Math.Abs(Math.Log2(frequency / targetFrequency));
        var gainDistance = Math.Abs(gain - targetGain);
        var qDistance = Math.Abs(Math.Log2(q / targetQ));
        var frequencyError = NormalizedError(frequencyDistance, 1.0 / 6, 1);
        var gainError = NormalizedError(gainDistance, 1.5, 6);
        var qError = NormalizedError(qDistance, Math.Log2(1.2), 1);
        var score = Math.Clamp(1 - FrequencyWeight * frequencyError - GainWeight * gainError - QWeight * qError, 0, 1);
        var failedCriteria = new List<string>();
        if (scoringVersion == CurrentScoringVersion)
        {
            if (gain == 0 || Math.Sign(gain) != Math.Sign(targetGain))
                failedCriteria.Add("direction");
            if (!WithinLimit(frequencyDistance, 400.0 / 1200))
                failedCriteria.Add("frequency");
            if (!WithinLimit(gainDistance, Math.Min(3, Math.Abs(targetGain) / 2)))
                failedCriteria.Add("gain");
            if (!WithinLimit(qDistance, Math.Log2(1.5)))
                failedCriteria.Add("q");
        }
        if (!(score > CorrectThreshold))
            failedCriteria.Add("score");
        return new ExerciseValidationResult(failedCriteria.Count == 0, canonical,
            new EqMatchingValidationDetail(targetFrequency, targetGain, targetQ, frequency, gain, q, score,
                scoringVersion, failedCriteria));
    }

    // Include mathematical boundaries despite floating-point roundoff.
    private static bool WithinLimit(double error, double limit) => error <= limit + 1e-12;

    private static double NormalizedError(double error, double fullCreditTolerance, double noCreditAt) =>
        Math.Clamp((error - fullCreditTolerance) / (noCreditAt - fullCreditTolerance), 0, 1);

    private static bool TryNumber(string value, double minimum, double maximum, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number) && number >= minimum && number <= maximum;
}

public sealed record EqMatchingValidationDetail(
    double TargetFrequencyHz,
    double TargetGainDb,
    double TargetQ,
    double? GuessFrequencyHz,
    double? GuessGainDb,
    double? GuessQ,
    double Score,
    int ScoringVersion,
    IReadOnlyList<string> FailedCriteria);
