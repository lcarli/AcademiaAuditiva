using System.Globalization;
using AcademiaAuditiva.Interfaces;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.ExerciseValidators;

public sealed class EqMatchingValidator : IExerciseValidator
{
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
        var canonical = FormattableString.Invariant($"{targetFrequency:R}|{targetGain:R}|{targetQ:R}");
        var values = (userGuess ?? string.Empty).Split('|');
        if (values.Length != 3
            || !TryNumber(values[0], 20, 20000, out var frequency)
            || !TryNumber(values[1], -12, 12, out var gain)
            || !TryNumber(values[2], 0.25, 8, out var q))
        {
            return new ExerciseValidationResult(false, canonical,
                new EqMatchingValidationDetail(targetFrequency, targetGain, targetQ, null, null, null, 0));
        }

        var frequencyError = NormalizedError(Math.Abs(Math.Log2(frequency / targetFrequency)), 1.0 / 6, 1);
        var gainError = NormalizedError(Math.Abs(gain - targetGain), 1.5, 6);
        var qError = NormalizedError(Math.Abs(Math.Log2(q / targetQ)), Math.Log2(1.2), 1);
        var score = Math.Clamp(1 - FrequencyWeight * frequencyError - GainWeight * gainError - QWeight * qError, 0, 1);
        return new ExerciseValidationResult(score > CorrectThreshold, canonical,
            new EqMatchingValidationDetail(targetFrequency, targetGain, targetQ, frequency, gain, q, score));
    }

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
    double Score);
