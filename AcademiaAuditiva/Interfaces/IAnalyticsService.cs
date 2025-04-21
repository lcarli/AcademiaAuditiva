using System.Threading.Tasks;

namespace AcademiaAuditiva.Interfaces;

public interface IAnalyticsService
{
    Task SaveAttemptAsync(ExerciseAttemptLog log);
    Task<List<ExerciseAttemptLog>> GetAttemptsAsync(string userId, string? exercise = null);
}