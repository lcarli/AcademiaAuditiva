using System.ComponentModel.DataAnnotations.Schema;
using AcademiaAuditiva.Extensions;
using Newtonsoft.Json;

namespace AcademiaAuditiva.Models
{
    public class Exercise
    {
        public int ExerciseId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string? FiltersJson { get; set; }
        public string? Instructions { get; set; }
        public string? TipsJson { get; set; }
        public string? AudioButtonsJson { get; set; }
        public string? AnswerButtonsJson { get; set; }
        public int ExerciseTypeId { get; set; }
        public ExerciseType ExerciseType { get; set; }

        public int ExerciseCategoryId { get; set; }
        public ExerciseCategory ExerciseCategory { get; set; }

        public int DifficultyLevelId { get; set; }
        public DifficultyLevel DifficultyLevel { get; set; }

        [NotMapped]
        public List<string> Tips
        {
            get => string.IsNullOrEmpty(TipsJson) 
                ? new List<string>() 
                : JsonConvert.DeserializeObject<List<string>>(TipsJson);
            set => TipsJson = JsonConvert.SerializeObject(value);
        }

        [NotMapped]
        public List<string> AudioButtons
        {
            get => string.IsNullOrEmpty(AudioButtonsJson)
                ? new List<string>()
                : JsonConvert.DeserializeObject<List<string>>(AudioButtonsJson);
            set => AudioButtonsJson = JsonConvert.SerializeObject(value);
        }

        [NotMapped]
        public Dictionary<string, Dictionary<string, string>> AnswerButtons
        {
            get => string.IsNullOrEmpty(AnswerButtonsJson)
                ? new Dictionary<string, Dictionary<string, string>>()
                : JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(AnswerButtonsJson);
            set => AnswerButtonsJson = JsonConvert.SerializeObject(value);
        }
    }

}
