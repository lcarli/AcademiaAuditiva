using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.ExerciseValidators;

public sealed class ExpandedAudioExerciseValidator : IExerciseValidator
{
    public ExpandedAudioExerciseValidator(string name)
    {
        if (!AudioComparisonCatalog.Expansion.Any(definition => definition.Name == name))
            throw new ArgumentException("Unknown expanded Audio exercise.", nameof(name));
        ExerciseName = name;
    }

    public string ExerciseName { get; }

    public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
    {
        var expected = JObject.Parse(expectedAnswerJson);
        return ValidatorHelpers.MatchSingleField((userGuess ?? "").Trim(), expectedAnswerJson, "answer") with
        {
            Detail = new ExpandedAudioValidationDetail(
                expected["answer"]!.Value<string>()!,
                expected["parameters"]!.ToObject<double[]>()!),
        };
    }
}

public sealed record ExpandedAudioValidationDetail(string Answer, IReadOnlyList<double> Parameters);
