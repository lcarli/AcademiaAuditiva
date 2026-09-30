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
        private readonly IMusicTheoryService _theory;
        public GuessNoteValidator(IMusicTheoryService theory) { _theory = theory; }

        public string ExerciseName => "GuessNote";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedNote = (string?)obj["note"] ?? string.Empty;
            var isCorrect = _theory.NotesAreEquivalent(userGuess, expectedNote);
            return new ExerciseValidationResult(isCorrect, expectedNote);
        }
    }

    public sealed class GuessChordsValidator : IExerciseValidator
    {
        private readonly IMusicTheoryService _theory;
        public GuessChordsValidator(IMusicTheoryService theory) { _theory = theory; }

        public string ExerciseName => "GuessChords";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedRoot = (string?)obj["root"] ?? string.Empty;
            var expectedQuality = (string?)obj["quality"] ?? string.Empty;
            var actualChord = $"{expectedRoot}|{expectedQuality}";
            var isCorrect = _theory.AnswersAreEquivalent(userGuess, actualChord);
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

    public sealed class HigherOrLowerValidator : IExerciseValidator
    {
        public string ExerciseName => "HigherOrLower";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "answer");
    }

    public sealed class GuessScaleTypeValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessScaleType";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "scaleType");
    }

    public sealed class GuessGreekModeValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessGreekMode";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "mode");
    }

    public sealed class GuessCadenceValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessCadence";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "cadence");
    }

    public sealed class GuessInversionValidator : IExerciseValidator
    {
        public string ExerciseName => "GuessInversion";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => ValidatorHelpers.MatchSingleField(userGuess, expectedAnswerJson, "inversion");
    }

    /// <summary>
    /// Shared logic for staff-based exercises (CompleteScale, CompleteChord,
    /// TransposeScale, MelodicDictation, RhythmDictation). Compares the
    /// pipe-separated <c>answerString</c> field after canonicalising both
    /// sides: trim, lower-case, normalise enharmonic-free accidentals
    /// (♯→#, ♭→b, ♮ stripped), unify rest aliases (B4:qr → rest:qr) and
    /// barline aliases (barline → bar). Note name and duration label are
    /// compared verbatim — enharmonic spelling matters (matches SonicMind).
    /// </summary>
    internal static class StaffSequenceHelpers
    {
        public static ExerciseValidationResult Compare(
            string userGuess,
            string expectedAnswerJson,
            bool durationOnly = false)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expected = (string?)obj["answerString"] ?? string.Empty;

            var canonicalExpected = Canonicalise(expected, durationOnly);
            var canonicalGuess = Canonicalise(userGuess ?? string.Empty, durationOnly);

            var isCorrect = string.Equals(canonicalExpected, canonicalGuess, StringComparison.Ordinal);
            return new ExerciseValidationResult(isCorrect, expected);
        }

        private static string Canonicalise(string raw, bool durationOnly)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var tokens = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var canon = new List<string>(tokens.Length);

            foreach (var tRaw in tokens)
            {
                var t = tRaw.Trim().ToLowerInvariant()
                    .Replace("♯", "#")
                    .Replace("♭", "b")
                    .Replace("♮", "");

                if (t == "barline" || t == "bar") { canon.Add("bar"); continue; }

                if (durationOnly)
                {
                    // Rhythm dictation: drop note-name half if present.
                    var idx = t.IndexOf(':');
                    if (idx >= 0) t = t.Substring(idx + 1);
                    canon.Add(t);
                    continue;
                }

                // Note:duration form. Treat any "<pitch>:<dur>r" or "rest:<dur>r" as rest.
                var parts = t.Split(':');
                if (parts.Length == 2)
                {
                    var notePart = parts[0];
                    var durPart = parts[1];
                    var isRest = notePart == "rest" || durPart.EndsWith("r");
                    if (isRest)
                    {
                        // Normalise: rest:<base>r
                        var baseDur = durPart.EndsWith("r") ? durPart : durPart + "r";
                        canon.Add($"rest:{baseDur}");
                    }
                    else
                    {
                        canon.Add($"{notePart}:{durPart}");
                    }
                }
                else
                {
                    canon.Add(t);
                }
            }

            return string.Join("|", canon);
        }
    }

    public sealed class CompleteScaleValidator : IExerciseValidator
    {
        public string ExerciseName => "CompleteScale";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => StaffSequenceHelpers.Compare(userGuess, expectedAnswerJson);
    }

    public sealed class CompleteChordValidator : IExerciseValidator
    {
        public string ExerciseName => "CompleteChord";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => StaffSequenceHelpers.Compare(userGuess, expectedAnswerJson);
    }

    public sealed class TransposeScaleValidator : IExerciseValidator
    {
        public string ExerciseName => "TransposeScale";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => StaffSequenceHelpers.Compare(userGuess, expectedAnswerJson);
    }

    public sealed class MelodicDictationValidator : IExerciseValidator
    {
        public string ExerciseName => "MelodicDictation";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => StaffSequenceHelpers.Compare(userGuess, expectedAnswerJson);
    }

    public sealed class RhythmDictationValidator : IExerciseValidator
    {
        public string ExerciseName => "RhythmDictation";
        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
            => StaffSequenceHelpers.Compare(userGuess, expectedAnswerJson, durationOnly: true);
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
