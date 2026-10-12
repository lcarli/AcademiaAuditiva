using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.ViewModels;

/// <param name="Levels">The supported levels to summarize, or null in a difficulty section.</param>
public sealed record ExerciseCatalogCardViewModel(
    Exercise Exercise,
    string CategoryLabel,
    IReadOnlyList<DifficultyLevel>? Levels = null,
    IReadOnlyDictionary<string, object?>? Filters = null);
