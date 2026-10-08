using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services.Gamification;
using AcademiaAuditiva.Services.Routines;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// A routine is taken like a test: Play asks each item's questions with the teacher's filters,
/// one at a time and each until it is answered, every answer counts, right or wrong, and none is
/// taken past the item's target. Only the answers given in the routine count towards it.
/// </summary>
public class RoutineRoundTests : IClassFixture<RoutineWebApplicationFactory>
{
    private const string UserId = SignedInWebApplicationFactory.UserId;
    private const string SessionExpired = "Session expired or no answer found.";
    private const string AlreadyAnsweredTitle = "Question already answered";
    private const string AlreadyAnswered = "This question was already answered, maybe in another tab.";
    private const string ItemDone = "You answered every question of this exercise. Go back to My Training for the rest of the routine.";
    private const string Unavailable = "This routine is no longer assigned to you.";
    private const string ClosedMessage = "The due date has passed: this routine no longer takes answers.";

    private static readonly Uri BaseAddress = new("http://localhost");

    private readonly RoutineWebApplicationFactory _factory;

    public RoutineRoundTests(RoutineWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task RoutineQuestions_AreEachAnsweredOnce_UpToTheTeachersTarget()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 2, minScore: 60));
        var client = await ClientAsync();

        var first = await PlayAsync(client, exerciseId, routine);
        first.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "playToken", "routine");
        StatusOf(first).Should().Be(new Status("open", "Question 1 of 2", 0, 0, 2, 0));
        var right = await AnswerAsync(client, exerciseId, RoundId(first), correct: true);
        right.GetProperty("success").GetBoolean().Should().BeTrue();
        right.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        StatusOf(right).Should().Be(new Status("open", "Question 2 of 2", 1, 1, 2, 50));

        var second = await PlayAsync(client, exerciseId, routine);
        RoundId(second).Should().NotBe(RoundId(first));
        StatusOf(second).Should().Be(new Status("open", "Question 2 of 2", 1, 1, 2, 50));
        var wrong = await AnswerAsync(client, exerciseId, RoundId(second), correct: false);
        wrong.GetProperty("success").GetBoolean().Should().BeTrue();
        StatusOf(wrong).Should().Be(new Status("done", "Done: 1 of 2 correct (50%)", 2, 1, 2, 100));
        wrong.GetProperty("routine").GetProperty("verdict").GetString().Should().Be("Below the minimum of 60%");
        wrong.GetProperty("routine").GetProperty("passed").GetBoolean().Should().BeFalse();

        var mixed = _factory.Mixer.Plans.Count;
        var more = await PlayAsync(client, exerciseId, routine);
        more.GetProperty("success").GetBoolean().Should().BeFalse();
        more.GetProperty("message").GetString().Should().Be(ItemDone);
        more.GetProperty("routine").GetProperty("blocked").GetProperty("title").GetString().Should().Be("Exercise complete");
        StatusOf(more).Should().Be(StatusOf(wrong));
        _factory.Mixer.Plans.Count.Should().Be(mixed, "no question past the target is played");

        (await _factory.AnswersAsync(routine.AssignmentId)).Select(s => (s.RoutineItemId, s.RoutineQuestion, s.IsCorrect))
            .Should().Equal((routine.ItemId, 1, true), (routine.ItemId, 2, false));
    }

    [Fact]
    public async Task PracticeOutsideTheRoutine_DoesNotCountTowardsIt()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 1));
        var client = await ClientAsync();

        var scored = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));
        scored.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "playToken");
        var answer = await AnswerAsync(client, exerciseId, RoundId(scored), correct: true);
        answer.GetProperty("success").GetBoolean().Should().BeTrue();
        answer.GetProperty("routine").ValueKind.Should().Be(JsonValueKind.Null);

        var free = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true }));
        (await AnswerAsync(client, exerciseId, RoundId(free), correct: true)).GetProperty("free").GetBoolean().Should().BeTrue();

        (await _factory.AnswersAsync(routine.AssignmentId)).Should().BeEmpty();
        StatusOf(await PlayAsync(client, exerciseId, routine)).Should().Be(new Status("open", "Question 1 of 1", 0, 0, 1, 0));
    }

    [Fact]
    public async Task Play_AsksTheSameQuestionAgain_UntilItIsAnswered()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 2));
        var client = await ClientAsync();
        var mixed = _factory.Mixer.Plans.Count;

        var first = await PlayAsync(client, exerciseId, routine);
        await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true }));
        var again = await PlayAsync(client, exerciseId, routine);

        RoundId(again).Should().Be(RoundId(first));
        again.GetProperty("playToken").GetString().Should().Be(first.GetProperty("playToken").GetString());
        StatusOf(again).Should().Be(new Status("open", "Question 1 of 2", 0, 0, 2, 0));
        _factory.Mixer.Plans.Count.Should().Be(mixed + 2, "the question asked again is not mixed again");

        await AnswerAsync(client, exerciseId, RoundId(first), correct: false);
        var next = await PlayAsync(client, exerciseId, routine);

        RoundId(next).Should().NotBe(RoundId(first));
        StatusOf(next).Should().Be(new Status("open", "Question 2 of 2", 1, 0, 2, 50));
    }

    [Fact]
    public async Task SheetMusicQuestion_IsAskedAgain_EvenAfterFreePracticeOfTheSameExercise()
    {
        var exerciseId = _factory.ExerciseId("SolfegeMelody");
        var routine = await _factory.AssignAsync(_factory.Item("SolfegeMelody", target: 2));
        var client = await ClientAsync();

        var first = await PlayAsync(client, exerciseId, routine);
        StatusOf(first).Should().Be(new Status("open", "Question 1 of 2", 0, 0, 2, 0));
        var free = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, free = true }));
        free.TryGetProperty("routine", out _).Should().BeFalse();
        var again = await PlayAsync(client, exerciseId, routine);
        Melody(again).Should().Equal(Melody(first));
        // Its starting note comes again too, under a new token: the first one may have expired.
        _factory.Mixer.Plans.Last().Should().Equal(new MixInput(Melody(first)[0].Replace("#", "s") + ".mp3", 0, 1.5));
        (await _factory.Tokens.ResolveTokenAsync(UserId, again.GetProperty("startingNoteToken").GetString()!))
            .Should().Be(ExploreWebApplicationFactory.Clip);

        // Free practice replaced the exercise's session; asking the question again restored it.
        var validation = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, userGuess = string.Join("|", Melody(first)) }));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
        StatusOf(validation).Should().Be(new Status("open", "Question 2 of 2", 1, 1, 2, 50));
        (await _factory.AnswersAsync(routine.AssignmentId)).Select(s => (s.RoutineItemId, s.RoutineQuestion, s.IsCorrect))
            .Should().Equal((routine.ItemId, 1, true));
    }

    [Fact]
    public async Task QuestionAnsweredInAnotherWindow_IsNotAnsweredAgain()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 3));
        var client = await ClientAsync();
        var play = await PlayAsync(client, exerciseId, routine);
        var otherWindow = await SameQuestionAsync(exerciseId, routine, RoundId(play), question: 1);

        (await AnswerAsync(client, exerciseId, RoundId(play), correct: true)).GetProperty("success").GetBoolean().Should().BeTrue();
        var late = await AnswerAsync(client, exerciseId, otherWindow, correct: true);

        late.GetProperty("success").GetBoolean().Should().BeFalse();
        late.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        late.GetProperty("title").GetString().Should().Be(AlreadyAnsweredTitle);
        late.GetProperty("message").GetString().Should().Be(AlreadyAnswered);
        StatusOf(late).Should().Be(new Status("open", "Question 2 of 3", 1, 1, 3, 33));
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().ContainSingle();

        var replay = await AnswerAgainAsync(client, exerciseId, otherWindow);
        replay.GetProperty("title").GetString().Should().Be(AlreadyAnsweredTitle, "the refused round still asked an answered question");
        StatusOf(replay).Should().Be(new Status("open", "Question 2 of 3", 1, 1, 3, 33));
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().ContainSingle();
    }

    [Fact]
    public async Task QuestionPlayedTwice_IsRefused_OnceTheExerciseIsComplete()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 1));
        var client = await ClientAsync();
        var play = await PlayAsync(client, exerciseId, routine);
        var otherWindow = await SameQuestionAsync(exerciseId, routine, RoundId(play), question: 1);

        await AnswerAsync(client, exerciseId, RoundId(play), correct: true);
        var late = await AnswerAsync(client, exerciseId, otherWindow, correct: false);

        late.GetProperty("success").GetBoolean().Should().BeFalse();
        late.GetProperty("message").GetString().Should().Be(ItemDone);
        StatusOf(late).Should().Be(new Status("done", "Done: 1 of 1 correct (100%)", 1, 1, 1, 100));
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().ContainSingle().Which.IsCorrect.Should().BeTrue();
    }

    // Play asks a pending question again, so two windows usually show the very same round.
    [Fact]
    public async Task RoundShownInTwoWindows_IsAnsweredOnce()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 3));
        var client = await ClientAsync();
        var play = await PlayAsync(client, exerciseId, routine);
        var otherWindow = await PlayAsync(client, exerciseId, routine);
        RoundId(otherWindow).Should().Be(RoundId(play));

        (await AnswerAsync(client, exerciseId, RoundId(play), correct: true)).GetProperty("success").GetBoolean().Should().BeTrue();
        var late = await AnswerAgainAsync(client, exerciseId, RoundId(otherWindow));

        late.GetProperty("success").GetBoolean().Should().BeFalse();
        late.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
        late.GetProperty("title").GetString().Should().Be(AlreadyAnsweredTitle);
        late.GetProperty("message").GetString().Should().Be(AlreadyAnswered);
        StatusOf(late).Should().Be(new Status("open", "Question 2 of 3", 1, 1, 3, 33));
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().ContainSingle();
    }

    [Fact]
    public async Task RoundShownInTwoWindows_IsRefused_OnceTheExerciseIsComplete()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 1));
        var client = await ClientAsync();
        var play = await PlayAsync(client, exerciseId, routine);
        var otherWindow = await PlayAsync(client, exerciseId, routine);

        await AnswerAsync(client, exerciseId, RoundId(play), correct: true);
        var late = await AnswerAgainAsync(client, exerciseId, RoundId(otherWindow));

        late.GetProperty("success").GetBoolean().Should().BeFalse();
        late.GetProperty("message").GetString().Should().Be(ItemDone);
        StatusOf(late).Should().Be(new Status("done", "Done: 1 of 1 correct (100%)", 1, 1, 1, 100));
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().ContainSingle().Which.IsCorrect.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"exerciseId":1,"routineAssignmentId":1,"routineItemId":1,"free":true}""")]
    [InlineData("""{"exerciseId":1,"routineAssignmentId":1}""")]
    [InlineData("""{"exerciseId":1,"routineItemId":1}""")]
    [InlineData("""{"exerciseId":1,"routineAssignmentId":0,"routineItemId":1}""")]
    [InlineData("""{"exerciseId":1,"routineAssignmentId":1,"routineItemId":-1}""")]
    public async Task RequestPlay_RefusesARoutineQuestionWithoutBothIds_OrInFreePractice(string body)
    {
        var client = await ClientAsync();

        var response = await client.PostAsync("/Exercise/RequestPlay", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RoutineQuestion_IsUnavailable_UnlessTheItemIsTheStudents()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var someoneElses = await _factory.AssignAsync(new RoutineAssignment { StudentId = "another-student" }, _factory.Item("GuessNote"));
        var archived = await _factory.AssignAsync(
            new RoutineAssignment { Classroom = _factory.ClassroomOfTheStudent(archived: true) }, _factory.Item("GuessNote"));
        var ofAnotherExercise = await _factory.AssignAsync(_factory.Item("GuessInterval"));
        var excluded = await _factory.AssignAsync(_factory.Item("GuessNote"), _factory.Item("GuessNote"));
        await _factory.ExcludeAsync(excluded, item: 0);
        var ofTheClassroom = await _factory.AssignAsync(
            new RoutineAssignment { Classroom = _factory.ClassroomOfTheStudent() }, _factory.Item("GuessNote"));
        var client = await ClientAsync();
        var mixed = _factory.Mixer.Plans.Count;

        foreach (var routine in new[] { someoneElses, archived, ofAnotherExercise, excluded })
        {
            var play = await PlayAsync(client, exerciseId, routine);

            play.GetProperty("success").GetBoolean().Should().BeFalse();
            play.GetProperty("message").GetString().Should().Be(Unavailable);
            StatusOf(play).Should().Be(new Status("missing", Unavailable, 0, 0, 0, 0));
            play.GetProperty("routine").GetProperty("blocked").GetProperty("title").GetString().Should().Be("Routine unavailable");
        }
        _factory.Mixer.Plans.Count.Should().Be(mixed);

        StatusOf(await PlayAsync(client, exerciseId, excluded, item: 1)).State.Should().Be("open");
        StatusOf(await PlayAsync(client, exerciseId, ofTheClassroom)).State.Should().Be("open");
    }

    [Fact]
    public async Task RoutinePastItsDueDate_TakesNoMoreAnswers_UnlessTheTeacherAcceptsLateOnes()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var dueDate = new DateTime(2026, 1, 14);
        var closes = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = UserId, DueAt = dueDate }, _factory.Item("GuessNote"));
        var acceptsLate = await _factory.AssignAsync(
            new RoutineAssignment { StudentId = UserId, DueAt = dueDate, AllowLate = true }, _factory.Item("GuessNote"));
        var inUtc = await ClientAsync();
        var inToronto = await ClientAsync(RoutineWebApplicationFactory.Toronto);

        // The 14th is over in UTC, but not in Toronto yet.
        var closed = await PlayAsync(inUtc, exerciseId, closes);
        closed.GetProperty("success").GetBoolean().Should().BeFalse();
        closed.GetProperty("message").GetString().Should().Be(ClosedMessage);
        StatusOf(closed).Should().Be(new Status("closed", ClosedMessage, 0, 0, 2, 0));
        closed.GetProperty("routine").GetProperty("blocked").GetProperty("title").GetString().Should().Be("Routine closed");
        StatusOf(await PlayAsync(inToronto, exerciseId, closes)).State.Should().Be("open");

        var late = await PlayAsync(inUtc, exerciseId, acceptsLate);
        StatusOf(late).Should().Be(new Status("late", "Question 1 of 2", 0, 0, 2, 0));
        var answer = await AnswerAsync(inUtc, exerciseId, RoundId(late), correct: true);
        StatusOf(answer).Should().Be(new Status("late", "Question 2 of 2", 1, 1, 2, 50));
        (await _factory.AnswersAsync(acceptsLate.AssignmentId)).Should().ContainSingle();
    }

    [Fact]
    public async Task RoutineQuestion_IsPlayedWithTheTeachersFilters_AndTheStudentPicksTheOthers()
    {
        var exerciseId = _factory.ExerciseId("GuessInterval");
        var routine = await _factory.AssignAsync(_factory.Item("GuessInterval", filterJson: """{"keySelect":"D4"}"""));
        var client = await ClientAsync();

        var play = await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId,
            routineAssignmentId = routine.AssignmentId,
            routineItemId = routine.ItemId,
            filters = new Dictionary<string, string> { ["keySelect"] = "C4", ["scaleTypeSelect"] = "minor" }
        }));

        var round = await _factory.Tokens.GetRoundAsync(UserId, exerciseId, RoundId(play));
        round!.FilterJson.Should().Be("""{"keySelect":"D4","scaleTypeSelect":"minor"}""");
    }

    [Fact]
    public async Task AnswerToARoutineNoLongerAssigned_IsOrdinaryPractice()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote"));
        var client = await ClientAsync();
        var play = await PlayAsync(client, exerciseId, routine);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RoutineAssignments.Remove(await db.RoutineAssignments.SingleAsync(a => a.Id == routine.AssignmentId));
            await db.SaveChangesAsync();
        }
        var practice = await UntaggedAnswersAsync();

        var answer = await AnswerAsync(client, exerciseId, RoundId(play), correct: true);

        answer.GetProperty("success").GetBoolean().Should().BeTrue();
        answer.GetProperty("routine").ValueKind.Should().Be(JsonValueKind.Null);
        (await _factory.AnswersAsync(routine.AssignmentId)).Should().BeEmpty();
        (await UntaggedAnswersAsync()).Should().Be(practice + 1);
        (await AnswerAgainAsync(client, exerciseId, RoundId(play))).GetProperty("message").GetString()
            .Should().Be(SessionExpired, "the round is used up and its routine gone");
    }

    [Fact]
    public async Task AnsweringTheRoutinesLastQuestion_TellsTheTeacher()
    {
        var exerciseId = _factory.ExerciseId("GuessNote");
        var routine = await _factory.AssignAsync(_factory.Item("GuessNote", target: 1), _factory.Item("GuessNote", target: 1));
        var client = await ClientAsync();

        var first = await AnswerAsync(client, exerciseId, RoundId(await PlayAsync(client, exerciseId, routine)), correct: false);
        StatusOf(first).State.Should().Be("done");
        (await NotificationsAsync(routine.AssignmentId)).Should().BeEmpty("the routine's other exercise is left");

        var last = await AnswerAsync(client, exerciseId, RoundId(await PlayAsync(client, exerciseId, routine, item: 1)), correct: true);

        StatusOf(last).State.Should().Be("done");
        (await NotificationsAsync(routine.AssignmentId)).Should().Equal(
            (RoutineWebApplicationFactory.TeacherId, NotificationKind.StudentFinishedRoutine, UserId, RoutineWebApplicationFactory.Now.UtcDateTime));
    }

    private sealed record Status(string State, string Text, int Answered, int Correct, int Target, int Percent);

    private static Status StatusOf(JsonElement response)
    {
        var routine = response.GetProperty("routine");
        return new Status(
            routine.GetProperty("state").GetString()!,
            routine.GetProperty("text").GetString()!,
            routine.GetProperty("answered").GetInt32(),
            routine.GetProperty("correct").GetInt32(),
            routine.GetProperty("target").GetInt32(),
            routine.GetProperty("percent").GetInt32());
    }

    private static string RoundId(JsonElement play) => play.GetProperty("roundId").GetString()!;

    private static string[] Melody(JsonElement play) => play.GetProperty("melody").EnumerateArray()
        .Where(item => item.GetProperty("type").GetString() == "note")
        .Select(item => item.GetProperty("note").GetString()!)
        .ToArray();

    private static async Task<JsonElement> PlayAsync(HttpClient client, int exerciseId, SeededRoutine routine, int item = 0)
        => await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new
        {
            exerciseId,
            routineAssignmentId = routine.AssignmentId,
            routineItemId = routine.ItemIds[item]
        }));

    private async Task<JsonElement> AnswerAsync(HttpClient client, int exerciseId, string roundId, bool correct)
    {
        var round = await _factory.Tokens.GetRoundAsync(UserId, exerciseId, roundId);
        round.Should().NotBeNull();
        var note = (string)JObject.Parse(round!.ExpectedAnswerJson)["note"]!;
        var guess = correct ? note : note.StartsWith('C') ? "D" : "C";
        return await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", new { exerciseId, roundId, userGuess = guess }));
    }

    // A window still showing a round the test can no longer read (it was answered) answers it.
    private static async Task<JsonElement> AnswerAgainAsync(HttpClient client, int exerciseId, string roundId)
        => await ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise", new { exerciseId, roundId, userGuess = "C" }));

    // What a second window asking the same question would have got.
    private async Task<string> SameQuestionAsync(int exerciseId, SeededRoutine routine, string roundId, int question)
    {
        var round = await _factory.Tokens.GetRoundAsync(UserId, exerciseId, roundId);
        var other = await _factory.Tokens.CreateRoundAsync(UserId, exerciseId, round!.ExpectedAnswerJson,
            [ExploreWebApplicationFactory.Clip], routine: new RoutineQuestion(routine.Link(), question));
        return other.RoundId;
    }

    private async Task<int> UntaggedAnswersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ScoreSnapshots.CountAsync(s => s.UserId == UserId && s.RoutineAssignmentId == null);
    }

    private async Task<List<(string UserId, NotificationKind Kind, string StudentId, DateTime CreatedAt)>> NotificationsAsync(int assignmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.RoutineAssignmentId == assignmentId)
            .OrderBy(n => n.Id)
            .ToListAsync();
        return notifications.Select(n => (n.UserId, n.Kind, n.StudentId, n.CreatedAt)).ToList();
    }

    private Task<HttpClient> ClientAsync(string? timeZone = null)
    {
        var cookies = new CookieContainer();
        if (timeZone is not null)
        {
            cookies.Add(BaseAddress, new Cookie(UserTimeZone.CookieName, Uri.EscapeDataString(timeZone)));
        }
        return IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies)));
    }

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) => IntegrationHttp.ReadJsonAsync(response);
}
