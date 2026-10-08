using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessInterval and GuessFullInterval play their two notes one after the other (melodic, the
/// default), together (harmonic) or either, drawn for each round, as their <c>intervalMode</c>
/// filter says. Together, the notes start at once in the octaves written, for a whole note; the
/// guitar plucks them from the lower up, and the violin plays them as a double stop.
/// </summary>
public class HarmonicIntervalTests
{
    private static readonly ExercisePlaybackPlanner Planner = new();

    // A melodic interval: the second note starts after the first one's clip (1.5 s) and a gap (0.5 s).
    private const double SecondNoteStart = 2.0;

    // A harmonic interval rings for a whole note at 120 BPM, as a written chord does.
    private const double HarmonicSeconds = 2.0;

    public static TheoryData<string> IntervalExercises => ["GuessInterval", "GuessFullInterval"];

    [Theory]
    [InlineData("GuessInterval", null)]
    [InlineData("GuessInterval", "melodic")]
    [InlineData("GuessInterval", "unknown")]
    [InlineData("GuessFullInterval", null)]
    [InlineData("GuessFullInterval", "melodic")]
    [InlineData("GuessFullInterval", "unknown")]
    public void Melodic_PlaysTheSecondNoteAfterTheFirst(string exerciseName, string? mode)
    {
        for (var i = 0; i < 50; i++)
        {
            var (round, plan) = Play(exerciseName, Filters(mode));

            round.Value<bool>("harmonic").Should().BeFalse("without the filter, an interval stays melodic");
            plan.Should().Equal(
                new MixInput(Instrument.Piano.SampleFor(round.Value<string>("note1")!), 0.0, 1.5),
                new MixInput(Instrument.Piano.SampleFor(round.Value<string>("note2")!), SecondNoteStart, 1.5));
        }
    }

    [Theory]
    [MemberData(nameof(IntervalExercises))]
    public void Harmonic_PlaysBothNotesAtOnce_OnThePiano(string exerciseName)
    {
        for (var i = 0; i < 50; i++)
        {
            var (round, plan) = Play(exerciseName, Filters("harmonic"));

            round.Value<bool>("harmonic").Should().BeTrue();
            plan.Should().BeEquivalentTo(
                [
                    new MixInput(Instrument.Piano.SampleFor(round.Value<string>("note1")!), 0.0, HarmonicSeconds),
                    new MixInput(Instrument.Piano.SampleFor(round.Value<string>("note2")!), 0.0, HarmonicSeconds),
                ],
                options => options.WithoutStrictOrdering());
        }
    }

    [Theory]
    [MemberData(nameof(IntervalExercises))]
    public void Harmonic_OnTheGuitar_PlucksTheWrittenNotes_FromTheLowerUp(string exerciseName)
    {
        for (var i = 0; i < 50; i++)
        {
            var (round, plan) = Play(exerciseName, Filters("harmonic", "Guitar"));
            var notes = new[] { Midi(round, "note1"), Midi(round, "note2") }.Order().ToArray();

            plan.Should().Equal(
                new MixInput(Instrument.Guitar.SampleName(notes[0]), 0.0, HarmonicSeconds),
                new MixInput(Instrument.Guitar.SampleName(notes[1]), 0.015, HarmonicSeconds - 0.015));
        }
    }

    [Theory]
    [MemberData(nameof(IntervalExercises))]
    public void Harmonic_OnTheViolin_IsADoubleStop(string exerciseName)
    {
        var (round, plan) = Play(exerciseName, Filters("harmonic", "Violin"));

        plan.Should().HaveCount(2)
            .And.OnlyContain(input => input.StartTimeSeconds == 0.0 && input.DurationSeconds == HarmonicSeconds)
            .And.OnlyContain(input => input.SampleName.StartsWith("violin/"));
        plan.Select(input => input.SampleName).Should().BeEquivalentTo(
            Instrument.Violin.SampleFor(round.Value<string>("note1")!),
            Instrument.Violin.SampleFor(round.Value<string>("note2")!));
    }

    [Theory]
    [MemberData(nameof(IntervalExercises))]
    public void Both_DrawsTheModeOfEachRound(string exerciseName)
    {
        var modes = new HashSet<bool>();

        for (var i = 0; i < 200; i++)
        {
            var (round, plan) = Play(exerciseName, Filters("both"));
            var harmonic = round.Value<bool>("harmonic");
            modes.Add(harmonic);

            plan.Select(input => input.StartTimeSeconds).Should().Equal(harmonic ? [0.0, 0.0] : [0.0, SecondNoteStart]);
        }

        modes.Should().BeEquivalentTo([false, true], "both modes come up");
    }

    // Two notes played together have no direction: the note of the key is the lower one.
    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    [InlineData("both")]
    public void GuessFullInterval_Harmonic_PutsTheKeyBelow_WhateverTheDirection(string direction)
    {
        var filters = new Dictionary<string, string> { ["keySelect"] = "E", ["intervalDirection"] = direction, ["intervalMode"] = "harmonic" };

        for (var i = 0; i < 100; i++)
        {
            var (round, _) = Play("GuessFullInterval", filters);

            Midi(round, "note1").Should().Be(64, "the key's note is E4");
            Midi(round, "note2").Should().BeGreaterThan(64, round.ToString());
        }
    }

    [Fact]
    public void GuessFullInterval_Melodic_StillGoesDown_WhenAsked()
    {
        var filters = new Dictionary<string, string> { ["keySelect"] = "E", ["intervalDirection"] = "desc", ["intervalMode"] = "melodic" };

        for (var i = 0; i < 50; i++)
        {
            var (round, _) = Play("GuessFullInterval", filters);

            Midi(round, "note2").Should().BeLessThan(Midi(round, "note1"), round.ToString());
        }
    }

    [Fact]
    public void HigherOrLower_StaysMelodic()
    {
        var (round, plan) = Play("HigherOrLower", Filters("harmonic"));

        round["harmonic"].Should().BeNull();
        plan.Select(input => input.StartTimeSeconds).Should().Equal(0.0, SecondNoteStart);
    }

    private static Dictionary<string, string> Filters(string? mode, string instrument = "Piano")
    {
        var filters = new Dictionary<string, string> { ["instrument"] = instrument };
        if (mode is not null)
            filters["intervalMode"] = mode;
        return filters;
    }

    private static (JObject Round, IReadOnlyList<MixInput> Plan) Play(string exerciseName, Dictionary<string, string> filters)
    {
        var plan = Planner.Plan(new Exercise { ExerciseId = 997, Name = exerciseName }, filters);
        plan.PlaybackPlans.Should().ContainSingle();
        return (JObject.Parse(plan.ExpectedAnswerJson), plan.PlaybackPlans[0]);
    }

    private static int Midi(JObject round, string field) =>
        MusicTheoryService.NoteToMidi(round.Value<string>(field)!)!.Value;
}
