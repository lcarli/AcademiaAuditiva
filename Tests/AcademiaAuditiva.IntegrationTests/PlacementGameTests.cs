using System.Net;
using System.Text.Json;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Games;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The placement test: started from the games hub, played on the exercise pages of the learning
/// path's first units, and applied to the learning path.
/// </summary>
public class PlacementGameTests : IClassFixture<GameModesTests.Factory>
{
    private readonly GameModesTests.Factory _factory;

    public PlacementGameTests(GameModesTests.Factory factory) => _factory = factory;

    [Fact]
    public async Task PlacementTest_SuggestsAUnit_AndUnlocksThePathUpToIt()
    {
        var player = await GamePlayer.CreateAsync(_factory);

        var start = await player.Client.PostAsync("/Games/StartPlacement", null);
        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var runId = player.Db(db => db.GameRuns.Single(r => r.UserId == GamePlayer.UserId).Id);
        var location = start.Headers.Location!.OriginalString;
        location.Should().StartWith("/Exercise/GuessInterval?").And.Contain("game=placement").And.EndWith($"run={runId}");

        var page = WebUtility.HtmlDecode(await player.Client.GetStringAsync(location));
        page.Should().Contain("data-mode=\"placement\"").And.Contain($"data-run=\"{runId}\"")
            .And.Contain("Exercise 1 of 6, question 1 of 3: ").And.NotContain("data-aa-game-stop");
        (await player.Client.GetStringAsync($"/Exercise/GuessChords?game=placement&run={runId}")).Should()
            .Contain("id=\"aaGame\"").And.Contain("data-run=\"\"").And.Contain("The test has moved on");

        var moved = await player.PlayAsync("GuessChords", runId);
        moved.GetProperty("success").GetBoolean().Should().BeFalse();
        moved.GetProperty("message").GetString().Should().Be("This game continues on another exercise.");
        moved.GetProperty("game").GetProperty("nextUrl").GetString().Should().StartWith("/Exercise/GuessInterval?");

        JsonElement game = default;
        for (var i = 0; i < 3; i++) game = await player.PlayAndAnswerAsync("GuessInterval", runId, correct: true);
        game.GetProperty("nextUrl").GetString().Should().StartWith("/Exercise/GuessChords?").And.Contain($"run={runId}");
        game.GetProperty("text").GetString().Should().StartWith("Exercise 2 of 6, question 1 of 3: ");
        for (var i = 0; i < 3; i++) game = await player.PlayAndAnswerAsync("GuessChords", runId, correct: true);
        game = await player.PlayAndAnswerAsync("GuessDegree", runId, correct: true);
        game.GetProperty("nextUrl").GetString().Should().StartWith("/Exercise/GuessQuality?", "7 right answers pass the unit");

        game = await player.PlayAndAnswerAsync("GuessQuality", runId, correct: false);
        game = await player.PlayAndAnswerAsync("GuessQuality", runId, correct: false);
        game.GetProperty("over").GetBoolean().Should().BeFalse();
        game = await player.PlayAndAnswerAsync("GuessQuality", runId, correct: false);

        game.GetProperty("over").GetBoolean().Should().BeTrue("3 wrong answers fail the unit");
        game.GetProperty("text").GetString().Should().Be("Placement test finished.");
        game.GetProperty("nextUrl").ValueKind.Should().Be(JsonValueKind.Null);
        game.GetProperty("resultUrl").GetString().Should().Be($"/Games/Placement/{runId}");
        game.GetProperty("end").GetProperty("title").GetString().Should().Be("Placement test finished");
        var run = player.Db(db => db.GameRuns.Single(r => r.Id == runId));
        run.Should().BeEquivalentTo(new { PlacementUnit = 2, Score = 7, Answered = 10, AppliedAt = (DateTime?)null });
        run.EndedAt.Should().NotBeNull();
        (await player.PlayAsync("GuessQuality", runId)).GetProperty("message").GetString().Should().Be("This game is over.");

        var result = WebUtility.HtmlDecode(await player.Client.GetStringAsync($"/Games/Placement/{runId}"));
        result.Should().Contain("Start at unit 2: Building blocks").And.Contain("7 right out of 7").And.Contain("0 right out of 3");
        result.Should().Contain("aa-placement-unit is-passed").And.Contain("aa-placement-unit is-failed");

        var apply = await player.Client.PostAsync($"/Games/ApplyPlacement/{runId}", null);
        apply.StatusCode.Should().Be(HttpStatusCode.Redirect);
        apply.Headers.Location!.OriginalString.Should().Be("/LearningPath");
        var path = WebUtility.HtmlDecode(await player.Client.GetStringAsync("/LearningPath"));
        path.Should().Contain("Learning path updated from your placement test.").And.Contain("7 of 26 steps");
        path.Split(">Placed out<").Should().HaveCount(8, "every step of unit 1 is placed out");
        player.Db(db => db.GameRuns.Single(r => r.Id == runId).AppliedAt).Should().NotBeNull();

        var hub = WebUtility.HtmlDecode(await player.Client.GetStringAsync("/Games"));
        hub.Should().Contain("Last result: start at unit 2, Building blocks.").And.Contain("Start over")
            .And.NotContain("Continue the test");
    }

    [Fact]
    public async Task UnfinishedTest_IsContinuedFromTheHub_OrStartedOver()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        await player.Client.PostAsync("/Games/StartPlacement", null);
        var first = player.Db(db => db.GameRuns.Single(r => r.UserId == GamePlayer.UserId).Id);
        await player.PlayAndAnswerAsync("GuessInterval", first, correct: true);

        var hub = WebUtility.HtmlDecode(await player.Client.GetStringAsync("/Games"));
        hub.Should().Contain("Continue the test").And.Contain("Start over")
            .And.MatchRegex($@"href=""/Exercise/GuessInterval\?[^""]*game=placement&run={first}""");
        var progress = WebUtility.HtmlDecode(await player.Client.GetStringAsync($"/Games/Placement/{first}"));
        progress.Should().Contain("Test in progress").And.Contain("1 right out of 1");

        var restart = await player.Client.PostAsync("/Games/StartPlacement", null);
        var second = player.Db(db => db.GameRuns.Where(r => r.UserId == GamePlayer.UserId).Max(r => r.Id));
        second.Should().NotBe(first);
        restart.Headers.Location!.OriginalString.Should().EndWith($"run={second}");
        player.Db(db => db.GameRuns.Single(r => r.Id == first).EndedAt).Should().NotBeNull("starting over ends the unfinished test");
        var stale = await player.PlayAsync("GuessInterval", first);
        stale.GetProperty("message").GetString().Should().Be("This game is over.");
        WebUtility.HtmlDecode(await player.Client.GetStringAsync($"/Games/Placement/{first}"))
            .Should().Contain("This test was not finished");
        (await player.PostFinishAsync(second)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a placement test ends by itself");
    }

    [Fact]
    public async Task PlacementPages_AreOnlyThePlayers()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var (rivals, sprint) = player.Db(db =>
        {
            var now = _factory.Clock.GetUtcNow().UtcDateTime;
            var rival = new GameRun { UserId = GamePlayer.RivalId, Mode = GameModes.Placement, StartedAt = now, EndedAt = now, PlacementUnit = 3 };
            var own = new GameRun { UserId = GamePlayer.UserId, Mode = GameModes.Sprint, ExerciseId = player.Id("GuessNote"), StartedAt = now };
            db.GameRuns.AddRange(rival, own);
            db.SaveChanges();
            return (rival.Id, own.Id);
        });

        (await player.Client.GetAsync($"/Games/Placement/{rivals}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await player.Client.GetAsync($"/Games/Placement/{sprint}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await player.Client.PostAsync($"/Games/ApplyPlacement/{rivals}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await player.Client.PostAsync($"/Games/ApplyPlacement/{sprint}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await player.Client.GetStringAsync($"/Exercise/GuessInterval?game=placement&run={rivals}"))
            .Should().Contain("data-run=\"\"").And.Contain("The test has moved on");
        player.Db(db => db.GameRuns.Single(r => r.Id == rivals).AppliedAt).Should().BeNull();
    }
}
