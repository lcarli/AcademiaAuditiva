using System.Net;
using System.Net.Http.Json;
using System.Text;
using AcademiaAuditiva.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The Explore page plays what the learner picks. Play answers with the spelled notes
/// and a token for their mix; the clip is never named.
/// </summary>
public class ExplorePageTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public ExplorePageTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
    }

    [Fact]
    public async Task Page_OffersNotesIntervalsChordsAndScales()
    {
        var html = await GetPageAsync("/Explore");

        html.Should().Contain("<h1 class=\"display-6 mt-2 mb-1 text-body-emphasis\">Explore sounds</h1>")
            .And.Contain("data-play-url=\"/Explore/Play\"")
            .And.Contain("data-octaves=\"{\"note\":[1,7],\"interval\":[2,6],\"chord\":[2,5],\"scale\":[2,6]}\"");
        foreach (var kind in new[] { "note", "interval", "chord", "scale" })
        {
            html.Should().Contain($"id=\"explore-tab-{kind}\"");
        }
        html.Should().Contain("Half-diminished").And.Contain("3rd inversion");
        html.Should().NotMatchRegex(@"\b(Explore|Exercise)\.[A-Z]", "every text has a resource");
    }

    [Fact]
    public async Task Page_IsLocalized()
    {
        var html = await GetPageAsync("/Explore?culture=fr-CA");

        html.Should().Contain("Explorez les sons").And.Contain("Semi-diminué").And.Contain("3e renversement");
        html.Should().NotMatchRegex(@"\b(Explore|Exercise)\.[A-Z]");
    }

    [Fact]
    public async Task Play_ReturnsTheSpelledNotes_AndATokenForTheirMix()
    {
        var client = await SignedInClientAsync();

        var json = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Explore/Play",
            new { kind = "chord", root = "C#", octave = 4, quality = "major", inversion = 0, arpeggio = false }));

        json.EnumerateObject().Select(p => p.Name).Should().Equal("token", "root", "notes", "simultaneous");
        json.GetProperty("root").GetString().Should().Be("Db");
        json.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).Should().Equal("Db4", "F4", "Ab4");
        json.GetProperty("simultaneous").GetBoolean().Should().BeTrue();

        var token = json.GetProperty("token").GetString()!;
        token.Should().MatchRegex("^[0-9a-f]{32}$");
        var tokens = _factory.Services.GetRequiredService<IAudioTokenService>();
        (await tokens.ResolveTokenAsync(SignedInWebApplicationFactory.UserId, token)).Should().Be(ExploreWebApplicationFactory.Clip);

        _factory.Mixer.Plans.Should().ContainSingle().Which.Should().Equal(
            new MixInput("Cs4.mp3", 0, 2.0), new MixInput("F4.mp3", 0, 2.0), new MixInput("Gs4.mp3", 0, 2.0));
    }

    [Theory]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"power"}""")]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"major","inversion":3}""")]
    [InlineData("""{"kind":"note","root":"C","octave":"high"}""")]
    [InlineData("null")]
    [InlineData("")]
    public async Task Play_RefusesAnythingOffTheLists(string body)
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/Explore/Play", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Mixer.Plans.Should().BeEmpty();
    }

    [Fact]
    public async Task Play_NeedsTheAntiforgeryToken()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/Explore/Play", new { kind = "note", root = "C", octave = 4 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Mixer.Plans.Should().BeEmpty();
    }

    private Task<HttpClient> SignedInClientAsync() =>
        IntegrationHttp.WithAntiforgeryHeaderAsync(_factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    private async Task<string> GetPageAsync(string url)
    {
        var response = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
