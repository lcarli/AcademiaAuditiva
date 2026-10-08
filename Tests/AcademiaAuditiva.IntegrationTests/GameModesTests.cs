using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Games;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The games hub, and the sprint, sudden death and weak spot games played on an exercise page:
/// the page's first Play starts a run, every round of it is asked with the run's settings and
/// scored as usual, and the server counts the run's score from those answers.
/// </summary>
public class GameModesTests : IClassFixture<GameModesTests.Factory>
{
    private readonly Factory _factory;

    public GameModesTests(Factory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
    }

    [Theory]
    [InlineData("en-US", "Games", "60-second sprint", "Sudden death", "Weak spots", "Placement test", "Start the test")]
    [InlineData("pt-BR", "Jogos", "Sprint de 60 s", "Morte súbita", "Pontos fracos", "Teste de nivelamento", "Começar o teste")]
    [InlineData("fr-CA", "Jeux", "Sprint de 60\u00A0s", "Mort subite", "Points faibles", "Test de classement", "Commencer le test")]
    public async Task Hub_OffersEveryMode_InTheStudentsLanguage(
        string culture, string title, string sprint, string survival, string weakSpots, string placement, string start)
    {
        var player = await GamePlayer.CreateAsync(_factory);

        var html = await player.Client.GetStringAsync($"/Games?culture={culture}");

        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain($">{title}</h1>").And.Contain(sprint).And.Contain(survival)
            .And.Contain(weakSpots).And.Contain(placement).And.Contain(start);
        html.Should().Contain("data-game-mode=\"sprint\"").And.Contain("data-game-mode=\"survival\"")
            .And.Contain("data-game-mode=\"weakspots\"").And.Contain("data-game-mode=\"placement\"");
        html.Should().Contain("<option value=\"GuessNote\">").And.NotContain("<option value=\"SingNote\">",
            "a sung exercise can't be played as a game");
        text.Should().NotMatchRegex(@"\bGames?\.[A-Z]\w+", "every text has a resource");
    }

    [Theory]
    [InlineData("survival", "GuessNote", "/Exercise/GuessNote?game=survival")]
    [InlineData("Sprint", "HigherOrLower", "/Exercise/HigherOrLower?game=sprint")]
    [InlineData("survival", "SingNote", "/Games")]
    [InlineData("placement", "GuessNote", "/Games")]
    [InlineData("sprint", "NoSuchExercise", "/Games")]
    public async Task HubForm_OpensTheExerciseInTheMode(string mode, string exercise, string location)
    {
        var player = await GamePlayer.CreateAsync(_factory);

        var response = await player.Client.GetAsync($"/Games/Play?mode={mode}&exercise={exercise}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(location);
    }

    [Fact]
    public async Task ExercisePage_InAGame_ShowsTheGameBar_InPlaceOfFreePractice()
    {
        var player = await GamePlayer.CreateAsync(_factory);

        var sprint = await player.Client.GetStringAsync("/Exercise/GuessNote?game=sprint");
        sprint.Should().Contain("id=\"aaGame\"").And.Contain("data-mode=\"sprint\"")
            .And.Contain("data-aa-game-timer").And.Contain("1:00").And.Contain("60-second sprint")
            .And.Contain("data-aa-game-stop").And.NotContain("id=\"aaFreePractice\"");

        var survival = await player.Client.GetStringAsync("/Exercise/GuessNote?game=survival&culture=pt-BR");
        WebUtility.HtmlDecode(survival).Should().Contain("data-mode=\"survival\"").And.Contain("Morte súbita")
            .And.NotContain("data-aa-game-timer");

        (await player.Client.GetStringAsync("/Exercise/GuessNote")).Should()
            .NotContain("id=\"aaGame\"").And.Contain("id=\"aaFreePractice\"");
        (await player.Client.GetStringAsync("/Exercise/GuessNote?game=marathon")).Should().NotContain("id=\"aaGame\"");
        (await player.Client.GetStringAsync("/Exercise/SingNote?game=sprint")).Should()
            .NotContain("id=\"aaGame\"", "a sung exercise can't be played as a game");
    }

    [Fact]
    public async Task Sprint_CountsTheRightAnswers_GivenInItsMinute()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var game = await player.StartAsync("sprint", "GuessNote");
        var runId = game.GetProperty("runId").GetInt32();
        game.GetProperty("mode").GetString().Should().Be("sprint");
        game.GetProperty("secondsLeft").GetInt32().Should().Be(60);
        game.GetProperty("over").GetBoolean().Should().BeFalse();

        var play = await player.PlayAsync("GuessNote", runId);
        play.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["roundId", "playToken", "game"], "a game round tells no more than any other");
        var answer = await player.AnswerAsync("GuessNote", play.GetProperty("roundId").GetString()!, correct: true);
        game = answer.GetProperty("game");
        game.GetProperty("score").GetInt32().Should().Be(1);
        game.GetProperty("text").GetString().Should().Be("Right answers: 1");
        answer.GetProperty("newCorrectCount").GetInt32().Should().Be(1, "a game answer is scored like any other");

        game = await player.PlayAndAnswerAsync("GuessNote", runId, correct: false);
        game.GetProperty("score").GetInt32().Should().Be(1);
        game.GetProperty("answered").GetInt32().Should().Be(2);
        game.GetProperty("over").GetBoolean().Should().BeFalse("a sprint goes on after a wrong answer");

        // A round asked just before the whistle is answered on the way...
        var late = await player.PlayAsync("GuessNote", runId);
        var tooLate = await player.PlayAsync("GuessNote", runId);
        _factory.Clock.Advance(GameRules.SprintLength + TimeSpan.FromSeconds(2));
        game = (await player.AnswerAsync("GuessNote", late.GetProperty("roundId").GetString()!, correct: true)).GetProperty("game");
        game.GetProperty("score").GetInt32().Should().Be(2);
        game.GetProperty("over").GetBoolean().Should().BeTrue();
        game.GetProperty("secondsLeft").GetInt32().Should().Be(0);
        var end = game.GetProperty("end");
        end.GetProperty("title").GetString().Should().Be("Sprint over");
        end.GetProperty("text").GetString().Should().Be("2 right out of 3 answered.");
        end.GetProperty("record").GetString().Should().Be("New personal best!");
        end.GetProperty("newBest").GetBoolean().Should().BeTrue();

        // ...but not long after it: that answer is ordinary practice.
        _factory.Clock.Advance(TimeSpan.FromSeconds(5));
        game = (await player.AnswerAsync("GuessNote", tooLate.GetProperty("roundId").GetString()!, correct: true)).GetProperty("game");
        game.GetProperty("score").GetInt32().Should().Be(2);

        var over = await player.PlayAsync("GuessNote", runId);
        over.GetProperty("success").GetBoolean().Should().BeFalse();
        over.GetProperty("message").GetString().Should().Be("This game is over.");
        over.GetProperty("game").GetProperty("over").GetBoolean().Should().BeTrue();

        var run = player.Db(db => db.GameRuns.Single(r => r.Id == runId));
        run.Should().BeEquivalentTo(new { Score = 2, Answered = 3, UserId = GamePlayer.UserId });
        run.EndedAt.Should().Be(run.StartedAt + GameRules.SprintLength);
        player.Db(db => db.ScoreSnapshots.Count(s => s.UserId == GamePlayer.UserId && s.GameRunId == runId)).Should().Be(3);
        player.Db(db => db.ScoreSnapshots.Count(s => s.UserId == GamePlayer.UserId)).Should().Be(4);
    }

    [Fact]
    public async Task StoppedSprint_TakesTheAnswerOnItsWay_AndNoMoreRounds()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var runId = (await player.StartAsync("sprint", "HigherOrLower")).GetProperty("runId").GetInt32();
        var play = await player.PlayAsync("HigherOrLower", runId);
        _factory.Clock.Advance(TimeSpan.FromSeconds(10));

        var finish = await IntegrationHttp.ReadJsonAsync(await player.PostFinishAsync(runId));

        var game = finish.GetProperty("game");
        game.GetProperty("over").GetBoolean().Should().BeTrue();
        game.GetProperty("end").GetProperty("text").GetString().Should().Be("0 right out of 0 answered.");
        game.GetProperty("end").GetProperty("record").ValueKind.Should().Be(JsonValueKind.Null, "nothing was scored");

        _factory.Clock.Advance(TimeSpan.FromSeconds(1));
        game = (await player.AnswerAsync("HigherOrLower", play.GetProperty("roundId").GetString()!, correct: true)).GetProperty("game");
        game.GetProperty("score").GetInt32().Should().Be(1);
        game.GetProperty("end").GetProperty("record").GetString().Should().Be("New personal best!");
        (await player.PlayAsync("HigherOrLower", runId)).GetProperty("success").GetBoolean().Should().BeFalse();

        var run = player.Db(db => db.GameRuns.Single(r => r.Id == runId));
        run.EndedAt.Should().Be(run.StartedAt.AddSeconds(10));
    }

    [Fact]
    public async Task SuddenDeath_EndsAtTheFirstWrongAnswer_AndKeepsTheBestStreak()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var runId = (await player.StartAsync("survival", "HigherOrLower")).GetProperty("runId").GetInt32();

        (await player.PlayAndAnswerAsync("HigherOrLower", runId, correct: true)).GetProperty("text").GetString().Should().Be("Streak: 1");
        var game = await player.PlayAndAnswerAsync("HigherOrLower", runId, correct: true);
        game.GetProperty("text").GetString().Should().Be("Streak: 2");
        game.GetProperty("secondsLeft").ValueKind.Should().Be(JsonValueKind.Null);
        game = await player.PlayAndAnswerAsync("HigherOrLower", runId, correct: false);

        game.GetProperty("over").GetBoolean().Should().BeTrue();
        game.GetProperty("score").GetInt32().Should().Be(2);
        game.GetProperty("end").GetProperty("title").GetString().Should().Be("Game over");
        game.GetProperty("end").GetProperty("text").GetString().Should().Be("Streak: 2 in a row.");
        game.GetProperty("end").GetProperty("record").GetString().Should().Be("New personal best!");
        var over = await player.PlayAsync("HigherOrLower", runId);
        over.GetProperty("success").GetBoolean().Should().BeFalse();
        over.GetProperty("game").GetProperty("over").GetBoolean().Should().BeTrue();

        var second = (await player.StartAsync("survival", "HigherOrLower")).GetProperty("runId").GetInt32();
        await player.PlayAndAnswerAsync("HigherOrLower", second, correct: true);
        game = await player.PlayAndAnswerAsync("HigherOrLower", second, correct: false);
        game.GetProperty("end").GetProperty("record").GetString().Should().Be("Personal best: 2");
        game.GetProperty("end").GetProperty("newBest").GetBoolean().Should().BeFalse();

        var hub = WebUtility.HtmlDecode(await player.Client.GetStringAsync("/Games"));
        hub.Should().NotContain("No records yet.");
        hub.Should().MatchRegex(@"Sudden death\s*</td>\s*<td>Higher or Lower</td>\s*<td class=""text-end fw-semibold"">2</td>\s*<td class=""text-end"">2</td>");
        hub.Should().Contain("href=\"/Exercise/HigherOrLower?game=survival\"");
    }

    [Fact]
    public async Task WeakSpots_AreSuggested_AndPlayedAsASetOfTen()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        (await player.Client.GetStringAsync("/Games")).Should().Contain("No weak spots right now.");
        var filters = new Dictionary<string, string> { ["gtLevel"] = "10" };
        var runId = (await player.StartAsync("weakspots", "GuessTuning", filters)).GetProperty("runId").GetInt32();
        JsonElement game = default;
        for (var i = 0; i < WeakSpotRules.MinAnswers; i++)
        {
            game = await player.QuickAnswerAsync("GuessTuning", runId, correct: i < 4);
            game.GetProperty("total").GetInt32().Should().Be(10);
            game.GetProperty("over").GetBoolean().Should().Be(i == 9);
        }

        game.GetProperty("text").GetString().Should().Be("4 right out of 10");
        game.GetProperty("end").GetProperty("title").GetString().Should().Be("Set complete");
        game.GetProperty("end").GetProperty("record").ValueKind.Should().Be(JsonValueKind.Null, "weak spot sets keep no record");
        player.Db(db => db.GameRuns.Single(r => r.Id == runId).FilterJson).Should().Be("""{"gtLevel":"10"}""");

        var hub = WebUtility.HtmlDecode(await player.Client.GetStringAsync("/Games"));
        hub.Should().Contain("In tune or not?").And.Contain("Level: 10 cents").And.Contain("40% right in your last 10 answers")
            .And.Contain("href=\"/Exercise/GuessTuning?gtLevel=10&game=weakspots\"");
    }

    [Fact]
    public async Task GameRounds_AreAskedWithTheRunsSettings()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var runId = (await player.StartAsync("sprint", "GuessTuning", new() { ["gtLevel"] = "5", ["bogus"] = "x" }))
            .GetProperty("runId").GetInt32();
        player.Db(db => db.GameRuns.Single(r => r.Id == runId).FilterJson).Should().Be("""{"gtLevel":"5"}""");

        for (var i = 0; i < 6; i++)
        {
            _factory.Mixer.Plans.Clear();
            var play = await player.PlayAsync("GuessTuning", runId, new() { ["gtLevel"] = "50" });
            play.GetProperty("roundId").GetString().Should().NotBeNullOrEmpty();
            _factory.Mixer.Plans.Should().ContainSingle().Which[1].Cents.Should().BeOneOf(-5, 0, 5);
        }
    }

    [Fact]
    public async Task Games_AreOnlyPlayedByTheirPlayer_OnTheirExercise()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var rivals = player.Db(db =>
        {
            var run = new GameRun { UserId = GamePlayer.RivalId, Mode = GameModes.Survival, ExerciseId = player.Id("GuessNote"), StartedAt = _factory.Clock.GetUtcNow().UtcDateTime };
            db.GameRuns.Add(run);
            db.SaveChanges();
            return run.Id;
        });
        var runId = (await player.StartAsync("survival", "GuessNote")).GetProperty("runId").GetInt32();

        (await player.PostPlayAsync("GuessNote", rivals)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostFinishAsync(rivals)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostPlayAsync("GuessNote", runId, free: true)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostPlayAsync("SingNote", runId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostPlayAsync("GuessNote", 999_999)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var moved = await player.PlayAsync("HigherOrLower", runId);
        moved.GetProperty("success").GetBoolean().Should().BeFalse();
        moved.GetProperty("message").GetString().Should().Be("This game continues on another exercise.");

        (await player.PostStartAsync("placement", "GuessNote")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostStartAsync("marathon", "GuessNote")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostStartAsync("sprint", "SingNote")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.Client.PostAsJsonAsync("/Games/Start", new { mode = "sprint", exerciseId = 999_999 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        player.Db(db => db.GameRuns.Count(r => r.UserId == GamePlayer.UserId)).Should().Be(1);
    }

    [Fact]
    public async Task Answer_ToAnotherPlayersRun_IsOrdinaryPractice()
    {
        var player = await GamePlayer.CreateAsync(_factory);
        var rivals = player.Db(db =>
        {
            var run = new GameRun { UserId = GamePlayer.RivalId, Mode = GameModes.Survival, ExerciseId = player.Id("GuessNote"), StartedAt = _factory.Clock.GetUtcNow().UtcDateTime };
            db.GameRuns.Add(run);
            db.SaveChanges();
            return run.Id;
        });

        var answer = await player.QuickAnswerAsync("GuessNote", rivals, correct: true);

        answer.ValueKind.Should().Be(JsonValueKind.Null, "the run is not the player's");
        player.Db(db => db.ScoreSnapshots.Single(s => s.UserId == GamePlayer.UserId).GameRunId).Should().BeNull();
        player.Db(db => db.GameRuns.Single(r => r.Id == rivals).Answered).Should().Be(0);
    }

    /// <summary>
    /// <see cref="ExploreWebApplicationFactory"/> (no storage) whose time only moves when a
    /// test says so.
    /// </summary>
    public sealed class Factory : ExploreWebApplicationFactory
    {
        public AnswerTimeTests.ManualClock Clock { get; } = new(TimeProvider.System.GetUtcNow());

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
    }
}
