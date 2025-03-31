using AcademiaAuditiva.Extensions;

namespace AcademiaAuditiva.Models
{
    public class Exercise
    {
        public int ExerciseId { get; set; }
        public string Name { get; set; }
        public ExerciseType Type { get; set; }
        public string Description { get; set; }
        public DifficultyLevel Difficulty { get; set; }
        public ExerciseCategory Category { get; set; }
        public string? FiltersJson { get; set; }
    }

}
