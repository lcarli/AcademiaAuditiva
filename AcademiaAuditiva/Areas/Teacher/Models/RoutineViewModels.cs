using System.ComponentModel.DataAnnotations;

namespace AcademiaAuditiva.Areas.Teacher.Models;

public class RoutineFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Validation.Required"), StringLength(120, ErrorMessage = "Validation.MaxLength")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Validation.MaxLength")]
    public string? Description { get; set; }
}

public class RoutineListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ItemCount { get; set; }
    public int AssignmentCount { get; set; }
}

public class RoutineItemFormViewModel
{
    public int Id { get; set; }
    public int RoutineId { get; set; }

    [Required(ErrorMessage = "Validation.Required"), Display(Name = "Dashboard.Exercise")]
    public int ExerciseId { get; set; }

    [Range(1, 100, ErrorMessage = "Validation.Range"), Display(Name = "Teacher.Routines.TargetCount")]
    public int TargetCount { get; set; } = 10;

    [Range(0, 100, ErrorMessage = "Validation.Range"), Display(Name = "Teacher.Routines.MinimumScore")]
    public int? MinScore { get; set; }

    [StringLength(4000, ErrorMessage = "Validation.MaxLength"), Display(Name = "Teacher.Routines.FilterJson")]
    public string? FilterJson { get; set; }

    public IReadOnlyList<ExerciseOption> ExerciseOptions { get; set; } = Array.Empty<ExerciseOption>();
}

public record ExerciseOption(int Id, string Name);

public class AssignRoutineViewModel
{
    public int RoutineId { get; set; }
    public string RoutineName { get; set; } = string.Empty;

    [Display(Name = "Teacher.Routines.Target")]
    public string Target { get; set; } = "classroom"; // "classroom" | "student"

    public int? ClassroomId { get; set; }
    public string? StudentId { get; set; }

    [DataType(DataType.Date), Display(Name = "Teacher.Routines.DueDate")]
    public DateTime? DueAt { get; set; }

    public IReadOnlyList<ClassroomOption> Classrooms { get; set; } = Array.Empty<ClassroomOption>();
    public IReadOnlyList<StudentOption> Students { get; set; } = Array.Empty<StudentOption>();
}

public record ClassroomOption(int Id, string Name);
public record StudentOption(string Id, string Display);
