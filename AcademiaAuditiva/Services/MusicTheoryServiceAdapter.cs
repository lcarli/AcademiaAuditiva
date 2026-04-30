using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// Thin adapter that exposes the static <see cref="MusicTheoryService"/>
    /// through <see cref="IMusicTheoryService"/> for DI / mocking. The static
    /// class is intentionally kept as-is because most of its surface is
    /// pure-function helper code with no state.
    /// </summary>
    public sealed class MusicTheoryServiceAdapter : IMusicTheoryService
    {
        public bool NotesAreEquivalent(string note1, string note2)
            => MusicTheoryService.NotesAreEquivalent(note1, note2);

        public bool AnswersAreEquivalent(string userAnswer, string correctAnswer)
            => MusicTheoryService.AnswersAreEquivalent(userAnswer, correctAnswer);

        public object GenerateNoteForExercise(Exercise exercise, Dictionary<string, string> filters)
            => MusicTheoryService.GenerateNoteForExercise(exercise, filters);
    }
}
