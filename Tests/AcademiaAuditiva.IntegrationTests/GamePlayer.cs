using System.Net.Http.Json;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Plays game rounds over HTTP as the signed-in student, the way wwwroot/js/core/game.js does,
/// and answers them right or wrong (the right answer is read from the round on the server).
/// </summary>
internal sealed class GamePlayer
{
    public const string UserId = SignedInWebApplicationFactory.UserId;
    public const string RivalId = "game-rival";
    public const string WrongGuess = "not-an-answer";

    private readonly SignedInWebApplicationFactory _factory;
    private readonly Dictionary<string, int> _ids;

    private GamePlayer(SignedInWebApplicationFactory factory, HttpClient client, Dictionary<string, int> ids)
    {
        _factory = factory;
        Client = client;
        _ids = ids;
    }

    public HttpClient Client { get; }

    /// <summary>
    /// A player with no games and no answers yet (the factory's database is shared by the
    /// class's tests), and a rival who has games of their own.
    /// </summary>
    public static async Task<GamePlayer> CreateAsync(SignedInWebApplicationFactory factory)
    {
        Dictionary<string, int> ids;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!db.Exercises.Any()) SeedData.SeedExercises(db);
            if (!db.Users.Any(u => u.Id == RivalId))
                db.Users.Add(new IdentityUser { Id = RivalId, UserName = "rival@example.test", Email = "rival@example.test" });
            db.GameRuns.RemoveRange(db.GameRuns);
            db.ScoreSnapshots.RemoveRange(db.ScoreSnapshots.Where(s => s.UserId == UserId));
            db.SaveChanges();
            ids = db.Exercises.ToDictionary(e => e.Name, e => e.ExerciseId);
        }

        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return new GamePlayer(factory, client, ids);
    }

    public int Id(string exercise) => _ids[exercise];

    public Task<HttpResponseMessage> PostStartAsync(string mode, string exercise, Dictionary<string, string>? filters = null)
        => Client.PostAsJsonAsync("/Games/Start", new { mode, exerciseId = Id(exercise), filters });

    /// <summary>Starts a run as the page's first Play does, and returns its <c>game</c> status.</summary>
    public async Task<JsonElement> StartAsync(string mode, string exercise, Dictionary<string, string>? filters = null)
    {
        var json = await IntegrationHttp.ReadJsonAsync(await PostStartAsync(mode, exercise, filters));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        return json.GetProperty("game");
    }

    public Task<HttpResponseMessage> PostFinishAsync(int runId)
        => Client.PostAsJsonAsync("/Games/Finish", new { runId });

    public Task<HttpResponseMessage> PostPlayAsync(string exercise, int? gameRunId,
        Dictionary<string, string>? filters = null, bool free = false)
        => Client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId = Id(exercise), filters, free, gameRunId });

    public async Task<JsonElement> PlayAsync(string exercise, int gameRunId, Dictionary<string, string>? filters = null)
        => await IntegrationHttp.ReadJsonAsync(await PostPlayAsync(exercise, gameRunId, filters));

    public async Task<JsonElement> AnswerAsync(string exercise, string roundId, bool correct)
    {
        var guess = WrongGuess;
        if (correct)
        {
            using var scope = _factory.Services.CreateScope();
            var round = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
                .GetRoundAsync(UserId, Id(exercise), roundId);
            round.Should().NotBeNull("the round is answered once");
            guess = scope.ServiceProvider.GetRequiredService<IExerciseValidatorRegistry>()
                .Get(exercise)!.AnswerOf(round!.ExpectedAnswerJson);
        }

        var json = await IntegrationHttp.ReadJsonAsync(await Client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId = Id(exercise), roundId, userGuess = guess }));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("isCorrect").GetBoolean().Should().Be(correct);
        return json;
    }

    /// <summary>Plays a round of the run and answers it; returns the answer's <c>game</c> status.</summary>
    public async Task<JsonElement> PlayAndAnswerAsync(string exercise, int runId, bool correct)
    {
        var play = await PlayAsync(exercise, runId);
        play.GetProperty("game").GetProperty("over").GetBoolean().Should().BeFalse();
        var answer = await AnswerAsync(exercise, play.GetProperty("roundId").GetString()!, correct);
        return answer.GetProperty("game");
    }

    /// <summary>
    /// Answers a round of the run made the way RequestPlay makes it, without going through it:
    /// its rate limit takes 60 rounds a minute.
    /// </summary>
    public async Task<JsonElement> QuickAnswerAsync(string exercise, int runId, bool correct)
    {
        string roundId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = db.Exercises.Single(e => e.ExerciseId == Id(exercise));
            var filterJson = db.GameRuns.Where(r => r.Id == runId).Select(r => r.FilterJson).SingleOrDefault();
            var filters = ExerciseFilterPresets.Parse(filterJson);
            filters["instrument"] = "Piano";
            var plan = scope.ServiceProvider.GetRequiredService<ExercisePlaybackPlanner>().Plan(entity, filters);
            var round = await scope.ServiceProvider.GetRequiredService<IAudioTokenService>()
                .CreateRoundAsync(UserId, entity.ExerciseId, plan.ExpectedAnswerJson, [ExploreWebApplicationFactory.Clip],
                    filterJson: filterJson, gameRunId: runId);
            roundId = round.RoundId;
        }
        return (await AnswerAsync(exercise, roundId, correct)).GetProperty("game");
    }

    public T Db<T>(Func<ApplicationDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
