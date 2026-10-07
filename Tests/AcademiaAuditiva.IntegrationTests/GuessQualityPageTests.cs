using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The GuessQuality page offers every chord type in its filter and an answer button for each of
/// the thirteen qualities, named in the student's language. Its script shows the buttons of the
/// chord type picked, from the qualities the server plays for each, which the page carries.
/// </summary>
public class GuessQualityPageTests(SignedInWebApplicationFactory factory) : IClassFixture<SignedInWebApplicationFactory>
{
    private static readonly string[] ChordTypes = ["both", "triads", "sevenths", "susAdded", "triadsSevenths", "all"];

    private static readonly string[] Qualities =
        ["major", "minor", "diminished", "augmented", "major7", "dominant7", "minor7", "halfDiminished", "diminished7", "sus2", "sus4", "major6", "add9"];

    [Theory]
    [InlineData("en-US",
        "Majors and Minors|All triads|Seventh chords|Sus, 6th and add9 chords|Triads and sevenths|All chords",
        "Major|Minor|Diminished|Augmented|Major 7|Dominant 7|Minor 7|Half-diminished|Diminished 7|Sus2|Sus4|Major 6|Add9")]
    [InlineData("pt-BR",
        "Maiores e menores|Todas as tríades|Acordes com sétima|Suspensos, com sexta e com nona|Tríades e sétimas|Todos os acordes",
        "Maior|Menor|Diminuto|Aumentado|Maior 7|Dominante 7|Menor 7|Meio-diminuto|Diminuto 7|Sus2|Sus4|Maior 6|Add9")]
    [InlineData("fr-CA",
        "Majeurs et mineurs|Toutes les triades|Accords de septième|Suspendus, de sixte et de neuvième ajoutée|Triades et septièmes|Tous les accords",
        "Majeur|Mineur|Diminué|Augmenté|Majeur 7|Dominante 7|Mineur 7|Semi-diminué|Diminué 7|Sus2|Sus4|Majeur 6|Add9")]
    public async Task Page_NamesEveryChordTypeAndQuality_InTheStudentsLanguage(string culture, string chordTypes, string qualities)
    {
        SeedExercises();

        var html = await factory.CreateClient().GetStringAsync($"/Exercise/GuessQuality?culture={culture}");

        var select = Regex.Match(html, "<select id=\"chordGroup\".*?</select>", RegexOptions.Singleline).Value;
        Regex.Matches(select, "<option value=\"(\\w+)\"[^>]*>([^<]*)</option>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(ChordTypes.Zip(chordTypes.Split('|')));
        Regex.Matches(html, "<button type=\"button\" class=\"aa-answer guessAnswer\" value=\"(\\w+)\">([^<]*)</button>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(Qualities.Zip(qualities.Split('|')));
    }

    [Fact]
    public async Task Page_CarriesTheQualitiesOfEachChordType()
    {
        SeedExercises();

        var html = await factory.CreateClient().GetStringAsync("/Exercise/GuessQuality");

        var groups = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            WebUtility.HtmlDecode(Regex.Match(html, "<div id=\"aa-quality-groups\" hidden data-groups=\"([^\"]*)\"").Groups[1].Value));
        groups.Should().NotBeNull().And.HaveCount(ChordTypes.Length);
        groups!.Keys.Should().BeEquivalentTo(ChordTypes);
        foreach (var (chordType, played) in MusicTheoryService.QualityGroups)
        {
            groups[chordType].Should().Equal(played, "the page shows the buttons of the chords {0} plays", chordType);
        }
        groups["all"].Should().Equal(Qualities);
    }

    private void SeedExercises()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }
}
