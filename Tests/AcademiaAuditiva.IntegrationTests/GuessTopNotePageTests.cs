using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// The GuessTopNote page offers the qualities of the chords in its filter and a button for each
/// tone of the chord that can be on top, with its texts in the student's language.
/// </summary>
public class GuessTopNotePageTests(SignedInWebApplicationFactory factory) : IClassFixture<SignedInWebApplicationFactory>
{
    private static readonly string[] Qualities = ["major", "minor", "both"];

    private static readonly string[] TopNotes = ["topRoot", "topThird", "topFifth"];

    [Theory]
    [InlineData("en-US", "Majors|Minors|Majors and Minors", "Root|Third|Fifth",
        "Listen to the chord and identify the note on top.", "The bass is always the root.")]
    [InlineData("pt-BR", "Maiores|Menores|Maiores e menores", "Fundamental|Terça|Quinta",
        "Ouça o acorde e identifique a nota de cima.", "O baixo é sempre a fundamental.")]
    [InlineData("fr-CA", "Majeurs|Mineurs|Majeurs et mineurs", "Fondamentale|Tierce|Quinte",
        "Écoutez l’accord et identifiez la note du dessus.", "La basse est toujours la fondamentale.")]
    public async Task Page_NamesTheQualitiesAndTheTones_InTheStudentsLanguage(
        string culture, string qualities, string topNotes, string question, string instructions)
    {
        SeedExercises();

        var html = await factory.CreateClient().GetStringAsync($"/Exercise/GuessTopNote?culture={culture}");

        var select = Regex.Match(html, "<select id=\"tnQuality\".*?</select>", RegexOptions.Singleline).Value;
        Regex.Matches(select, "<option value=\"(\\w+)\"[^>]*>([^<]*)</option>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(Qualities.Zip(qualities.Split('|')));
        Regex.Matches(html, "<button type=\"button\" class=\"aa-answer guessAnswer\" value=\"(\\w+)\">([^<]*)</button>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(TopNotes.Zip(topNotes.Split('|')));

        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain(question).And.Contain(instructions);
        text.Should().NotMatchRegex(@"Exercise\.(GuessTopNote|SelectGuessTopNote|TopNote\.)", "every text has a resource");
    }

    private void SeedExercises()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }
}
