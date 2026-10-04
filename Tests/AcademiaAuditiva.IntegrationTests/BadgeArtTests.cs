using System.Net;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// Every badge players can earn has its medal art in wwwroot/img/badges (made by
/// scripts/export-badge-art.py), and every file there belongs to a badge.
/// </summary>
public class BadgeArtTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BadgeArtTests(TestWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string> AvailableBadges => [.. BadgeCatalog.Available.Select(b => b.Key)];

    [Theory]
    [MemberData(nameof(AvailableBadges))]
    public async Task AvailableBadge_HasArt_ServedAsWebp(string key)
    {
        var response = await _factory.CreateClient().GetAsync($"/img/badges/{key}.webp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
    }

    [Fact]
    public void ArtFiles_AreNamedAfterBadges()
    {
        var webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;

        webRoot.GetDirectoryContents("img/badges").Select(f => f.Name)
            .Should().NotBeEmpty()
            .And.BeSubsetOf(BadgeCatalog.All.Select(b => $"{b.Key}.webp"));
    }
}
