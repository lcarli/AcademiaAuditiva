using System.Collections.Generic;

namespace AcademiaAuditiva.ViewModels
{
    public class ExerciseViewModel
    {
        public string Title { get; set; }
        public string Instruction { get; set; }
        public Dictionary<string, string> AnswerButtons { get; set; } = new();
        public string SelectedAnswer { get; set; }
        public string CorrectAnswer { get; set; }
        public string FeedbackMessage { get; set; }
        public string FeedbackType { get; set; }
        public int Score { get; set; }
        public int Attempts { get; set; }
        public string TimeSpent { get; set; }
        public ExerciseFiltersViewModel Filters { get; set; } = new();
        public string CurrentLanguage { get; set; }
        public string Instructions { get; set; }
        public List<string> Tips { get; set; } = new();
        public List<string> AudioButtons { get; set; } = new();
    }
}
