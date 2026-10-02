namespace AcademiaAuditiva.ViewModels
{
    public class ExerciseFiltersViewModel
    {
        public string Instrument { get; set; }
        public string Range { get; set; } // Ex: C3-C4

        /// <summary>The exercise-specific filter selects declared in <c>Exercise.FiltersJson</c>.</summary>
        public IReadOnlyList<FilterOptionGroup> Groups { get; set; } = Array.Empty<FilterOptionGroup>();

        /// <summary>
        /// Whether the exercise is about chords: it then only offers the instruments that play them.
        /// </summary>
        public bool IsChordExercise { get; set; }

        /// <summary>
        /// Whether the exercise plays chords: on the guitar, the student then picks where on the
        /// neck to play them instead of the note range.
        /// </summary>
        public bool PlaysChords { get; set; }
    }
}
