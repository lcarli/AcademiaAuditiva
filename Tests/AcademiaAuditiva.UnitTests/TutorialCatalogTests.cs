using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Tutorials;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

public class TutorialCatalogTests
{
    [Fact]
    public void TourKeys_AreUniqueIgnoringCase_AndFitTheColumn()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tutorial-catalog-{Guid.NewGuid():N}")
            .Options);
        var maxLength = db.Model.FindEntityType(typeof(UserTutorial))!
            .FindProperty(nameof(UserTutorial.TutorialKey))!.GetMaxLength();
        maxLength.Should().NotBeNull();

        var keys = TutorialCatalog.All.Select(t => t.Key).ToList();

        keys.Select(k => k.ToUpperInvariant()).Should().OnlyHaveUniqueItems("TryGet ignores case");
        keys.Should().AllSatisfy(key =>
        {
            key.Should().MatchRegex("^[A-Za-z]+$");
            key.Length.Should().BeLessThanOrEqualTo(maxLength!.Value);
        });
    }

    [Fact]
    public void EveryTour_HasDistinctSteps_AndEndsOnTheReplayButton()
    {
        foreach (var tour in TutorialCatalog.All)
        {
            tour.Steps.Should().HaveCountGreaterThanOrEqualTo(2, tour.Key);
            tour.Steps.Select(s => s.Key).Should().OnlyHaveUniqueItems(tour.Key)
                .And.AllSatisfy(key => key.Should().MatchRegex("^[A-Za-z]+$"));
            tour.Steps.Where(s => s.Target is not null)
                .SelectMany(s => s.Target!.Split(','))
                .Should().AllSatisfy(selector => selector.Should().NotBeNullOrWhiteSpace(tour.Key));
            tour.Steps[^1].Target.Should().Be("[data-aa-tour-start]", "{0} ends by showing how to open it again", tour.Key);
        }
    }

    [Theory]
    [InlineData("Dashboard", TutorialCatalog.Dashboard)]
    [InlineData("dashboard", TutorialCatalog.Dashboard)]
    [InlineData("EXERCISE", TutorialCatalog.Exercise)]
    public void TryGet_IgnoresCase_AndReturnsTheCatalogKey(string key, string expected)
    {
        TutorialCatalog.TryGet(key, out var tour).Should().BeTrue();

        tour!.Key.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Exercises")]
    public void TryGet_RejectsUnknownTours(string? key)
    {
        TutorialCatalog.TryGet(key, out var tour).Should().BeFalse();

        tour.Should().BeNull();
    }
}
