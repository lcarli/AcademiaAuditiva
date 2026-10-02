namespace AcademiaAuditiva.ViewModels
{
    public class ExerciseFiltersViewModel
    {
        public string Instrument { get; set; }
        public string Range { get; set; } // Ex: C3-C4

        /// <summary>The exercise-specific filter selects declared in <c>Exercise.FiltersJson</c>.</summary>
        public IReadOnlyList<FilterOptionGroup> Groups { get; set; } = Array.Empty<FilterOptionGroup>();

        /// <summary>
        /// Whether the exercise is about chords: it then only offers the instruments that play them,
        /// and its note range starts where they play chords.
        /// </summary>
        public bool IsChordExercise { get; set; }
    }
}
