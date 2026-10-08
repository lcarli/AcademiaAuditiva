using System.Net;
using AcademiaAuditiva.Models.Teaching;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// My Training shows each routine by its own answers and links the questions still to take;
/// the exercise page opened from it shows the routine instead of free practice and plays the
/// teacher's filters.
/// </summary>
public class RoutinePagesTests : IClassFixture<RoutineWebApplicationFactory>
{
    private const string UserId = SignedInWebApplicationFactory.UserId;
    private const string FilterLocked = "Set by your teacher for this routine.";

    private static readonly DateTime LastWeek = new(2026, 1, 8);

    private readonly RoutineWebApplicationFactory _factory;

    public RoutinePagesTests(RoutineWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task MyTraining_ShowsEachRoutineByItsOwnAnswers_AndLinksTheQuestionsLeft()
    {
        var classroom = _factory.ClassroomOfTheStudent();
        var started = await _factory.AssignAsync(
            new RoutineAssignment { Classroom = classroom, DueAt = new DateTime(2026, 2, 1) },
            _factory.Item("GuessNote", target: 2, minScore: 60),
            _factory.Item("GuessInterval", target: 3, filterJson: """{"keySelect":"D4"}""", minScore: 60));
        await _factory.RecordAnswersAsync(started, item: 0, true, false);
        await _factory.RecordAnswersAsync(started, item: 1, true);
        await _factory.PracticeAsync("GuessInterval", answers: 5);
        var finished = await _factory.AssignAsync(_factory.Item("GuessNote", target: 1));
        await _factory.RecordAnswersAsync(finished, item: 0, true);
        var closed = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = UserId, DueAt = LastWeek }, _factory.Item("GuessNote"));
        var late = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = UserId, DueAt = LastWeek, AllowLate = true }, _factory.Item("GuessNote"));

        var html = await PageAsync("/MyTraining");

        var block = Block(html, started);
        block.Should().Contain($"Classroom: {classroom.Name}")
            .And.Contain("aria-valuenow=\"66\"", "the items are 100% and 33% done")
            .And.NotContain("aa-routine-finished").And.NotContain("aa-routine-closed").And.NotContain("aa-routine-late");
        block.Should().Contain("Done: 1 of 2 correct (50%)").And.Contain("Below the minimum of 60%")
            .And.NotContain($"routineItemId={started.ItemIds[0]}\"", "a complete exercise takes no more answers");
        block.Should().Contain($"href=\"/Exercise/GuessInterval?keySelect=D4&amp;{Html(started.Query(1))}\"")
            .And.Contain("1 of 3 answered", "practice outside the routine does not count")
            .And.Contain("Minimum accuracy: 60%");

        block = Block(html, finished);
        block.Should().Contain("Personal")
            .And.Contain("<span class=\"aa-badge-soft aa-routine-finished\">Finished</span>")
            .And.Contain("aria-valuenow=\"100\"")
            .And.Contain("Done: 1 of 1 correct (100%)")
            .And.NotContain("href=\"/Exercise/");

        block = Block(html, closed);
        block.Should().Contain("<span class=\"aa-badge-soft aa-routine-closed\">Closed</span>")
            .And.Contain("0 of 2 answered")
            .And.NotContain("href=\"/Exercise/", "a closed routine takes no answers");

        block = Block(html, late);
        block.Should().Contain("<span class=\"aa-badge-soft aa-routine-late\">Late answers accepted</span>")
            .And.Contain($"href=\"/Exercise/GuessNote?{Html(late.Query())}\"");
    }

    [Fact]
    public async Task ExercisePage_ForARoutineQuestion_ShowsTheRoutine_AndLocksTheTeachersFilters()
    {
        var classroom = _factory.ClassroomOfTheStudent();
        var routine = await _factory.AssignAsync(
            new RoutineAssignment { Classroom = classroom },
            _factory.Item("GuessInterval", target: 3, filterJson: """{"keySelect":"D4"}""", minScore: 60));
        await _factory.RecordAnswersAsync(routine, item: 0, true);

        // The query's filters are ignored: the routine sets them.
        var html = await PageAsync($"/Exercise/GuessInterval?keySelect=C4&{routine.Query()}");

        html.Should().Contain("<section class=\"aa-preset aa-routine\" id=\"aaRoutine\"")
            .And.Contain($"data-assignment-id=\"{routine.AssignmentId}\" data-item-id=\"{routine.ItemId}\"")
            .And.Contain("data-state=\"open\"")
            .And.Contain("data-blocked-title=\"\" data-blocked-message=\"\"", "nothing stops Play")
            .And.Contain($"<strong id=\"aaRoutineName\">{routine.Name}</strong>")
            .And.Contain($"Classroom: {classroom.Name}")
            .And.Contain(">Question 2 of 3</span>")
            .And.Contain("Minimum accuracy: 60%")
            .And.Contain("aria-valuenow=\"33\"");
        html.Should().NotContain("id=\"aaFreePractice\"", "a routine question is never free practice")
            .And.NotContain("id=\"aaFreeNote\"");

        html.Should().MatchRegex("<select id=\"keySelect\" name=\"keySelect\" class=\"form-select\" disabled=\"disabled\"\\s+aria-describedby=\"keySelectLocked\">")
            .And.Contain("<option value=\"D4\" selected=\"selected\">")
            .And.Contain("id=\"keySelectLocked\"")
            .And.Contain(FilterLocked)
            .And.Contain("<select id=\"scaleTypeSelect\" name=\"scaleTypeSelect\" class=\"form-select\">", "the student picks the other filters")
            .And.NotContain("id=\"scaleTypeSelectLocked\"");
    }

    [Fact]
    public async Task ExercisePage_ForARoutineThatTakesNoAnswers_SaysWhy()
    {
        var closed = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = UserId, DueAt = LastWeek }, _factory.Item("GuessNote"));
        var someoneElses = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = "another-student" }, _factory.Item("GuessNote"));
        var ofAnotherExercise = await _factory.AssignAsync(_factory.Item("GuessInterval"));

        var html = await PageAsync($"/Exercise/GuessNote?{closed.Query()}");
        html.Should().Contain("data-state=\"closed\"")
            .And.Contain("data-blocked-title=\"Routine closed\"")
            .And.Contain("data-blocked-message=\"The due date has passed: this routine no longer takes answers.\"")
            .And.NotContain("id=\"aaFreePractice\"");

        foreach (var routine in new[] { someoneElses, ofAnotherExercise })
        {
            html = await PageAsync($"/Exercise/GuessNote?{routine.Query()}");

            html.Should().Contain("data-state=\"missing\"")
                .And.Contain("data-blocked-title=\"Routine unavailable\"")
                .And.Contain("<strong id=\"aaRoutineName\">Routine unavailable</strong>")
                .And.NotContain(routine.Name)
                .And.NotContain("id=\"aaFreePractice\"");
        }
    }

    [Fact]
    public async Task ExercisePage_WithoutBothRoutineIds_IsOrdinaryPractice()
    {
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote"));

        var html = await PageAsync($"/Exercise/GuessNote?routineAssignmentId={routine.AssignmentId}");

        html.Should().NotContain("id=\"aaRoutine\"").And.Contain("id=\"aaFreePractice\"");
    }

    // A routine's part of My Training, from its anchor (which a notification about it opens) to the next routine's.
    private static string Block(string html, SeededRoutine routine)
    {
        var anchor = $" id=\"routine-{routine.AssignmentId}\"";
        var start = html.IndexOf(anchor, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "My Training lists {0} under its anchor", routine.Name);
        var end = html.IndexOf(" id=\"routine-", start + anchor.Length, StringComparison.Ordinal);
        var block = end < 0 ? html[start..] : html[start..end];
        block.Should().Contain($"<strong class=\"text-body-emphasis\">{routine.Name}</strong>");
        return block;
    }

    private static string Html(string query) => query.Replace("&", "&amp;", StringComparison.Ordinal);

    private async Task<string> PageAsync(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }
}
