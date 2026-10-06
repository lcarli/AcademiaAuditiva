using AcademiaAuditiva.Services;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// CompleteChord plays a chord for the student to write on the staff: its notes stacked in
/// thirds over the root, each with at most one accidental, among the chords the filters pick.
/// </summary>
public class CompleteChordRoundsTests
{
    // The semitones of each note of a chord above its root.
    private static readonly Dictionary<string, int[]> Intervals = new()
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
    };

    private static readonly string[] QualityFilters = ["major", "minor", "both", "triads", "sevenths", "all"];

    public static TheoryData<string, string, string, string> EveryFilter
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var quality in QualityFilters)
            foreach (var accidentals in new[] { "none", "any" })
            foreach (var root in new[] { "given", "hidden" })
            foreach (var octave in new[] { "3", "4" })
            {
                data.Add(quality, accidentals, root, octave);
            }
            return data;
        }
    }

    [Theory]
    [InlineData("major", "major")]
    [InlineData("minor", "minor")]
    [InlineData("both", "major minor")]
    [InlineData("triads", "major minor diminished augmented")]
    [InlineData("sevenths", "major7 dominant7 minor7 halfDiminished diminished7")]
    [InlineData("all", "major minor diminished augmented major7 dominant7 minor7 halfDiminished diminished7")]
    [InlineData(null, "major")]
    [InlineData("ninths", "major")]
    public void Qualities_OfEachFilter(string? filter, string qualities) =>
        CompleteChordRounds.Qualities(filter).Should().Equal(qualities.Split(' '));

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryChord_IsStackedInThirdsOverItsRoot_WithOneAccidentalAtMost(int octave)
    {
        var chords = CompleteChordRounds.Candidates("all", "any", octave);

        chords.Should().HaveCount(105, "every root of the 9 qualities, but B augmented and C and F diminished 7");
        chords.Should().OnlyHaveUniqueItems(chord => chord.Root + " " + chord.Quality);
        foreach (var chord in chords)
        {
            chord.Notes[0].Should().Be(chord.Root + octave, "the chord is in root position, in octave {0}", octave);
            chord.Notes.Select(note => Midi(note) - Midi(chord.Notes[0])).Should().Equal(Intervals[chord.Quality],
                "{0} {1} has the intervals of its quality", chord.Root, chord.Quality);
            chord.Notes.Select(note => Letter(note) - Letter(chord.Notes[0])).Should().Equal(
                Enumerable.Range(0, chord.Notes.Count).Select(k => 2 * k),
                "{0} {1} is written in thirds: {2}", chord.Root, chord.Quality, string.Join(" ", chord.Notes));
            chord.Notes.Should().AllSatisfy(note => note.Should().MatchRegex(@"^[A-G][#b]?\d$",
                "the staff editor writes one accidental per note"));
        }
    }

    [Theory]
    [InlineData("major")]
    [InlineData("minor")]
    [InlineData("diminished")]
    [InlineData("augmented")]
    [InlineData("major7")]
    [InlineData("dominant7")]
    [InlineData("minor7")]
    [InlineData("halfDiminished")]
    [InlineData("diminished7")]
    public void WithAccidentals_EveryChordOfAQualityMayComeUp(string quality)
    {
        // The augmented and the diminished 7 chords repeat every major third and minor third:
        // the 4 and 3 that sound different are all there, spelled with one accidental at most.
        var sounding = Enumerable.Range(0, 12).Select(root => PitchClasses(Intervals[quality].Select(i => root + i))).Distinct();
        var filter = quality.Contains('7') || quality == "halfDiminished" ? "sevenths" : "triads";

        CompleteChordRounds.Candidates(filter, "any", 4)
            .Where(chord => chord.Quality == quality)
            .Select(chord => PitchClasses(chord.Notes.Select(Midi)))
            .Distinct()
            .Should().BeEquivalentTo(sounding);
    }

    [Theory]
    [InlineData("major", "C:major F:major G:major")]
    [InlineData("minor", "D:minor E:minor A:minor")]
    [InlineData("both", "C:major F:major G:major D:minor E:minor A:minor")]
    [InlineData("triads", "C:major F:major G:major D:minor E:minor A:minor B:diminished")]
    [InlineData("sevenths", "C:major7 F:major7 G:dominant7 D:minor7 E:minor7 A:minor7 B:halfDiminished")]
    [InlineData("all", "C:major F:major G:major D:minor E:minor A:minor B:diminished "
        + "C:major7 F:major7 G:dominant7 D:minor7 E:minor7 A:minor7 B:halfDiminished")]
    public void WithoutAccidentals_OnlyTheChordsOfTheWhiteKeysComeUp(string quality, string chords)
    {
        foreach (var accidentals in new[] { "none", null, "sharps" })
        foreach (var octave in new[] { 3, 4 })
        {
            var candidates = CompleteChordRounds.Candidates(quality, accidentals, octave);

            candidates.Select(chord => chord.Root + ":" + chord.Quality).Should().BeEquivalentTo(chords.Split(' '));
            candidates.SelectMany(chord => chord.Notes).Should().AllSatisfy(note => note.Should().MatchRegex(@"^[A-G]\d$"));
        }
    }

    [Theory]
    [MemberData(nameof(EveryFilter))]
    public void Round_PlaysAChordOfTheFilters_AndAsksForTheNotesNotOnTheStaff(
        string quality, string accidentals, string root, string octave)
    {
        var filters = new Dictionary<string, string>
        {
            ["ccQuality"] = quality, ["ccAccidentals"] = accidentals, ["ccRoot"] = root, ["ccOctave"] = octave,
        };
        var candidates = CompleteChordRounds.Candidates(quality, accidentals, int.Parse(octave));
        var largest = candidates.Max(chord => chord.Notes.Count);
        var random = new Random(17);

        for (var round = 0; round < 20; round++)
        {
            var json = Round(filters, random);

            var notes = json["chordNotes"]!.Values<string>().ToList();
            var (chordRoot, chordQuality) = (json.Value<string>("chordRoot"), json.Value<string>("chordQuality"));
            candidates.Should().ContainSingle(chord => chord.Root == chordRoot && chord.Quality == chordQuality)
                .Which.Notes.Should().Equal(notes);
            json.Value<int>("octave").Should().Be(int.Parse(octave));
            json.Value<string>("clef").Should().Be(octave == "3" ? "bass" : "treble");

            var given = root == "given" ? notes.Take(1).ToList() : new List<string?>();
            json["promptNotes"]!.Values<string>().Should().Equal(given);
            json.Value<string>("answerString").Should().Be(string.Join("|", notes.Skip(given.Count).Select(note => note + ":w")),
                "the student writes the notes that aren't on the staff, from the lowest up");
            json.Value<int>("slots").Should().Be(largest - given.Count,
                "the staff takes as many notes as the largest chord, so it doesn't tell a triad from a seventh");
        }
    }

    [Theory]
    [InlineData("major", "given", 2)]
    [InlineData("both", "hidden", 3)]
    [InlineData("triads", "given", 2)]
    [InlineData("triads", "hidden", 3)]
    [InlineData("sevenths", "given", 3)]
    [InlineData("all", "given", 3)]
    [InlineData("all", "hidden", 4)]
    public void Round_OffersRoomForTheLargestChordOfTheFilters(string quality, string root, int slots)
    {
        foreach (var accidentals in new[] { "none", "any" })
        {
            var json = Round(new() { ["ccQuality"] = quality, ["ccAccidentals"] = accidentals, ["ccRoot"] = root }, new Random(3));

            json.Value<int>("slots").Should().Be(slots);
        }
    }

    [Fact]
    public void Rounds_PlayEveryChordOfTheFilters()
    {
        var filters = new Dictionary<string, string> { ["ccQuality"] = "all", ["ccAccidentals"] = "any" };
        var random = new Random(5);

        var played = Enumerable.Range(0, 3000)
            .Select(_ => Round(filters, random))
            .Select(json => json.Value<string>("chordRoot") + " " + json.Value<string>("chordQuality"))
            .ToHashSet();

        played.Should().BeEquivalentTo(CompleteChordRounds.Candidates("all", "any", 4).Select(chord => chord.Root + " " + chord.Quality));
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("ninths", "sharps", "shown", "5")]
    public void Round_WithoutValidFilters_IsAWhiteKeyMajorChord_FromItsGivenRoot_InOctave4(
        string? quality, string? accidentals, string? root, string? octave)
    {
        var filters = new Dictionary<string, string>();
        if (quality is not null) filters["ccQuality"] = quality;
        if (accidentals is not null) filters["ccAccidentals"] = accidentals;
        if (root is not null) filters["ccRoot"] = root;
        if (octave is not null) filters["ccOctave"] = octave;
        var random = new Random(11);

        for (var round = 0; round < 20; round++)
        {
            var json = Round(filters, random);

            json.Value<string>("chordQuality").Should().Be("major");
            json["chordNotes"]!.Values<string>().First().Should().BeOneOf("C4", "F4", "G4");
            json["promptNotes"]!.Values<string>().Should().ContainSingle();
            json.Value<int>("octave").Should().Be(4);
            json.Value<string>("clef").Should().Be("treble");
        }
    }

    private static JObject Round(Dictionary<string, string> filters, Random random) =>
        JObject.FromObject(CompleteChordRounds.Generate(filters, random));

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    // The staff position of a note: its letter, counted from C0.
    private static int Letter(string note) => 7 * (note[^1] - '0') + "CDEFGAB".IndexOf(note[0]);

    private static string PitchClasses(IEnumerable<int> midis) =>
        string.Join(",", midis.Select(midi => midi % 12).Order());
}
