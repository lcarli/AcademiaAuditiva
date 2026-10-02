using System.Diagnostics.CodeAnalysis;

namespace AcademiaAuditiva.Services.Tutorials;

/// <param name="Key">Names the step's texts: Tutorial.{tour}.{step}.Title and .Text.</param>
/// <param name="Target">
/// CSS selectors for what the step points at; null centers the step on the screen.
/// The tour frames every visible match together and skips the step when none is
/// visible, so a list can name fallbacks for other layouts.
/// </param>
public sealed record TutorialStep(string Key, string? Target = null);

public sealed record TutorialDefinition(string Key, IReadOnlyList<TutorialStep> Steps)
{
    public string TitleKey(TutorialStep step) => $"Tutorial.{Key}.{step.Key}.Title";

    public string TextKey(TutorialStep step) => $"Tutorial.{Key}.{step.Key}.Text";
}

/// <summary>
/// The guided tours that start the first time a user opens a page
/// (see TutorialViewComponent and wwwroot/js/core/tutorial.js).
/// </summary>
public static class TutorialCatalog
{
    public const string Dashboard = "Dashboard";
    public const string Exercise = "Exercise";

    // The last step points at the button that replays the tour.
    private const string ReplayButton = "[data-aa-tour-start]";

    public static IReadOnlyList<TutorialDefinition> All { get; } =
    [
        new(Dashboard,
        [
            new("Welcome"),
            new("Path", "[data-tour=path]"),
            new("Progress", "[data-tour=progress]"),
            // Below the lg breakpoint the navigation links collapse into the menu button.
            new("Practice", "[data-tour=nav-exercises], .navbar-toggler"),
            new("Training", "[data-tour=nav-training], .navbar-toggler"),
            new("Replay", ReplayButton),
        ]),
        new(Exercise,
        [
            new("Listen", ".aa-audio-buttons"),
            // Answer buttons, the piano keyboard, the staff editor, or a page's own controls.
            new("Answer", ".aa-answer-toolbar, .aa-answers-chromatic, .aa-answer-grid, #staffEditor, [data-tour=answer]"),
            new("Check", "#validateGuess"),
            // The counters and the filters button; the replay button has its own step.
            new("Score", "[data-tour=score]"),
            new("Free", "[data-tour=free-practice]"),
            new("Instructions", "[data-tour=instructions]"),
            new("Replay", ReplayButton),
        ]),
    ];

    /// <summary>Finds a tour by key, ignoring case; <paramref name="tutorial"/> holds the catalog's own key.</summary>
    public static bool TryGet(string? key, [NotNullWhen(true)] out TutorialDefinition? tutorial)
    {
        tutorial = All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        return tutorial is not null;
    }
}
