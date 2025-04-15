using AcademiaAuditiva.Models;
using AcademiaAuditiva.ViewModels;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AcademiaAuditiva.Extensions
{
    public static class ExerciseExtensions
    {
        public static ExerciseViewModel ToViewModel(this Exercise exercise)
        {
            return new ExerciseViewModel
            {
                ExerciseId = exercise.ExerciseId,
                Title = exercise.Description,
                Instructions = exercise.Instructions,
                Tips = exercise.Tips,
                Score = 0,
                Attempts = 0,
                TimeSpent = "00:00:00",
                FeedbackMessage = null,
                FeedbackType = null,
                Filters = string.IsNullOrEmpty(exercise.FiltersJson)
                    ? new ExerciseFiltersViewModel()
                    : JsonConvert.DeserializeObject<ExerciseFiltersViewModel>(exercise.FiltersJson),
                AudioButtons = exercise.AudioButtons,
                AnswerButtons = exercise.AnswerButtons
            };
        }
    }
}