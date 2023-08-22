using AcademiaAuditiva.Extensions;

namespace AcademiaAuditiva.Models
{
    public class Exercise
    {
        public int ExerciseId { get; set; }
        public string Name { get; set; }
        public ExerciseType Type { get; set; } // Usando o enum para o tipo
        public string Description { get; set; }
        public DifficultyLevel Difficulty { get; set; } // Usando o enum para dificuldade

    }

}
