using System.Net;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The credits page names whose sounds and fonts the app uses, under which licenses,
/// in each culture (Credits.pt-BR.cshtml, Credits.fr-CA.cshtml), and every page links to it.
/// </summary>
public class CreditsPageTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CreditsPageTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en-US", "Credits")]
    [InlineData("pt-BR", "Créditos")]
    [InlineData("fr-CA", "Crédits")]
    public async Task CreditsPage_CreditsTheSoundsAndFonts_InEachCulture(string culture, string title)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/Home/Credits?culture={culture}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"<h1 class=\"aa-section-title\">{title}</h1>")
            .And.Contain("<section id=\"sounds\">")
            .And.Contain("FluidR3_GM").And.Contain("Frank Wen").And.Contain("Benjamin Gleitzman")
            .And.Contain("href=\"https://github.com/gleitz/midi-js-soundfonts\"")
            .And.Contain("href=\"https://creativecommons.org/licenses/by/3.0/us/\"")
            .And.Contain("href=\"https://github.com/lcarli/AcademiaAuditiva/blob/master/AcademiaAuditiva/Audio/Instruments/LICENSE.txt\"")
            .And.Contain("<section id=\"fonts\">")
            .And.Contain("href=\"/fonts/Inter-OFL.txt\"")
            .And.Contain("href=\"/fonts/Fraunces-OFL.txt\"");
    }

    [Fact]
    public async Task EveryPage_LinksToTheCredits()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Home/Privacy");

        html.Should().Contain("href=\"/Home/Credits\"");
    }

    [Theory]
    [InlineData("/fonts/Inter-OFL.txt")]
    [InlineData("/fonts/Fraunces-OFL.txt")]
    public async Task FontLicenses_AreServed(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SIL OPEN FONT LICENSE Version 1.1");
    }
}
