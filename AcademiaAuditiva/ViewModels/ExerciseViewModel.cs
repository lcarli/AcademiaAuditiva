using System.Collections.Generic;

namespace AcademiaAuditiva.ViewModels
{
    public class ExerciseViewModel
    {
        // Dados gerais do exercício
        public string Title { get; set; }
        public string Instruction { get; set; }
        public List<string> AnswerOptions { get; set; } = new();
        public string SelectedAnswer { get; set; }
        public string CorrectAnswer { get; set; }

        // Feedback
        public string FeedbackMessage { get; set; } // Ex: "Acertou!" ou "Errou, a nota era C"
        public string FeedbackType { get; set; } // success, danger, info (para Bootstrap)

        // Progresso
        public int Score { get; set; }
        public int Attempts { get; set; }
        public string TimeSpent { get; set; }

        // Filtros aplicáveis
        public ExerciseFiltersViewModel Filters { get; set; } = new();

        // Auxiliar para idiomas
        public string CurrentLanguage { get; set; }
    }
}
