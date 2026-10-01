using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.ViewModels;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Extensions
{
    public static class ExerciseExtensions
    {
        // The filter selects are rendered (and HTML-encoded) by _ExerciseFilters.cshtml;
        // the localizer parameter is kept so existing callers don't change.
        public static ExerciseViewModel ToViewModel(this Exercise exercise, IStringLocalizer localizer)
        {
            return new ExerciseViewModel
            {
                ExerciseId = exercise.ExerciseId,
                Title = exercise.Name,
                Instructions = exercise.Instructions,
                Tips = exercise.Tips,
                Score = 0,
                Attempts = 0,
                TimeSpent = "00:00:00",
                FeedbackMessage = null,
                FeedbackType = null,
                Filters = new ExerciseFiltersViewModel
                {
                    Groups = ExerciseFilterPresets.Groups(exercise.FiltersJson)
                },
                AudioButtons = exercise.AudioButtons,
                AnswerButtons = exercise.AnswerButtons
            };
        }
    }
}