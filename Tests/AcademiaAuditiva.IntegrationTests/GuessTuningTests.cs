using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// In tune or not? (GuessTuning) offers the levels in its filter and a button for each answer,
/// with its texts in the student's language. Its round plays the note twice, the second time the
/// level's cents out of tune or in tune, without telling which.
/// </summary>
public class GuessTuningTests : IClassFixture<ExploreWebApplicationFactory>
{
    private static readonly string[] Levels = ["50", "25", "10", "5"];

    private static readonly string[] Answers = ["inTune", "sharp", "flat"];

    private readonly ExploreWebApplicationFactory _factory;

    public GuessTuningTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Theory]
    [InlineData("en-US", "In tune or not?", "Was the second note in tune, sharp or flat?", "Level",
        "50 cents (a quarter tone)|25 cents|10 cents|5 cents", "In tune|Sharp|Flat",
        "Sing the first note and keep it in your head while the second one plays.")]
    [InlineData("pt-BR", "Afinado ou não?", "A segunda nota estava afinada, alta ou baixa?", "Nível",
        "50 cents (um quarto de tom)|25 cents|10 cents|5 cents", "Afinada|Alta|Baixa",
        "Cante a primeira nota e mantenha-a na cabeça enquanto a segunda toca.")]
    [InlineData("fr-CA", "Juste ou pas\u00A0?", "La deuxième note était-elle juste, trop haute ou trop basse\u00A0?", "Niveau",
        "50 cents (un quart de ton)|25 cents|10 cents|5 cents", "Juste|Trop haute|Trop basse",
        "Chantez la première note et gardez-la en tête pendant que la deuxième joue.")]
    public async Task Page_OffersTheLevelsAndTheAnswers_InTheStudentsLanguage(
        string culture, string title, string question, string levelLabel, string levels, string answers, string tip)
    {
        var html = await _factory.CreateClient().GetStringAsync($"/Exercise/GuessTuning?culture={culture}");

        var select = Regex.Match(html, "<select id=\"gtLevel\".*?</select>", RegexOptions.Singleline).Value;
        Regex.Matches(select, "<option value=\"(\\w+)\"[^>]*>([^<]*)</option>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(Levels.Zip(levels.Split('|')));
        Regex.Matches(html, "<button type=\"button\" class=\"aa-answer guessAnswer\" value=\"(\\w+)\">([^<]*)</button>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(Answers.Zip(answers.Split('|')));
        html.Should().Contain("aa-answer-grid", "the tour points at the answers");

        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain(title).And.Contain(question).And.Contain($">{levelLabel}<").And.Contain(tip);
        text.Should().NotMatchRegex(@"Exercise\.(GuessTuning|SelectGuessTuning|Tuning\.|TuningLevel\.)", "every text has a resource");
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("10", 10)]
    [InlineData("5", 5)]
    public async Task Round_PlaysTheNoteTwice_TheSecondTimeTheLevelsCentsOutOfTune_WithoutTellingWhich(string level, int cents)
    {
        var (exerciseId, client) = await StartAsync();

        for (var i = 0; i < 12; i++)
        {
            _factory.Mixer.Plans.Clear();
            var play = await PlayAsync(client, exerciseId, level);

            play.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                ["roundId", "playToken"], "the round doesn't tell the note nor whether it is out of tune");
            var plan = _factory.Mixer.Plans.Should().ContainSingle().Subject;
            plan.Should().HaveCount(2);
            plan[1].SampleName.Should().Be(plan[0].SampleName, "the same note plays twice");
            plan[0].Cents.Should().Be(0, "the first note is the reference");
            plan[1].Cents.Should().BeOneOf(-cents, 0, cents);
            plan[1].StartTimeSeconds.Should().BeGreaterThan(plan[0].StartTimeSeconds + (plan[0].DurationSeconds ?? 0));
        }
    }

    [Fact]
    public async Task Round_IsRight_OnlyWithWhereTheSecondNoteWent()
    {
        var (exerciseId, client) = await StartAsync();

        for (var i = 0; i < 6; i++)
        {
            _factory.Mixer.Plans.Clear();
            var play = await PlayAsync(client, exerciseId, "25");
            var answer = Answer();

            var wrong = await ValidateAsync(client, exerciseId, play, Answers.First(a => a != answer));
            wrong.GetProperty("success").GetBoolean().Should().BeTrue();
            wrong.GetProperty("isCorrect").GetBoolean().Should().BeFalse("the second note was {0}", answer);

            _factory.Mixer.Plans.Clear();
            play = await PlayAsync(client, exerciseId, "25");
            answer = Answer();

            var right = await ValidateAsync(client, exerciseId, play, answer);
            right.GetProperty("success").GetBoolean().Should().BeTrue();
            right.GetProperty("isCorrect").GetBoolean().Should().BeTrue("the second note was {0}", answer);
        }
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "GuessTuning").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    private static async Task<JsonElement> PlayAsync(HttpClient client, int exerciseId, string level)
    {
        var filters = new Dictionary<string, string> { ["gtLevel"] = level };
        return await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, filters }));
    }

    // What the second note of the round the mixer was given did.
    private string Answer()
    {
        var second = _factory.Mixer.Plans.Should().ContainSingle().Subject[1];
        return second.Cents switch { > 0 => "sharp", < 0 => "flat", _ => "inTune" };
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, int exerciseId, JsonElement play, string userGuess) =>
        await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));
}
