using AcademiaAuditiva.Models;
using AcademiaAuditiva.ViewModels;
using Newtonsoft.Json;

namespace AcademiaAuditiva.Extensions
{
    public static class ExerciseExtensions
    {
        public static ExerciseViewModel ToViewModel(this Exercise exercise)
        {
            return new ExerciseViewModel
            {
                Title = exercise.Description,
                Instructions = exercise.Instructions,
                Tips = string.IsNullOrEmpty(exercise.TipsJson)
                    ? new List<string>()
                    : JsonConvert.DeserializeObject<List<string>>(exercise.TipsJson),
                Score = 0,
                Attempts = 0,
                TimeSpent = "00:00:00",
                FeedbackMessage = null,
                FeedbackType = null,
                Filters = JsonConvert.DeserializeObject<ExerciseFiltersViewModel>(exercise.FiltersJson) ?? new ExerciseFiltersViewModel(),
                AnswerOptions = new List<string>()
            };
        }
    }
}
