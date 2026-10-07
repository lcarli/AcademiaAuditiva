using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessMeter plays twelve beats at 120 BPM, the first of each bar accented, and asks whether
/// they group in twos, threes or fours. The clicks level clicks them on the piano, a higher C on
/// the accents; the accompaniment level plays an oom-pah in a major key drawn for the round: the
/// bass of each bar's chord on its first beat, then the chord, short, on each of the others.
/// </summary>
public class GuessMeterExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 999, Name = "GuessMeter" };

    private static readonly Dictionary<string, int> BeatsPerBar = new()
    {
        ["duple"] = 2,
        ["triple"] = 3,
        ["quadruple"] = 4,
    };

    // The roots of the bars' chords, in semitones up from the tonic: I–V–I in 4/4, I–IV–V–I in
    // 3/4 and I–IV–V–I–V–I in 2/4.
    private static readonly Dictionary<string, int[]> Progressions = new()
    {
        ["duple"] = [0, 5, 7, 0, 7, 0],
        ["triple"] = [0, 5, 7, 0],
        ["quadruple"] = [0, 7, 0],
    };

    [Fact]
    public void EveryMeter_IsAskedAtBothLevels_AndEachHasAButton()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-meter-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessMeter").AnswerButtonsJson!)!["guessAnswer"];

        buttons.Values.Should().BeEquivalentTo(BeatsPerBar.Keys);
        foreach (var level in new[] { "clicks", "accompaniment" })
        {
            var rounds = Enumerable.Range(0, 100).Select(_ => Round(level)).ToList();

            rounds.Should().AllSatisfy(round =>
            {
                round.Value<string>("level").Should().Be(level);
                round.Value<int>("beatsPerBar").Should().Be(BeatsPerBar[round.Value<string>("answer")!]);
            });
            rounds.Select(round => round.Value<string>("answer")).Distinct().Should().BeEquivalentTo(buttons.Values,
                "the student can give every answer, and every button can be right");
        }
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Guitar")]
    [InlineData("Violin")]
    public void TheClicks_AreTwelveBeatsOnThePiano_TheFirstOfEachBarAnOctaveHigher(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 60; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["gmLevel"] = "clicks" });

            // Like the count-in of a dictation: C7 on the accents, C6 on the other beats.
            var beatsPerBar = BeatsPerBar[JObject.Parse(plan.ExpectedAnswerJson).Value<string>("answer")!];
            plan.PlaybackPlans.Should().ContainSingle().Which.Should().Equal(Enumerable.Range(0, 12).Select(beat =>
                new MixInput(Instrument.Piano.SampleFor(beat % beatsPerBar == 0 ? "C7" : "C6"), beat * 0.5, 0.12)));
        }
    }

    [Fact]
    public void EachBar_IsAMajorChordOfTheKey_ThatChangesOnEveryFirstBeat()
    {
        var tonics = new HashSet<int>();

        for (var i = 0; i < 300; i++)
        {
            var round = Round("accompaniment");
            var answer = round.Value<string>("answer")!;
            var tonic = PitchClass(Midi(round.Value<string>("key")! + "4"));
            tonics.Add(tonic);
            var bars = round["bars"]!.ToList();

            (bars.Count * round.Value<int>("beatsPerBar")).Should().Be(12, "every meter lasts twelve beats");
            bars.Select(bar => PitchClass(Midi(bar.Value<string>("bass")!) - tonic)).Should().Equal(Progressions[answer]);
            bars.Zip(bars.Skip(1)).Should().AllSatisfy(pair =>
                pair.First.Value<string>("bass").Should().NotBe(pair.Second.Value<string>("bass"),
                    "the chord changes with the bar, so its first beat is heard"));
            foreach (var bar in bars)
            {
                var bass = Midi(bar.Value<string>("bass")!);
                bass.Should().BeInRange(Midi("F2"), Midi("E3"));
                Midis(bar["chord"]!).Should().Equal([bass + 12, bass + 16, bass + 19],
                    "the chord is a major triad in root position, an octave above the bass");
                bar["chord"]!.Values<string>().Append(bar.Value<string>("bass")).Should().OnlyContain(
                    note => Instrument.Guitar.Has(note!), "the guitar plays the accompaniment");
            }
        }

        tonics.Should().HaveCount(12, "every major key can be drawn");
    }

    // The violin plays no chords: the piano accompanies it.
    [Theory]
    [InlineData("Piano")]
    [InlineData("Violin")]
    public void ThePiano_PlaysTheBassOnTheFirstBeat_AndTheChordOnTheOthers(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 60; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["gmLevel"] = "accompaniment" });

            var round = JObject.Parse(plan.ExpectedAnswerJson);
            var beatsPerBar = round.Value<int>("beatsPerBar");
            var expected = round["bars"]!.SelectMany((bar, b) => Enumerable.Range(0, beatsPerBar).SelectMany(beat =>
            {
                var start = (b * beatsPerBar + beat) * 0.5;
                return beat == 0
                    ? [new MixInput(Instrument.Piano.SampleFor(bar.Value<string>("bass")!), start, 0.45)]
                    : bar["chord"]!.Values<string>().Select(note => new MixInput(Instrument.Piano.SampleFor(note!), start, 0.3));
            }));
            plan.PlaybackPlans.Should().ContainSingle().Which.Should().Equal(expected);
        }
    }

    [Fact]
    public void OnTheGuitar_TheBassIsPlucked_AndTheChordsAreStrummedAsWritten()
    {
        var planner = new ExercisePlaybackPlanner();

        for (var i = 0; i < 60; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = "Guitar", ["gmLevel"] = "accompaniment" });

            var round = JObject.Parse(plan.ExpectedAnswerJson);
            var beatsPerBar = round.Value<int>("beatsPerBar");
            var inputs = plan.PlaybackPlans.Should().ContainSingle().Subject;
            var bars = round["bars"]!.ToList();
            var next = 0;
            for (var b = 0; b < bars.Count; b++)
            {
                var bar = bars[b];
                var downbeat = b * beatsPerBar * 0.5;
                inputs[next++].Should().Be(new MixInput(Instrument.Guitar.SampleFor(bar.Value<string>("bass")!), downbeat, 0.45));
                for (var beat = 1; beat < beatsPerBar; beat++)
                {
                    // A string every 15 ms from the lowest note up, every string ringing until the chord ends.
                    var start = downbeat + beat * 0.5;
                    var chord = Midis(bar["chord"]!);
                    for (var k = 0; k < chord.Count; k++)
                    {
                        var input = inputs[next++];
                        input.SampleName.Should().Be(Instrument.Guitar.SampleName(chord[k]));
                        input.StartTimeSeconds.Should().BeApproximately(start + k * 0.015, 1e-9);
                        (input.StartTimeSeconds + input.DurationSeconds).Should().BeApproximately(start + 0.3, 1e-9);
                    }
                }
            }
            next.Should().Be(inputs.Count, "the accompaniment plays nothing else");
        }
    }

    // Beat 12 is never the first of a bar, so every meter ends on a weak beat at the same time.
    [Theory]
    [InlineData("clicks", "Piano", 5.62)]
    [InlineData("accompaniment", "Piano", 5.8)]
    [InlineData("accompaniment", "Guitar", 5.8)]
    [InlineData("accompaniment", "Violin", 5.8)]
    public void EveryMeter_StartsOnAnAccent_AndLastsAsLong(string level, string instrumentName, double end)
    {
        var planner = new ExercisePlaybackPlanner();
        var answers = new HashSet<string>();

        for (var i = 0; i < 60; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["gmLevel"] = level });
            answers.Add(JObject.Parse(plan.ExpectedAnswerJson).Value<string>("answer")!);

            var clip = plan.PlaybackPlans.Should().ContainSingle().Subject;
            clip.Min(input => input.StartTimeSeconds).Should().Be(0.0);
            clip.Max(input => input.StartTimeSeconds + input.DurationSeconds).Should().BeApproximately(end, 1e-9);
        }

        answers.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expert")]
    public void AnUnknownLevel_IsClicks(string? level)
    {
        var rounds = Enumerable.Range(0, 30).Select(_ => Round(level)).ToList();

        rounds.Should().AllSatisfy(round =>
        {
            round.Value<string>("level").Should().Be("clicks");
            round["bars"].Should().BeNull();
        });
    }

    private static JObject Round(string? level)
    {
        var filters = new Dictionary<string, string>();
        if (level is not null)
            filters["gmLevel"] = level;

        return JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters));
    }

    private static List<int> Midis(JToken notes) => [.. notes.Values<string>().Select(note => Midi(note!))];

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);

    private static int PitchClass(int semitones) => ((semitones % 12) + 12) % 12;
}
