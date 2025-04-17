using AcademiaAuditiva.Models;
using AcademiaAuditiva.ViewModels;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;

namespace AcademiaAuditiva.Extensions
{
    public static class ExerciseExtensions
    {
        public static ExerciseViewModel ToViewModel(this Exercise exercise, IStringLocalizer localizer)
        {
            var filters = string.IsNullOrEmpty(exercise.FiltersJson) ? new List<FilterOptionGroup>() : JsonConvert.DeserializeObject<List<FilterOptionGroup>>(exercise.FiltersJson);

            var filtersHtml = new StringBuilder();

            foreach (var group in filters)
            {
                filtersHtml.AppendLine($@"
                    <div class='mb-3'>
                        <label for='{group.Name}' class='form-label'>{localizer[group.Label]}</label>
                        <select id='{group.Name}' name='{group.Name}' class='form-select'>");

                foreach (var opt in group.Options)
                {
                    filtersHtml.AppendLine($@"<option value='{opt.Value}'>{localizer[opt.Text]}</option>");
                }

                filtersHtml.AppendLine("</select></div>");
            }

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
                Filters = new ExerciseFiltersViewModel
                {
                    CustomFiltersHtml = filtersHtml.ToString()
                },
                AudioButtons = exercise.AudioButtons,
                AnswerButtons = exercise.AnswerButtons
            };
        }
    }
}