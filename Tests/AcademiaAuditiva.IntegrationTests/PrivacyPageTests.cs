using System.Net;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The privacy policy is a culture-suffixed view (Privacy.pt-BR.cshtml,
/// Privacy.fr-CA.cshtml), so each supported culture must resolve its own copy.
/// </summary>
public class PrivacyPageTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PrivacyPageTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en-US", "Privacy Policy")]
    [InlineData("pt-BR", "Política de Privacidade")]
    [InlineData("fr-CA", "Politique de confidentialité")]
    public async Task PrivacyPage_RendersThePolicyInEachCulture(string culture, string title)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/Home/Privacy?culture={culture}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"<h1 class=\"aa-section-title\">{title}</h1>");
        html.Should().Contain("href=\"mailto:contato@academiaauditiva.com\"");
        html.Should().Contain("<section id=\"cookies\">");
        html.Should().Contain("<code>aa_tz</code>", "the time zone cookie is disclosed");
        html.Should().Contain("<code>instrument</code>", "the instrument cookie is disclosed")
            .And.Contain("<code>guitarPosition</code>", "the guitar position cookie is disclosed")
            .And.Contain("<code>noteRange</code>", "the note range cookie is disclosed");
        html.Should().Contain("<code>aa-theme</code>", "the theme choice in local storage is disclosed")
            .And.Contain("<code>aa-answer-layout</code>", "the answer layout in local storage is disclosed");
    }
}
