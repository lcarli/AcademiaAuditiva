using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The note range slider moves the chords of every chord exercise: the roots are in the
/// octaves of the range (octave 4 without one, where the slider starts), and in the top
/// octave a chord that would go past the highest sample (B7) is played an octave lower.
/// </summary>
public class ChordRangeTests
{
    // In C the chords of every degree of the key have their root in the octave of the tonic.
    private static readonly Dictionary<string, string> InC = new()
    {
        ["chordType"] = "all",
        ["chordGroup"] = "all",
        ["keySelect"] = "C",
        ["cadenceRoot"] = "C",
    };

    [Theory]
    [InlineData("GuessChords", "C2-C2", 2)]
    [InlineData("GuessChords", "C5-C5", 5)]
    [InlineData("GuessChords", null, 4)]
    [InlineData("GuessQuality", "C2-C2", 2)]
    [InlineData("GuessQuality", "C5-C5", 5)]
    [InlineData("GuessQuality", null, 4)]
    [InlineData("GuessFunction", "C2-C2", 2)]
    [InlineData("GuessFunction", "C5-C5", 5)]
    [InlineData("GuessFunction", null, 4)]
    [InlineData("GuessInversion", "C2-C2", 2)]
    [InlineData("GuessInversion", "C5-C5", 5)]
    [InlineData("GuessInversion", null, 4)]
    [InlineData("GuessCadence", "C2-C2", 2)]
    [InlineData("GuessCadence", "C5-C5", 5)]
    [InlineData("GuessCadence", null, 4)]
    public void TheRoots_AreInTheOctaveOfTheRange(string exerciseName, string? noteRange, int octave)
    {
        var filters = new Dictionary<string, string>(InC);
        if (noteRange is not null)
            filters["noteRange"] = noteRange;

        for (var round = 0; round < 30; round++)
        {
            var chord = Generate(exerciseName, filters);

            Roots(chord).Should().NotBeEmpty().And.AllSatisfy(root => (root / 12 - 1).Should().Be(octave,
                "the roots of {0} follow the note range {1}", chord.ToString(Newtonsoft.Json.Formatting.None), noteRange));
        }
    }

    [Theory]
    [InlineData("GuessChords", "chordType", "all")]
    [InlineData("GuessQuality", "chordGroup", "all")]
    [InlineData("GuessInversion", "invQuality", "both")]
    [InlineData("GuessFunction", "keySelect", "B")]
    [InlineData("GuessCadence", "cadenceRoot", "B")]
    public void InTheTopOctave_EveryNoteHasASample(string exerciseName, string filter, string value)
    {
        // In B, the dominant of octave 6 (F#7 A#7 C#8) goes past B7: so does every cadence.
        var filters = new Dictionary<string, string> { ["noteRange"] = "C6-C6", [filter] = value };

        for (var round = 0; round < 100; round++)
        {
            var chord = Generate(exerciseName, filters);

            Notes(chord).Should().NotBeEmpty().And.AllSatisfy(midi => midi.Should().BeInRange(Midi("C5"), PianoSamples.HighestMidi,
                "{0} plays {1} an octave lower at most", exerciseName, chord.ToString(Newtonsoft.Json.Formatting.None)));
        }
    }

    private static JObject Generate(string exerciseName, Dictionary<string, string> filters) =>
        JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(new Exercise { Name = exerciseName }, filters));

    /// <summary>The root of each chord, which the generators build the chord up from.</summary>
    private static List<int> Roots(JObject chord)
    {
        if (chord["chords"] is JArray chords)
            return [.. chords.Select(notes => Midi((string)notes[0]!))];

        var notes = Notes(chord);
        return (string?)chord["inversion"] switch
        {
            // The inversions raise the root, then the third, an octave.
            "first" => [notes[2] - 12],
            "second" => [notes[1] - 12],
            _ => [notes[0]],
        };
    }

    private static List<int> Notes(JObject chord) =>
        [.. chord.SelectTokens("$.chords[*][*]").Concat(chord.SelectTokens("$.notes[*]")).Select(note => Midi((string)note!))];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
