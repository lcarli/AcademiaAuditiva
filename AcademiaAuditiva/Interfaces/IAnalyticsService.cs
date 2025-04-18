using System.Threading.Tasks;

namespace AcademiaAuditiva.Interfaces;

public interface IAnalyticsService
{
    Task SaveAttemptAsync(ExerciseAttemptLog log);
}