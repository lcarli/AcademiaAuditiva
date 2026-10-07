using System.Globalization;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The <c>noteRange</c> filter arrives from the <c>/Exercise/RequestPlay</c> body (or the
/// <c>noteRange</c> cookie), so parsing must never throw and must keep the generated note
/// list bounded — an unbounded range such as <c>C1-C300000000</c> used to allocate hundreds
/// of millions of notes per request. Only the exercises whose notes come from it offer it.
/// </summary>
public class NoteRangeFilterTests
{
    [Theory]
    [InlineData("C4-C4", new[] { 4 })]
    [InlineData("C3-C5", new[] { 3, 4, 5 })]
    [InlineData("C1-C6", new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData("C5-C2", new[] { 2, 3, 4, 5 })]              // reversed bounds are swapped
    [InlineData("C0-C9", new[] { 1, 2, 3, 4, 5, 6 })]        // clamped to the slider's C1–C6
    [InlineData("C1-C300000000", new[] { 1, 2, 3, 4, 5, 6 })]
    public void ParseOctaveRange_WellFormed_ReturnsBoundedOctaves(string noteRange, int[] expected)
    {
        MusicTheoryService.ParseOctaveRange(noteRange).Should().Equal(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C4")]
    [InlineData("garbage")]
    [InlineData("C-C4")]
    [InlineData("C4-")]
    [InlineData("4-5")]
    [InlineData("C#4-C5")]
    [InlineData("C-1-C4")]
    [InlineData("C+1-C4")]
    [InlineData("C 1-C4")]
    [InlineData("C4-C99999999999")]                           // int overflow
    public void ParseOctaveRange_Malformed_FallsBackToDefaultOctave(string? noteRange)
    {
        MusicTheoryService.ParseOctaveRange(noteRange).Should().Equal(4);
    }

    [Theory]
    [InlineData(null, 2, new[] { 2 })]
    [InlineData("garbage", 2, new[] { 2 })]
    [InlineData("C3-C5", 2, new[] { 3, 4, 5 })]
    public void ParseOctaveRange_WithADefaultOctave_FallsBackToIt(string? noteRange, int defaultOctave, int[] expected)
    {
        MusicTheoryService.ParseOctaveRange(noteRange, defaultOctave).Should().Equal(expected);
    }

    [Fact]
    public void GenerateNoteForExercise_GuessNote_HugeRange_StaysWithinSliderOctaves()
    {
        var exercise = new Exercise { Name = "GuessNote" };
        var filters = new Dictionary<string, string> { ["noteRange"] = "C1-C2147483647" };

        for (var i = 0; i < 50; i++)
        {
            var note = ReadProperty<string>(MusicTheoryService.GenerateNoteForExercise(exercise, filters), "note");
            OctaveOf(note).Should().BeInRange(MusicTheoryService.MinRangeOctave, MusicTheoryService.MaxRangeOctave);
        }
    }

    [Fact]
    public void GenerateNoteForExercise_GuessNote_WithoutRange_UsesDefaultOctave()
    {
        var result = MusicTheoryService.GenerateNoteForExercise(
            new Exercise { Name = "GuessNote" }, new Dictionary<string, string>());

        OctaveOf(ReadProperty<string>(result, "note")).Should().Be(4);
    }

    [Fact]
    public void GenerateNoteForExercise_GuessChords_MalformedRange_StillReturnsChord()
    {
        var filters = new Dictionary<string, string> { ["noteRange"] = "not-a-range", ["chordType"] = "all" };

        var result = MusicTheoryService.GenerateNoteForExercise(new Exercise { Name = "GuessChords" }, filters);

        ReadProperty<List<string>>(result, "notes").Should().NotBeEmpty();
    }

    // The page of an exercise offers the octave range only when its rounds follow it: with the
    // range on octave 1, which the other exercises never reach, the rounds of those play there.
    [Theory]
    [MemberData(nameof(ExercisePlaybackPlannerInstrumentTests.SeededExercises), MemberType = typeof(ExercisePlaybackPlannerInstrumentTests))]
    public void UsesNoteRange_ListsTheExercisesWhoseRoundsFollowTheRange(string exerciseName)
    {
        var exercise = new Exercise { Name = exerciseName };
        var filters = new Dictionary<string, string> { ["noteRange"] = "C1-C1" };
        // GuessMeter's clicks are not notes of its rounds: the notes of its accompaniment are.
        if (exerciseName == "GuessMeter")
            filters["gmLevel"] = "accompaniment";

        var notes = Enumerable.Range(0, 50)
            .SelectMany(_ => NotesIn(MusicTheoryService.GenerateNoteForExercise(exercise, filters)))
            .ToList();

        notes.Should().NotBeEmpty("the notes of {0} are read to check where they are played", exerciseName);
        notes.Any(note => OctaveOf(note) == MusicTheoryService.MinRangeOctave).Should().Be(
            MusicTheoryService.UsesNoteRange(exerciseName),
            "{0} must offer the octave range exactly when its notes come from it", exerciseName);
    }

    // The notes of a round: its strings, and the parts of its answer strings such as "A4:w|B4:w".
    private static IEnumerable<string> NotesIn(object round) =>
        ((JContainer)JToken.FromObject(round)).Descendants()
            .OfType<JValue>()
            .Where(value => value.Type == JTokenType.String)
            .SelectMany(value => value.ToString(CultureInfo.InvariantCulture).Split('|', ':'))
            .Where(part => Regex.IsMatch(part, @"^[A-G](?:##|#|bb|b)?\d$"));

    // GenerateNoteForExercise returns anonymous types, which are internal to the web assembly.
    private static T ReadProperty<T>(object source, string name) =>
        (T)source.GetType().GetProperty(name)!.GetValue(source)!;

    private static int OctaveOf(string note) =>
        int.Parse(Regex.Match(note, @"\d+$").Value, CultureInfo.InvariantCulture);
}
