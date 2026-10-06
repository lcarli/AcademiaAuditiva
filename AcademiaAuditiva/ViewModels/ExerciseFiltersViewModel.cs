namespace AcademiaAuditiva.ViewModels
{
    public class ExerciseFiltersViewModel
    {
        /// <summary>The exercise the filters are for; a routine question sets some of them (see <c>RoutinePage</c>).</summary>
        public int ExerciseId { get; set; }

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
