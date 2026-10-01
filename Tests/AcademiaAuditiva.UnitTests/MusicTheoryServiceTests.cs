using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Behavioural tests for <see cref="IMusicTheoryService"/> — exercised
/// through <see cref="MusicTheoryServiceAdapter"/>, which is the production
/// implementation. Focus is on the two equivalence helpers that the
/// validators depend on; the answer parser splits on '|' so we cover each
/// arity (single note, note+quality, note+quality+inversion).
/// </summary>
public class MusicTheoryServiceTests
{
    private readonly IMusicTheoryService _svc = new MusicTheoryServiceAdapter();

    [Theory]
    [InlineData("C", "C")]
    [InlineData("C4", "C")]                  // octave digit ignored
    [InlineData("c", "C")]                   // case-insensitive
    [InlineData("C#", "Db")]                 // enharmonic sharp/flat
    [InlineData("Db", "C#")]
    [InlineData("F#", "Gb")]
    [InlineData("g#", "ab")]                 // enharmonic + casing
    [InlineData("D#5", "Eb3")]               // enharmonic + octave digits on both
    public void NotesAreEquivalent_TrueCases(string a, string b)
    {
        _svc.NotesAreEquivalent(a, b).Should().BeTrue($"{a} and {b} should be enharmonically equivalent");
    }

    [Theory]
    [InlineData("C", "D")]
    [InlineData("C#", "D#")]                 // adjacent semitones, not enharmonic
    [InlineData("C", "B")]
    public void NotesAreEquivalent_FalseCases(string a, string b)
    {
        _svc.NotesAreEquivalent(a, b).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "C")]
    [InlineData("C", null)]
    [InlineData("", "C")]
    public void NotesAreEquivalent_NullOrEmpty_DoesNotThrow(string? a, string? b)
    {
        var act = () => _svc.NotesAreEquivalent(a!, b!);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("C#", "Db")]                          // single-note enharmonic
    [InlineData("C|major", "C|major")]                // note + chord quality
    [InlineData("Db|major", "C#|major")]
    [InlineData("C|major|root", "C|major|root")]     // note + quality + inversion
    [InlineData("Db|minor|first", "C#|minor|first")]
    public void AnswersAreEquivalent_TrueCases(string user, string correct)
    {
        _svc.AnswersAreEquivalent(user, correct).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "C")]
    [InlineData("C", "")]
    [InlineData(null, "C")]
    [InlineData("C|major", "C")]                      // arity mismatch
    [InlineData("C|major", "C|minor")]                // quality mismatch
    [InlineData("C|major|root", "C|major|first")]     // inversion mismatch
    [InlineData("D|major", "C|major")]                // note mismatch
    public void AnswersAreEquivalent_FalseCases(string? user, string? correct)
    {
        _svc.AnswersAreEquivalent(user!, correct!).Should().BeFalse();
    }

    [Fact]
    public void AnswersAreEquivalent_QualityComparison_IsCaseInsensitive()
    {
        _svc.AnswersAreEquivalent("C|MAJOR", "C|major").Should().BeTrue();
        _svc.AnswersAreEquivalent("C|major|ROOT", "C|major|root").Should().BeTrue();
    }

    [Theory]
    [InlineData("C4", "C4", "1J")]
    [InlineData("Db4", "C#4", "1J")]  // enharmonic unison
    [InlineData("C4", "C#4", "2m")]
    [InlineData("C4", "D4", "2M")]
    [InlineData("E4", "C4", "3M")]    // direction is ignored
    [InlineData("C4", "F#4", "4A")]   // tritone
    [InlineData("C4", "G4", "5J")]
    [InlineData("C4", "B4", "7M")]
    [InlineData("C4", "C5", "8J")]
    [InlineData("C4", "E5", "3M")]    // compound intervals are reduced
    [InlineData("C3", "C5", "8J")]    // so are compound octaves
    public void GetIntervalBetweenNotes_NamesTheSimpleInterval(string from, string to, string expected)
    {
        MusicTheoryService.GetIntervalBetweenNotes(from, to).Should().Be(expected);
    }

    [Fact]
    public void GenerateVocalMelody_CanBeSungAtSight()
    {
        for (var i = 0; i < 200; i++)
        {
            var melody = MusicTheoryService.GenerateVocalMelody(
                measures: 1, timeSignature: "4/4", voiceType: "mezzosoprano", difficulty: "easy", includeRests: true);
            var notes = melody.Where(m => !m.IsRest).Select(m => m.Note).ToList();
            var midi = notes.Select(n => MusicTheoryService.NoteToMidi(n)!.Value).ToList();

            melody.Sum(m => m.Duration).Should().Be(4.0, "one 4/4 bar");
            melody[0].IsRest.Should().BeFalse("a melody never starts with a rest");
            notes[0].Should().EndWith("4", "it starts in the lower octave of the range");
            notes.Should().OnlyContain(n => !n.Contains('#'), "only natural notes are used");
            notes.Should().OnlyContain(n => n.EndsWith('4') || n.EndsWith('5'), "mezzo-soprano range");
            midi.Zip(midi.Skip(1), (a, b) => Math.Abs(b - a))
                .Should().NotContain(semitones => semitones > 4, "the voice moves by a step or a third");
        }
    }

    [Fact]
    public void GuessFullInterval_AnswersWithTheButtonCodes()
    {
        // Same codes as the answer buttons in SeedData; the tritone is "5d".
        var semitonesByCode = new Dictionary<string, int>
        {
            ["2m"] = 1, ["2M"] = 2, ["3m"] = 3, ["3M"] = 4, ["4J"] = 5, ["5d"] = 6,
            ["5J"] = 7, ["6m"] = 8, ["6M"] = 9, ["7m"] = 10, ["7M"] = 11, ["8J"] = 12,
        };
        var exercise = new Exercise { Name = "GuessFullInterval" };
        var filters = new Dictionary<string, string> { ["keySelect"] = "E", ["intervalDirection"] = "both" };

        for (var i = 0; i < 200; i++)
        {
            var json = JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(exercise, filters));
            var answer = (string)json["answer"]!;
            var note1 = MusicTheoryService.NoteToMidi((string)json["note1"]!)!.Value;
            var note2 = MusicTheoryService.NoteToMidi((string)json["note2"]!)!.Value;

            semitonesByCode.Should().ContainKey(answer);
            note1.Should().Be(64, "the first note is the selected key in octave 4");
            Math.Abs(note2 - note1).Should().Be(semitonesByCode[answer], json.ToString());
        }
    }

    [Fact]
    public void IntervalMelodico_PicksMajorOrMinor_WhenBothAreAllowed()
    {
        var exercise = new Exercise { Name = "IntervalMelodico" };
        var filters = new Dictionary<string, string> { ["keySelect"] = "G", ["scaleTypeSelect"] = "both" };

        var scales = Enumerable.Range(0, 100)
            .Select(_ => (string)JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(exercise, filters))["scale"]!)
            .ToList();

        scales.Should().OnlyContain(s => s == "major" || s == "minor");
        scales.Should().Contain("major").And.Contain("minor");
    }

    [Theory]
    [InlineData("H", "major")]
    [InlineData("C", "lydian-ish")]
    public void IntervalMelodico_FallsBackToCMajor_ForUnknownKeysOrScales(string key, string scale)
    {
        var exercise = new Exercise { Name = "IntervalMelodico" };
        var filters = new Dictionary<string, string> { ["keySelect"] = key, ["scaleTypeSelect"] = scale };

        var json = JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(exercise, filters));

        ((string)json["key"]!).Should().Be("C");
        ((string)json["scale"]!).Should().Be("major");
        json["melody"]!.Values<string>().Should().OnlyContain(n => !n!.Contains('#'), "C major has no sharps");
    }

    [Theory]
    [InlineData("F4", "E#4")]
    [InlineData("C5", "B#4")]
    [InlineData("B4", "Cb5")]
    [InlineData("E4", "Fb4")]
    [InlineData("D4", "C##4")]
    [InlineData("Bb3", "A#3")]
    public void NoteToMidi_HandlesEnharmonicOctaveCarryAndDoubleAccidentals(string canonical, string enharmonic)
    {
        MusicTheoryService.NoteToMidi(enharmonic).Should().Be(MusicTheoryService.NoteToMidi(canonical));
    }

    [Theory]
    [InlineData("F4", "major", "F4,G4,A4,Bb4,C5,D5,E5,F5")]
    [InlineData("Gb4", "major", "Gb4,Ab4,Bb4,Cb5,Db5,Eb5,F5,Gb5")]
    [InlineData("F#4", "major", "F#4,G#4,A#4,B4,C#5,D#5,E#5,F#5")]
    [InlineData("B4", "major", "B4,C#5,D#5,E5,F#5,G#5,A#5,B5")]
    public void GetScaleNotes_UsesTextbookSpelling(string root, string scale, string expected)
    {
        MusicTheoryService.GetScaleNotes(root, scale).Should().Equal(expected.Split(','));
    }

    [Theory]
    [InlineData("Ab4", "major", "Ab4,C5,Eb5")]
    [InlineData("D4", "minor", "D4,F4,A4")]
    [InlineData("Bb3", "major", "Bb3,D4,F4")]
    [InlineData("Eb4", "minor", "Eb4,Gb4,Bb4")]
    public void GetChordNotes_UsesStackedThirdSpelling(string root, string quality, string expected)
    {
        MusicTheoryService.GetChordNotes(root, quality).Should().Equal(expected.Split(','));
    }
}
