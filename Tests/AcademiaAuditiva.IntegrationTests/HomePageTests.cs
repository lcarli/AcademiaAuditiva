using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The home page counts and lists every exercise by category, shows a few medals, named in
/// each culture, and the eight illustrations of docs/landing-art.md, and every image on it is
/// served as WebP.
/// </summary>
public class HomePageTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly string[] LandingArt = ["hero", "step-1", "step-2", "step-3", "student", "teacher", "faq", "final"];

    private readonly TestWebApplicationFactory _factory;

    public HomePageTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en-US", "Earn badges as you train", "Chord master")]
    [InlineData("pt-BR", "Desbloqueie conquistas enquanto treina", "Mestre dos acordes")]
    [InlineData("fr-CA", "Gagnez des badges en vous entraînant", "Maître des accords")]
    public async Task HomePage_ShowsTheShowcaseMedals_InEachCulture(string culture, string title, string chordMaster)
    {
        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/?culture={culture}"));

        html.Should().Contain($">{title}</h2>").And.Contain(chordMaster);
        foreach (var badge in BadgeCatalog.Showcase)
        {
            html.Should().MatchRegex($@"<img src=""/img/badges/{Regex.Escape(badge.Key)}\.webp\?v=[\w-]+""", "every medal shows its art");
        }
        html.Should().NotContain("Home.Badges.", "every text has a resource")
            .And.NotMatchRegex(@"Badge\.\w+\.Title");
    }

    [Theory]
    [InlineData("en-US", "{0} exercises", "{0} ways to train your ear", "{0}, grouped by area",
        "Ear Training|Melody|Harmony|Scales|Rhythm", "In tune or not?")]
    [InlineData("pt-BR", "{0} exercícios", "{0} jeitos de treinar o ouvido", "{0}, agrupados por área",
        "Percepção|Melodia|Harmonia|Escalas|Ritmo", "Afinado ou não?")]
    [InlineData("fr-CA", "{0} exercices", "{0} façons d'entraîner votre oreille", "{0}, regroupés par domaine",
        "Entraînement de l'oreille|Mélodie|Harmonie|Gammes|Rythme", "Juste ou pas\u00A0?")]
    public async Task HomePage_CountsAndListsEveryExercise_ByCategory_WithoutTheDatabase(
        string culture, string feature, string title, string faq, string categories, string guessTuning)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Exercises.Should()
                .BeEmpty("the test host seeds nothing, so the page lists the exercises from the code");
        }

        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/?culture={culture}"));

        var count = ExerciseCatalog.Count;
        count.Should().BeGreaterThan(29);
        html.Should().Contain(string.Format(feature, count) + "</h2>")
            .And.Contain(string.Format(title, count) + "</h2>")
            .And.Contain(">" + string.Format(faq, count));

        var groups = Regex.Matches(html,
                @"<h3 class=""aa-ex-group-title"" id=""aa-ex-(\w+)"">([^<]*)</h3>\s*<ul class=""aa-ex-list"" aria-labelledby=""aa-ex-\1"">(.*?)</ul>",
                RegexOptions.Singleline)
            .Select(m => (Category: m.Groups[1].Value, Title: m.Groups[2].Value,
                Exercises: Regex.Matches(m.Groups[3].Value, @"<a class=""aa-ex-item"" href=""/Exercise/(\w+)"">").Select(a => a.Groups[1].Value).ToList()))
            .ToList();

        groups.Select(g => g.Title).Should().Equal(categories.Split('|'));
        groups.Select(g => (g.Category, g.Exercises)).Should().BeEquivalentTo(
            ExerciseCatalog.ByCategory.Select(c => (c.Name, c.Exercises.Select(e => e.Name).ToList())),
            options => options.WithStrictOrdering());
        groups.SelectMany(g => g.Exercises).Should().HaveCount(count).And.OnlyHaveUniqueItems();
        html.Should().Contain($"<span class=\"aa-ex-item-title\">{guessTuning}</span>");
        html.Should().NotMatchRegex(@"Exercise\.\w+\.Subtitle|ExerciseCategory\.|>Home\.", "every text has a resource");
    }

    [Fact]
    public async Task HomePage_ShowsTheLandingArt_AndLoadsOnlyTheHeroEagerly()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        var tags = Regex.Matches(html, @"<img src=""/img/landing/([\w-]+)\.webp\?v=[\w-]+""[^>]*>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Value);

        tags.Keys.Should().BeEquivalentTo(LandingArt, "docs/landing-art.md lists where each image goes");
        foreach (var (name, tag) in tags)
        {
            tag.Should().Contain(@"alt=""""").And.MatchRegex(@"width=""\d+"" height=""\d+""", name);
            if (name == "hero")
            {
                tag.Should().NotContain("loading=", "the hero is the largest thing on screen when the page opens");
            }
            else
            {
                tag.Should().Contain(@"loading=""lazy""", name);
            }
        }
    }

    [Fact]
    public async Task EveryImageOnTheHomePage_IsServedAsWebp()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");

        var sources = Regex.Matches(html, @"<img\b[^>]*?\bsrc=""([^""]+)""")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Distinct()
            .ToList();

        sources.Should().HaveCountGreaterThanOrEqualTo(BadgeCatalog.Showcase.Count + LandingArt.Length);
        foreach (var src in sources)
        {
            var response = await client.GetAsync(src);

            response.StatusCode.Should().Be(HttpStatusCode.OK, src);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp", src);
        }
    }
}
