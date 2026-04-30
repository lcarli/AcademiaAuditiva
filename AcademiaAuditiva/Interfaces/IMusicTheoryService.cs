using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Interfaces
{
    /// <summary>
    /// Music-theory operations used by controllers and exercise validators.
    /// Backed by the static <c>MusicTheoryService</c> in production; mocked
    /// in unit tests so call sites don't depend on the static surface.
    /// Only the methods that are actually invoked from outside the music-
    /// theory module are listed — the rest stays internal to the static
    /// class as helper logic.
    /// </summary>
    public interface IMusicTheoryService
    {
        bool NotesAreEquivalent(string note1, string note2);
        bool AnswersAreEquivalent(string userAnswer, string correctAnswer);
        object GenerateNoteForExercise(Exercise exercise, Dictionary<string, string> filters);
    }
}
