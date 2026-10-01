using AcademiaAuditiva.Interfaces;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.ExerciseValidators
{
    /// <summary>
    /// Shared helpers for the simple "compare a single JSON field as a
    /// case-insensitive string" validators (GuessInterval, GuessMissingNote,
    /// GuessFunction, GuessQuality).
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

    /// <summary>
    /// Interval answers are case-sensitive codes: "2m" (minor 2nd) is not
    /// "2M" (major 2nd). The tritone has two names, "4A" (augmented 4th)
    /// and "5d" (diminished 5th), and either one is accepted.
    /// </summary>
    internal static class IntervalCodes
    {
        public static bool Equivalent(string? guess, string? expected)
        {
            var normalized = Normalize(guess);
            return normalized.Length > 0 && string.Equals(normalized, Normalize(expected), StringComparison.Ordinal);
        }

        private static string Normalize(string? code)
        {
            var trimmed = (code ?? string.Empty).Trim();
            return trimmed == "5d" ? "4A" : trimmed;
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
        {
            var expected = (string?)JObject.Parse(expectedAnswerJson)["answer"] ?? string.Empty;
            return new ExerciseValidationResult(IntervalCodes.Equivalent(userGuess, expected), expected);
        }
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
            var expectedFirst = obj["firstDegree"]?.ToString() ?? string.Empty;
            var expectedLast = obj["lastDegree"]?.ToString() ?? string.Empty;
            var expectedStart = obj["startInterval"]?.ToString() ?? string.Empty;
            var expectedEnd = obj["endInterval"]?.ToString() ?? string.Empty;
            var canonical = $"{expectedFirst}|{expectedLast}|{expectedStart}|{expectedEnd}";

            var parts = (userGuess ?? string.Empty).Split('|');
            if (parts.Length != 4 || expectedFirst.Length == 0 || expectedLast.Length == 0)
            {
                return new ExerciseValidationResult(false, canonical);
            }

            // Degrees are roman numerals (case does not matter); intervals are case-sensitive codes.
            var isCorrect =
                string.Equals(parts[0].Trim(), expectedFirst, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[1].Trim(), expectedLast, StringComparison.OrdinalIgnoreCase) &&
                IntervalCodes.Equivalent(parts[2], expectedStart) &&
                IntervalCodes.Equivalent(parts[3], expectedEnd);

            return new ExerciseValidationResult(isCorrect, canonical);
        }
    }

    /// <summary>
    /// Compares the notes detected in the student's recording ("C4|E4|G4")
    /// with the melody shown on the staff. Singers may use any octave, so only
    /// pitch classes are compared (enharmonic spellings are equal). Rests and
    /// repeated notes are ignored because the pitch tracker cannot tell a held
    /// note from a repeated one. Melodies of four or more notes tolerate one
    /// missing, extra or wrong note.
    /// </summary>
    public sealed class SolfegeMelodyValidator : IExerciseValidator
    {
        private const int MaxGuessLength = 1024;

        public string ExerciseName => "SolfegeMelody";

        public ExerciseValidationResult Validate(string userGuess, string expectedAnswerJson)
        {
            var obj = JObject.Parse(expectedAnswerJson);
            var expectedNotes = (obj["melody"] as JArray ?? new JArray())
                .Where(m => (string?)m["type"] == "note")
                .Select(m => (string?)m["note"] ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
            var canonical = string.Join("|", expectedNotes);

            if (expectedNotes.Count == 0 || string.IsNullOrWhiteSpace(userGuess) || userGuess.Length > MaxGuessLength)
            {
                return new ExerciseValidationResult(false, canonical);
            }

            var expected = CollapseRepeats(expectedNotes.Select(PitchClass));
            var sung = CollapseRepeats(userGuess
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(PitchClass));
            if (sung.Count == 0)
            {
                return new ExerciseValidationResult(false, canonical);
            }

            var tolerance = expected.Count >= 4 ? 1 : 0;
            return new ExerciseValidationResult(EditDistance(expected, sung) <= tolerance, canonical);
        }

        /// <summary>Pitch class 0-11 of a note name with or without octave; -1 when it is not a note.</summary>
        internal static int PitchClass(string note)
        {
            var name = note.Trim();
            if (name.Length > 0 && !char.IsDigit(name[^1]))
            {
                name += "4";
            }
            return MusicTheoryService.NoteToMidi(name) is int midi ? ((midi % 12) + 12) % 12 : -1;
        }

        private static List<int> CollapseRepeats(IEnumerable<int> notes)
        {
            var result = new List<int>();
            foreach (var note in notes)
            {
                if (result.Count == 0 || result[^1] != note)
                {
                    result.Add(note);
                }
            }
            return result;
        }

        private static int EditDistance(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            var previous = new int[b.Count + 1];
            var current = new int[b.Count + 1];
            for (var j = 0; j <= b.Count; j++)
            {
                previous[j] = j;
            }

            for (var i = 1; i <= a.Count; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Count; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Count];
        }
    }
}
