using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.ViewModels;

public sealed record ExerciseCatalogFiltersViewModel(
    IReadOnlyList<ExerciseCategory> Categories,
    IReadOnlyList<ExerciseType> Types,
    IReadOnlyList<DifficultyLevel> Levels,
    string? Category,
    string? Type,
    string? Level,
    int ResultsCount,
    bool TrackHasExercises)
{
    public bool IsFiltered => Category is not null || Type is not null || Level is not null;
}
