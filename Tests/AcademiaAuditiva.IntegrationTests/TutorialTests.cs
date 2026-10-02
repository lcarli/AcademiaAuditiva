using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Tutorials;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Guided tours: the dashboard and every exercise page render their steps for
/// wwwroot/js/core/tutorial.js, which starts the tour by itself until
/// POST /Tutorial/Seen records that the student closed it.
/// </summary>
public class TutorialTests : IClassFixture<SignedInWebApplicationFactory>
{
    private readonly SignedInWebApplicationFactory _factory;

    public TutorialTests(SignedInWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_StartsItsTour_UntilTheStudentClosesIt()
    {
        PrepareDatabase();
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());

        var (html, tour) = await GetTourAsync(client, "/Dashboard");

        tour.GetProperty("key").GetString().Should().Be(TutorialCatalog.Dashboard);
        tour.GetProperty("autoStart").GetBoolean().Should().BeTrue();
        tour.GetProperty("url").GetString().Should().Be("/Tutorial/Seen");
        var labels = tour.GetProperty("labels");
        labels.GetProperty("next").GetString().Should().Be("Next");
        labels.GetProperty("done").GetString().Should().Be("Done");
        labels.GetProperty("stepOf").GetString().Should().Be("Step {0} of {1}");
        var steps = tour.GetProperty("steps").EnumerateArray().ToList();
        TutorialCatalog.TryGet(TutorialCatalog.Dashboard, out var dashboard).Should().BeTrue();
        steps.Select(s => s.GetProperty("target").GetString()).Should().Equal(dashboard!.Steps.Select(s => s.Target));
        steps[0].GetProperty("title").GetString().Should().Be("Welcome to Academia Auditiva");
        steps.Should().AllSatisfy(step =>
        {
            step.GetProperty("title").GetString().Should().NotStartWith("Tutorial.", "every title has a resource");
            step.GetProperty("text").GetString().Should().NotStartWith("Tutorial.", "every text has a resource");
        });
        html.Should().Contain("data-aa-tour-start hidden", "the replay button waits for tutorial.js")
            .And.Contain("How it works");
        AssertStepsHaveTargets(html, dashboard!, "/Dashboard");

        (await PostSeenAsync(client, "dashboard", finished: true)).Should().Be(HttpStatusCode.NoContent);

        (_, tour) = await GetTourAsync(client, "/Dashboard");
        tour.GetProperty("autoStart").GetBoolean().Should().BeFalse("the student closed the tour");
        tour.GetProperty("steps").GetArrayLength().Should().Be(steps.Count, "the replay button still opens it");
        var row = Rows().Should().ContainSingle().Which;
        row.TutorialKey.Should().Be(TutorialCatalog.Dashboard);
        row.Finished.Should().BeTrue();
    }

    [Fact]
    public async Task EveryExercisePage_StartsTheExerciseTour_AndHasWhatItsStepsPointAt()
    {
        var exercises = PrepareDatabase();
        var client = _factory.CreateClient();
        TutorialCatalog.TryGet(TutorialCatalog.Exercise, out var exerciseTour).Should().BeTrue();

        exercises.Should().NotBeEmpty();
        using var scope = new AssertionScope();
        foreach (var exercise in exercises)
        {
            var (html, tour) = await GetTourAsync(client, $"/Exercise/{exercise}");

            tour.GetProperty("key").GetString().Should().Be(TutorialCatalog.Exercise, exercise);
            tour.GetProperty("autoStart").GetBoolean().Should().BeTrue(exercise);
            AssertStepsHaveTargets(html, exerciseTour!, exercise);
        }
    }

    [Theory]
    [InlineData("pt-BR", "Próximo", "Passo {0} de {1}", "Boas-vindas à Academia Auditiva")]
    [InlineData("fr-CA", "Suivant", "Étape {0} sur {1}", "Bienvenue sur Academia Auditiva")]
    public async Task Tour_IsLocalized(string culture, string next, string stepOf, string welcome)
    {
        PrepareDatabase();

        var (html, tour) = await GetTourAsync(_factory.CreateClient(), $"/Dashboard?culture={culture}");

        tour.GetProperty("labels").GetProperty("next").GetString().Should().Be(next);
        tour.GetProperty("labels").GetProperty("stepOf").GetString().Should().Be(stepOf);
        tour.GetProperty("steps")[0].GetProperty("title").GetString().Should().Be(welcome);
        html.Should().Contain($"\"next\":\"{next}\"", "accented letters are not escaped");
    }

    [Fact]
    public async Task Seen_RejectsUnknownTours_AndRequestsWithoutTheAntiforgeryToken()
    {
        PrepareDatabase();
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient());

        (await PostSeenAsync(client, "Exercises")).Should().Be(HttpStatusCode.BadRequest);
        (await PostSeenAsync(client, key: null)).Should().Be(HttpStatusCode.BadRequest);
        (await PostSeenAsync(_factory.CreateClient(), TutorialCatalog.Exercise)).Should().Be(HttpStatusCode.BadRequest);

        Rows().Should().BeEmpty();
    }

    /// <summary>
    /// Seeds the exercises once and forgets the tours the student closed in earlier
    /// tests (the class shares one database); returns the exercise names.
    /// </summary>
    private List<string> PrepareDatabase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
        db.UserTutorials.RemoveRange(db.UserTutorials.Where(t => t.UserId == SignedInWebApplicationFactory.UserId));
        db.SaveChanges();
        return db.Exercises.OrderBy(e => e.ExerciseId).Select(e => e.Name).ToList();
    }

    private List<UserTutorial> Rows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.UserTutorials.Where(t => t.UserId == SignedInWebApplicationFactory.UserId).ToList();
    }

    /// <summary>GETs a page; returns its HTML and the tour data rendered for tutorial.js.</summary>
    private static async Task<(string Html, JsonElement Tour)> GetTourAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        var html = await response.Content.ReadAsStringAsync();

        var data = Regex.Match(html, "<script type=\"application/json\" id=\"aa-tour-data\">(.*?)</script>", RegexOptions.Singleline);
        data.Success.Should().BeTrue("{0} renders a tour", url);
        data.Groups[1].Value.Should().NotContain("<", "the JSON cannot close its script element");
        using var json = JsonDocument.Parse(data.Groups[1].Value);
        return (html, json.RootElement.Clone());
    }

    private static async Task<HttpStatusCode> PostSeenAsync(HttpClient client, string? key, bool finished = false)
    {
        var form = new Dictionary<string, string> { ["finished"] = finished ? "true" : "false" };
        if (key is not null) form["key"] = key;
        using var response = await client.PostAsync("/Tutorial/Seen", new FormUrlEncodedContent(form));
        return response.StatusCode;
    }

    /// <summary>
    /// Every step with a target finds it in the server's HTML (tutorial.js skips a
    /// step whose target is missing, so a renamed hook would drop it silently).
    /// </summary>
    private static void AssertStepsHaveTargets(string html, TutorialDefinition tour, string page)
    {
        foreach (var step in tour.Steps.Where(s => s.Target is not null))
        {
            step.Target!.Split(',').Any(selector => Matches(html, selector.Trim()))
                .Should().BeTrue("the {0} step points at something on {1}", step.Key, page);
        }
    }

    /// <summary>
    /// Enough of CSS for the catalog's selectors: .class, #id, [attribute] and
    /// [attribute=value]; a descendant selector needs each of its parts on the page.
    /// </summary>
    private static bool Matches(string html, string selector) => selector
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(part => Regex.IsMatch(html, Pattern(part)));

    private static string Pattern(string part)
    {
        if (part.StartsWith('.')) return $"class=\"[^\"]*(?<![\\w-]){Regex.Escape(part[1..])}(?![\\w-])";
        if (part.StartsWith('#')) return $"id=\"{Regex.Escape(part[1..])}\"";
        if (part.StartsWith('[') && part.EndsWith(']'))
        {
            var attribute = part[1..^1].Split('=', 2);
            return attribute.Length == 2
                ? $"\\s{Regex.Escape(attribute[0])}=\"{Regex.Escape(attribute[1])}\""
                : $"\\s{Regex.Escape(attribute[0])}[\\s=>]";
        }
        throw new NotSupportedException($"Selector part '{part}'.");
    }
}
