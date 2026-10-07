using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessQuality plays a chord in root position and asks its quality. Its Chord type filter
/// picks the chords: the major and minor ones, the triads, the seventh chords, the sus, 6 and
/// add9 chords, the triads and seventh chords together, or all thirteen.
/// </summary>
public class GuessQualityExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessQuality" };

    private const string AllChords =
        "major minor diminished augmented major7 dominant7 minor7 halfDiminished diminished7 sus2 sus4 major6 add9";

    // Semitones from the root to each note of every quality.
    private static readonly Dictionary<string, int[]> Semitones = new()
    {
        ["major"] = [0, 4, 7],
        ["minor"] = [0, 3, 7],
        ["diminished"] = [0, 3, 6],
        ["augmented"] = [0, 4, 8],
        ["major7"] = [0, 4, 7, 11],
        ["dominant7"] = [0, 4, 7, 10],
        ["minor7"] = [0, 3, 7, 10],
        ["halfDiminished"] = [0, 3, 6, 10],
        ["diminished7"] = [0, 3, 6, 9],
        ["sus2"] = [0, 2, 7],
        ["sus4"] = [0, 5, 7],
        ["major6"] = [0, 4, 7, 9],
        ["add9"] = [0, 4, 7, 14],
    };

    [Theory]
    [InlineData("both", "major minor")]
    [InlineData("triads", "major minor diminished augmented")]
    [InlineData("sevenths", "major7 dominant7 minor7 halfDiminished diminished7")]
    [InlineData("susAdded", "sus2 sus4 major6 add9")]
    [InlineData("triadsSevenths", "major minor diminished augmented major7 dominant7 minor7 halfDiminished diminished7")]
    [InlineData("all", AllChords)]
    [InlineData("seventh", AllChords)]
    [InlineData(null, AllChords)]
    public void EachChordType_PlaysItsChords_AndAllOfThem(string? chordGroup, string qualities)
    {
        var rounds = Enumerable.Range(0, 800).Select(_ => Round(chordGroup)).ToList();

        rounds.Select(round => round.Value<string>("answer")).ToHashSet()
            .Should().BeEquivalentTo(qualities.Split(' '), "the {0} chord type plays these chords", chordGroup ?? "default");
        rounds.Should().AllSatisfy(round =>
        {
            var answer = round.Value<string>("answer")!;
            round.Value<string>("type").Should().Be(answer);
            var notes = Midis(round["notes"]!);
            notes.Select(note => note - notes[0]).Should().Equal(Semitones[answer], "the round plays a {0} chord in root position", answer);
            MusicTheoryService.MidiToNote(notes[0]).Should().Be(round.Value<string>("root") + Octave(notes[0]),
                "the root is the bass, without its octave");
        });
    }

    [Fact]
    public void TheFilter_OffersEveryChordType_AndEveryButtonIsAnAnswer()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-quality-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var exercise = db.Exercises.Single(e => e.Name == "GuessQuality");
        var filter = JsonConvert.DeserializeObject<List<FilterOptionGroup>>(exercise.FiltersJson!)!.Should().ContainSingle().Subject;
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(exercise.AnswerButtonsJson!)!["guessAnswer"];

        filter.Name.Should().Be("chordGroup");
        filter.Options.Select(option => option.Value).Should().Equal(
            ["both", "triads", "sevenths", "susAdded", "triadsSevenths", "all"], "the major and minor chords come first, as the page opens");
        filter.Options.Select(option => option.Value).Should().BeEquivalentTo(MusicTheoryService.QualityGroups.Keys,
            "the page shows the answers of each chord type the filter offers");
        buttons.Values.Should().Equal(AllChords.Split(' '), "the student can give every answer, and every button can be right");
        MusicTheoryService.QualityGroups.Values.Should().AllSatisfy(group =>
            group.Should().Equal(buttons.Values.Where(group.Contains), "each chord type shows its buttons in the page's order"));
    }

    [Fact]
    public void InTheTopOctave_TheAdd9ChordsPastTheSamples_ArePlayedAnOctaveLower()
    {
        // A6 add9 reaches B7, the highest sample; the ninths of A#6 and B6 are past it.
        var lowered = new HashSet<string>();

        for (var i = 0; i < 2000; i++)
        {
            var round = Round("susAdded", noteRange: "C6-C6");

            var notes = Midis(round["notes"]!);
            notes.Should().AllSatisfy(note => note.Should().BeLessThanOrEqualTo(PianoSamples.HighestMidi));
            var root = round.Value<string>("root")!;
            var tooHigh = round.Value<string>("answer") == "add9" && root is "A#" or "B";
            Octave(notes[0]).Should().Be(tooHigh ? 5 : 6, "{0} {1} is played in octave 6 unless it would pass B7", root, round.Value<string>("answer"));
            if (tooHigh)
                lowered.Add(root);
        }

        lowered.Should().BeEquivalentTo(["A#", "B"]);
    }

    private static JObject Round(string? chordGroup, string? noteRange = null)
    {
        var filters = new Dictionary<string, string>();
        if (chordGroup is not null)
            filters["chordGroup"] = chordGroup;
        if (noteRange is not null)
            filters["noteRange"] = noteRange;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static List<int> Midis(JToken notes) =>
        [.. notes.Values<string>().Select(note => MusicTheoryService.NoteToMidi(note!) ?? throw new ArgumentException(note))];

    private static int Octave(int midi) => midi / 12 - 1;
}
