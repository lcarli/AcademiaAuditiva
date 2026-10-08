using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// GuessInterval and GuessFullInterval let the student hear the interval melodic, harmonic or
/// either, and give a tip for the harmonic one, in the student's language.
/// </summary>
public class IntervalModePageTests(SignedInWebApplicationFactory factory) : IClassFixture<SignedInWebApplicationFactory>
{
    private static readonly string[] Modes = ["melodic", "harmonic", "both"];

    public static TheoryData<string, string, string, string, string, string> Pages => new()
    {
        { "GuessInterval", "en-US", "Playback",
            "Melodic (one note after the other)|Harmonic (both notes together)|Both, at random",
            "In a harmonic interval, sing the lower note, then the upper one: it becomes a melodic interval.", "keySelect|scaleTypeSelect|intervalMode" },
        { "GuessInterval", "pt-BR", "Execução",
            "Melódico (uma nota depois da outra)|Harmônico (as duas notas juntas)|Ambos, ao acaso",
            "Num intervalo harmônico, cante a nota de baixo e depois a de cima: ele vira um intervalo melódico.", "keySelect|scaleTypeSelect|intervalMode" },
        { "GuessInterval", "fr-CA", "Exécution",
            "Mélodique (une note après l’autre)|Harmonique (les deux notes ensemble)|Les deux, au hasard",
            "Dans un intervalle harmonique, chantez la note du bas, puis celle du haut : il devient un intervalle mélodique.", "keySelect|scaleTypeSelect|intervalMode" },
        { "GuessFullInterval", "en-US", "Playback",
            "Melodic (one note after the other)|Harmonic (both notes together)|Both, at random",
            "Played together, seconds and sevenths sound harsh, thirds and sixths sound sweet, and fourths, fifths and octaves sound hollow.", "keySelect|intervalMode|intervalDirection" },
        { "GuessFullInterval", "pt-BR", "Execução",
            "Melódico (uma nota depois da outra)|Harmônico (as duas notas juntas)|Ambos, ao acaso",
            "Tocadas juntas, segundas e sétimas soam ásperas, terças e sextas soam doces, e quartas, quintas e oitavas soam ocas.", "keySelect|intervalMode|intervalDirection" },
        { "GuessFullInterval", "fr-CA", "Exécution",
            "Mélodique (une note après l’autre)|Harmonique (les deux notes ensemble)|Les deux, au hasard",
            "Jouées ensemble, les secondes et les septièmes sonnent âpres, les tierces et les sixtes, douces, et les quartes, les quintes et les octaves, creuses.", "keySelect|intervalMode|intervalDirection" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_OffersTheModes_AndATipForTheHarmonicInterval_InTheStudentsLanguage(
        string exerciseName, string culture, string label, string modes, string tip, string selects)
    {
        SeedExercises();

        var html = await factory.CreateClient().GetStringAsync($"/Exercise/{exerciseName}?culture={culture}");

        Regex.Matches(html, "<select id=\"(\\w+)\"").Select(m => m.Groups[1].Value).Should().Equal(selects.Split('|'));
        WebUtility.HtmlDecode(Regex.Match(html, "<label for=\"intervalMode\" class=\"form-label\">([^<]*)</label>").Groups[1].Value)
            .Should().Be(label);
        var select = Regex.Match(html, "<select id=\"intervalMode\".*?</select>", RegexOptions.Singleline).Value;
        Regex.Matches(select, "<option value=\"(\\w+)\"[^>]*>([^<]*)</option>")
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Should().Equal(Modes.Zip(modes.Split('|')), "the melodic interval comes first, as the page's default");

        var text = WebUtility.HtmlDecode(html);
        text.Should().Contain(tip);
        text.Should().NotMatchRegex(@"Exercise\.(IntervalMode|\w+Interval\.Tip)", "every text has a resource");
    }

    private void SeedExercises()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }
}
