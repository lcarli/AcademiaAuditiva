using System.ComponentModel.DataAnnotations.Schema;
using AcademiaAuditiva.Extensions;
using Newtonsoft.Json;

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
        public string? Instructions { get; set; }
        public string? TipsJson { get; set; }
        [NotMapped]
        public List<string> Tips
        {
            get => string.IsNullOrEmpty(TipsJson) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(TipsJson);
            set => TipsJson = JsonConvert.SerializeObject(value);
        }
    }

}
