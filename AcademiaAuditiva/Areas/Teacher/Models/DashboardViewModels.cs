namespace AcademiaAuditiva.Areas.Teacher.Models;

public class ClassroomDashboardViewModel
{
    public int ClassroomId { get; set; }
    public string ClassroomName { get; set; } = string.Empty;
    public IReadOnlyList<ClassroomDashboardRow> Rows { get; set; } = Array.Empty<ClassroomDashboardRow>();
    public int TotalAttempts { get; set; }
    public int TotalSessions { get; set; }
    public double AverageAccuracy { get; set; }
    public int ActiveStudentsLast30Days { get; set; }
}

public class ClassroomDashboardRow
{
    public string StudentId { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public int Attempts { get; set; }
    public double Accuracy { get; set; }
    public DateTime? LastActivity { get; set; }
    public bool ActiveLast30Days { get; set; }
}

public class StudentDashboardViewModel
{
    public string StudentId { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<ClassroomOption> Classrooms { get; set; } = Array.Empty<ClassroomOption>();
    public int? BackClassroomId { get; set; }
    public int TotalSessions { get; set; }
    public int TotalAttempts { get; set; }
    public double OverallAccuracy { get; set; }
    public bool ActiveLast30Days { get; set; }
    public DateTime? LastActivity { get; set; }
    public IReadOnlyList<StudentExerciseRow> ByExercise { get; set; } = Array.Empty<StudentExerciseRow>();
}

public class StudentExerciseRow
{
    public string ExerciseName { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public double Accuracy { get; set; }
    public int BestScore { get; set; }
    public DateTime LastActivity { get; set; }
}
