using AcademiaAuditiva.Services.Gamification;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The medals the home page shows (BadgeCatalog.Showcase).</summary>
public class BadgeCatalogTests
{
    [Fact]
    public void Showcase_HasSixAvailableBadges_WithEveryGroup()
    {
        BadgeCatalog.Showcase.Should().HaveCount(6, "the home page shows three per row on phones and six on wider screens")
            .And.OnlyContain(b => b.IsAvailable)
            .And.OnlyHaveUniqueItems();
        BadgeCatalog.Showcase.Select(b => b.Group).Distinct()
            .Should().BeEquivalentTo(BadgeCatalog.Groups, "every medal colour shows");
    }
}
