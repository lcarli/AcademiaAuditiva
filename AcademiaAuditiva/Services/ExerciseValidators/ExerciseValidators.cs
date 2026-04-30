using AcademiaAuditiva.Interfaces;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.ExerciseValidators
{
    /// <summary>
    /// Shared helpers for the simple "compare a single JSON field as a
    /// case-insensitive string" validators (GuessInterval, GuessMissingNote,
    /// GuessFullInterval, GuessFunction, GuessQuality).
    /// </summary>
    internal static class ValidatorHelpers
    {
        public static ExerciseValidationResult MatchSingleField(
            string userGuess,
            string expectedAnswerJson,
            string fieldName)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expected = (string?)obj[fieldName] ?? string.Empty;
            var isCorrect = string.Equals(userGuess, expected, StringComparison.OrdinalIgnoreCase);
            return new ExerciseValidationResult(isCorrect, expected);
        }
    }

    public sealed class GuessNoteValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessNote";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedNote = (string?)obj["note"] ?? string.Empty;
            var isCorrect = MusicTheoryService.NotesAreEquivalent(userGuess, expectedNote);
            return new ExerciseValidationResult(isCorrect, expectedNote);
        }
    }

    public sealed class GuessChordsValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessChords";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedRoot = (string?)obj["root"] ?? string.Empty;
            var expectedQuality = (string?)obj["quality"] ?? string.Empty;
            var actualChord = $"{expectedRoot}|{expectedQuality}";
            var isCorrect = MusicTheoryService.AnswersAreEquivalent(userGuess, actualChord);
            return new ExerciseValidationResult(isCorrect, actualChord);
        }
    }

    public sealed class GuessIntervalValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessInterval";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class GuessMissingNoteValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessMissingNote";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class GuessFullIntervalValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessFullInterval";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class GuessFunctionValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessFunction";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class GuessQualityValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessQuality";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class IntervalMelodicoValidator : IExerciseValidator
    {
        public string ExerciseName => "IntervalMelodico";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedFirst = obj["firstDegree"]?.ToString() ?? "1";
            var expectedLast = obj["lastDegree"]?.ToString() ?? "1";
            var expectedStart = obj["startInterval"]?.ToString() ?? "Unísono";
            var expectedEnd = obj["endInterval"]?.ToString() ?? "Unísono";
            var canonical = $"{expectedFirst}|{expectedLast}|{expectedStart}|{expectedEnd}";

            var parts = (userGuess ?? string.Empty).Split('|');
            if (parts.Length != 4)
            {
                return new ExerciseValidationResult(false, canonical);
            }

            var isCorrect =
                string.Equals(parts[0], expectedFirst, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[1], expectedLast, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[2], expectedStart, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[3], expectedEnd, StringComparison.OrdinalIgnoreCase);

            return new ExerciseValidationResult(isCorrect, canonical);
        }
    }
}
