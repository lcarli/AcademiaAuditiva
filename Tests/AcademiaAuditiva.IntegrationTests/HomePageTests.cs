using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The home page shows a few medals, named in each culture, and every image on it
/// is served as WebP.
/// </summary>
public class HomePageTests : IClassFixture<TestWebApplicationFactory>
{
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

    [Fact]
    public async Task EveryImageOnTheHomePage_IsServedAsWebp()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");

        var sources = Regex.Matches(html, @"<img\b[^>]*?\bsrc=""([^""]+)""")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Distinct()
            .ToList();

        sources.Should().HaveCountGreaterThanOrEqualTo(BadgeCatalog.Showcase.Count);
        foreach (var src in sources)
        {
            var response = await client.GetAsync(src);

            response.StatusCode.Should().Be(HttpStatusCode.OK, src);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp", src);
        }
    }
}
