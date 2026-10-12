using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The home page counts both tracks and highlights a few exercises, shows medals, named in
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
    [InlineData("en-US", "{0} exercises", "{0} ways to train your ear", "Music", "Audio", "In tune or not?",
        "Beginner · Intermediate · Advanced", "How does the Audio track work?")]
    [InlineData("pt-BR", "{0} exercícios", "{0} jeitos de treinar o ouvido", "Música", "Áudio", "Afinado ou não?",
        "Iniciante · Intermediário · Avançado", "Como funciona a trilha de Áudio?")]
    [InlineData("fr-CA", "{0} exercices", "{0} façons d'entraîner votre oreille", "Musique", "Audio", "Juste ou pas\u00A0?",
        "Débutant · Intermédiaire · Avancé", "Comment fonctionne le parcours Audio\u00A0?")]
    public async Task HomePage_CountsBothTracks_AndShowsOnlySelectedExamples_WithoutTheDatabase(
        string culture, string feature, string title, string music, string audio, string guessTuning,
        string levels, string audioQuestion)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Exercises.Should()
                .BeEmpty("the test host seeds nothing, so the page lists the exercises from the code");
        }

        var html = WebUtility.HtmlDecode(await _factory.CreateClient().GetStringAsync($"/?culture={culture}"));

        var catalogs = ExerciseCatalog.ByTrack.Where(t => t.Key is TrainingTracks.Music or TrainingTracks.Audio).ToArray();
        var count = catalogs.Sum(track => track.Exercises.Count());
        count.Should().BeGreaterThan(29);
        html.Should().Contain(string.Format(feature, count) + "</h2>")
            .And.Contain(string.Format(title, count) + "</h2>")
            .And.Contain(">" + string.Format(feature, count));

        var cards = Regex.Matches(html, @"<article\b[^>]*data-aa-track=""(\w+)""[^>]*>(.*?)</article>",
            RegexOptions.Singleline).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        cards.Keys.Should().Equal(TrainingTracks.Music, TrainingTracks.Audio);
        cards[TrainingTracks.Music].Should().Contain($">{music}</h3>");
        cards[TrainingTracks.Audio].Should().Contain($">{audio}</h3>").And.Contain("A/B");
        foreach (var catalog in catalogs)
        {
            var card = cards[catalog.Key];
            card.Should().Contain(string.Format(feature, catalog.Exercises.Count())).And.Contain(levels)
                .And.Contain($"href=\"/Exercise?track={catalog.Key.ToLowerInvariant()}\"");
            var examples = Regex.Matches(card, @"<a class=""aa-ex-item"" href=""/Exercise/(\w+)"">")
                .Select(match => match.Groups[1].Value).ToArray();
            examples.Should().HaveCount(4).And.OnlyHaveUniqueItems()
                .And.OnlyContain(name => catalog.Exercises.Any(exercise => exercise.Name == name));
            examples.Length.Should().BeLessThan(catalog.Exercises.Count(), "the landing is a preview, not the full catalog");
        }
        var allExamples = Regex.Matches(html, @"<a class=""aa-ex-item"" href=""/Exercise/(\w+)"">");
        allExamples.Should().HaveCount(8);
        html.Should().Contain($"<span class=\"aa-ex-item-title\">{guessTuning}</span>");
        html.Should().Contain(audioQuestion).And.Contain("id=\"aaFaq-7\"").And.Contain("href=\"#tracks\"");
        html.IndexOf("id=\"tracks\"", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("id=\"how-it-works\"", StringComparison.Ordinal), "both tracks are introduced near the top");
        html.Should().Contain("href=\"/Identity/Account/Register\"", "anonymous visitors can create an account");
        html.Should().NotMatchRegex(@"Exercise\.\w+\.Subtitle|ExerciseCategory\.|>Home\.", "every text has a resource");
    }

    [Fact]
    public async Task SignedInHomePage_OffersBothTracksRatherThanDefaultingToMusic()
    {
        using var factory = new SignedInWebApplicationFactory();
        var html = await factory.CreateClient().GetStringAsync("/");
        Regex.Matches(html, @"href=""#tracks""").Should().HaveCount(2, "hero and final CTA choose a track");
        html.Should().Contain("href=\"/Exercise?track=music\"").And.Contain("href=\"/Exercise?track=audio\"")
            .And.Contain("href=\"/Dashboard\"");
        Regex.Match(html, @"<div class=""aa-hero-ctas"">(.*?)</div>", RegexOptions.Singleline).Value
            .Should().NotContain("href=\"/Identity/Account/Register\"");
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
