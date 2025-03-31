using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Models
{
    public class Score
    {
        public int ScoreId { get; set; }

        public string UserId { get; set; }
        public IdentityUser User { get; set; }
        public int ExerciseId { get; set; }
        public Exercise Exercise { get; set; }

        public int CorrectCount { get; set; }
        public int ErrorCount { get; set; }
        public int BestScore { get; set; }
        public int TimeSpentSeconds { get; set; } = 60;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}
