using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    /// <summary>
    /// Filter preset posted as <c>Filters[groupName]=optionValue</c>; an empty
    /// value lets the student choose. Sanitized against the exercise's groups
    /// before it is stored in <c>RoutineItem.FilterJson</c>.
    /// </summary>
    public Dictionary<string, string?> Filters { get; set; } = new();

    public IReadOnlyList<ExerciseOption> ExerciseOptions { get; set; } = Array.Empty<ExerciseOption>();
}

/// <param name="Name">Localized exercise name.</param>
/// <param name="FilterGroups">Filter selects the exercise offers (may be empty).</param>
public record ExerciseOption(int Id, string Name, IReadOnlyList<FilterOptionGroup> FilterGroups);

/// <summary>Per-student customization of a classroom assignment.</summary>
public class RoutineOverridesViewModel
{
    public int RoutineId { get; set; }
    public int AssignmentId { get; set; }
    public string? StudentId { get; set; }

    [BindNever] public string RoutineName { get; set; } = string.Empty;
    [BindNever] public string ClassroomName { get; set; } = string.Empty;
    [BindNever] public string? StudentDisplay { get; set; }
    [BindNever] public IReadOnlyList<OverrideStudentOption> Students { get; set; } = Array.Empty<OverrideStudentOption>();

    public List<RoutineItemOverrideInput> Items { get; set; } = new();
}

public record OverrideStudentOption(string Id, string Display, int OverrideCount);

public class RoutineItemOverrideInput
{
    public int ItemId { get; set; }

    [Display(Name = "Teacher.Overrides.Exclude")]
    public bool Exclude { get; set; }

    [Range(1, 100, ErrorMessage = "Validation.Range"), Display(Name = "Teacher.Routines.TargetCount")]
    public int? TargetCount { get; set; }

    /// <summary>Same shape as <see cref="RoutineItemFormViewModel.Filters"/>; empty keeps the routine setting.</summary>
    public Dictionary<string, string?> Filters { get; set; } = new();

    [BindNever] public string ExerciseName { get; set; } = string.Empty;
    [BindNever] public int DefaultTarget { get; set; }
    [BindNever] public IReadOnlyList<FilterOptionGroup> FilterGroups { get; set; } = Array.Empty<FilterOptionGroup>();
    [BindNever] public IReadOnlyDictionary<string, string> DefaultFilters { get; set; } = new Dictionary<string, string>();
}

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
