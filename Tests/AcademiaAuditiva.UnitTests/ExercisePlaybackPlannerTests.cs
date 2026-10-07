using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="ExercisePlaybackPlanner"/>, which turns a generated
/// question into the audio clips the server mixes for the player.
/// </summary>
public class ExercisePlaybackPlannerTests
{
    private readonly ExercisePlaybackPlanner _planner = new();

    [Fact]
    public void IntervalMelodico_PlaysTheMelodyEvenly_InOneClip()
    {
        var plan = _planner.Plan(
            new Exercise { Name = "IntervalMelodico" },
            new Dictionary<string, string> { ["keySelect"] = "F#", ["scaleTypeSelect"] = "minor" });

        var melody = JObject.Parse(plan.ExpectedAnswerJson)["melody"]!.Values<string>().ToList();
        var inputs = plan.PlaybackPlans.Should().ContainSingle().Which;

        inputs.Should().HaveCount(melody.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            inputs[i].SampleName.Should().Be(melody[i]!.Replace("#", "s") + ".mp3");
            inputs[i].StartTimeSeconds.Should().BeApproximately(i * 0.6, 1e-9);
            inputs[i].DurationSeconds.Should().Be(0.8);
        }
        (inputs[^1].StartTimeSeconds + 0.8).Should().BeLessThan(30, "the mixer refuses mixes longer than 30 seconds");
    }

    [Fact]
    public void SolfegeMelody_HasNoAudio_BecauseTheMelodyIsShownAsSheetMusic()
    {
        var plan = _planner.Plan(new Exercise { Name = "SolfegeMelody" }, new Dictionary<string, string>());

        plan.PlaybackPlans.Should().BeEmpty();
        JObject.Parse(plan.ExpectedAnswerJson)["melody"].Should().NotBeNull();
    }

    [Fact]
    public void SolfegeMelody_StartingNote_IsTheFirstNoteOfTheMelody_OnThePiano()
    {
        // The student's instrument is left out: the starting note is always the piano's.
        var filters = new Dictionary<string, string> { ["instrument"] = "Guitar" };
        for (var i = 0; i < 50; i++)
        {
            var plan = _planner.Plan(new Exercise { Name = "SolfegeMelody" }, filters);
            var first = JObject.Parse(plan.ExpectedAnswerJson)["melody"]!
                .First(item => (string?)item["type"] == "note")["note"]!.Value<string>()!;

            _planner.StartingNote(plan.ExpectedAnswerJson).Should().Equal(new MixInput(first.Replace("#", "s") + ".mp3", 0, 1.5));
        }
    }

    [Fact]
    public void StartingNote_SkipsLeadingRests()
    {
        const string melody = """
            {"melody":[{"type":"rest","note":"rest","duration":1},{"type":"note","note":"F#4","duration":1},{"type":"note","note":"A4","duration":2}]}
            """;

        _planner.StartingNote(melody).Should().Equal(new MixInput("Fs4.mp3", 0, 1.5));
    }

    [Theory]
    [InlineData("""{"melody":[]}""")]
    [InlineData("""{"melody":[{"type":"rest","note":"rest","duration":4}]}""")]
    [InlineData("""{"note":"C4"}""")]
    public void StartingNote_OfAMelodyWithoutNotes_IsAnError(string expectedAnswerJson)
    {
        var act = () => _planner.StartingNote(expectedAnswerJson);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'melody'*");
    }

    [Theory]
    [InlineData("RhythmDictation", "4/4", "x...", 1.0)]
    [InlineData("RhythmDictation", "3/4", "x..", 1.0)]
    [InlineData("RhythmDictation", "2/4", "x.x.", 1.0)]
    [InlineData("RhythmDictation", "6/8", "x..x..", 0.5)]
    [InlineData("GuessRhythmPattern", "4/4", "x...", 1.0)]
    [InlineData("GuessRhythmPattern", "3/4", "x..", 1.0)]
    [InlineData("GuessRhythmPattern", "2/4", "x.x.", 1.0)]
    [InlineData("GuessRhythmPattern", "6/8", "x..x..", 0.5)]
    [InlineData("MelodicDictation", "4/4", "x...", 1.0)]
    [InlineData("MelodicDictation", "3/4", "x..", 1.0)]
    [InlineData("MelodicDictation", "2/4", "x.x.", 1.0)]
    [InlineData("MelodicDictation", "6/8", "x..x..", 0.5)]
    public void Dictation_CountsTheStudentInOnThePiano_ThenPlaysOnTheirInstrument(
        string exerciseName, string timeSignature, string accents, double clickBeats)
    {
        var filters = new Dictionary<string, string>
        {
            ["instrument"] = "Violin",
            ["mdRoot"] = "D",
            ["mdLevel"] = "4",
            ["rdLevel"] = timeSignature == "6/8" ? "8" : "5",
            ["grpLevel"] = timeSignature == "6/8" ? "8" : "5",
        };

        var plan = PlanIn(exerciseName, timeSignature, filters);

        // The rhythm is counted in on C and the melody on its tonic, an octave higher on the
        // first beat of a bar, at 120 quarter notes a minute.
        var pitch = exerciseName == "MelodicDictation" ? "D" : "C";
        var inputs = plan.PlaybackPlans.Should().ContainSingle().Subject;
        inputs.Take(accents.Length).Should().Equal(accents.Select((accent, i) =>
            new MixInput(Instrument.Piano.SampleFor(pitch + (accent == 'x' ? "7" : "6")), i * clickBeats * 0.5, 0.12)));

        // The melody starts on the beat after the count-in.
        var melody = new List<MixInput>();
        var t = accents.Length * clickBeats * 0.5;
        foreach (var entry in JObject.Parse(plan.ExpectedAnswerJson)["melody"]!)
        {
            var seconds = entry.Value<double>("durationBeats") * 0.5;
            if (entry.Value<string>("type") == "note")
            {
                melody.Add(new MixInput(Instrument.Violin.SampleFor(entry.Value<string>("note")!), t, seconds));
            }
            t += seconds;
        }
        inputs.Skip(accents.Length).Should().Equal(melody);
    }

    [Theory]
    [InlineData("RhythmDictation", "rdTempo", "60", 1.0)]
    [InlineData("RhythmDictation", "rdTempo", "90", 2.0 / 3)]
    [InlineData("RhythmDictation", "rdTempo", "120", 0.5)]
    [InlineData("GuessRhythmPattern", "grpTempo", "60", 1.0)]
    [InlineData("GuessRhythmPattern", "grpTempo", "90", 2.0 / 3)]
    [InlineData("GuessRhythmPattern", "grpTempo", "120", 0.5)]
    [InlineData("MelodicDictation", "mdTempo", "60", 1.0)]
    [InlineData("MelodicDictation", "mdTempo", "90", 2.0 / 3)]
    public void Dictation_IsPlayedAtTheTempoTheStudentPicked(string exerciseName, string filter, string tempo, double beatSeconds)
    {
        // Level 1 is in 4/4, without rests: four clicks, then a note on each value.
        var filters = new Dictionary<string, string>
        {
            [filter] = tempo, ["mdLevel"] = "1", ["rdLevel"] = "1", ["grpLevel"] = "1",
        };

        var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = exerciseName }, filters);

        var melody = JObject.Parse(plan.ExpectedAnswerJson)["melody"]!.ToList();
        var inputs = plan.PlaybackPlans.Should().ContainSingle().Subject;
        inputs.Should().HaveCount(4 + melody.Count);
        for (var i = 0; i < 4; i++)
        {
            inputs[i].StartTimeSeconds.Should().BeApproximately(i * beatSeconds, 1e-9);
            inputs[i].DurationSeconds.Should().Be(0.12, "a click is short, however slow the tempo");
        }
        var t = 4 * beatSeconds;
        for (var k = 0; k < melody.Count; k++)
        {
            var seconds = melody[k].Value<double>("durationBeats") * beatSeconds;
            inputs[4 + k].StartTimeSeconds.Should().BeApproximately(t, 1e-9);
            inputs[4 + k].DurationSeconds.Should().BeApproximately(seconds, 1e-9);
            t += seconds;
        }
    }

    [Theory]
    [InlineData("MelodicDictation", "md", "1,3,4")]
    [InlineData("RhythmDictation", "rd", "1,3,4,5,6,7,8")]
    [InlineData("GuessRhythmPattern", "grp", "1,3,4,5,6,7,8")]
    public void Dictation_AtTheSlowestTempo_FitsInAMix(string exerciseName, string prefix, string levels)
    {
        foreach (var level in levels.Split(','))
        {
            for (var round = 0; round < 20; round++)
            {
                var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = exerciseName }, new Dictionary<string, string>
                {
                    [prefix + "Level"] = level, [prefix + "Measures"] = "long", [prefix + "Tempo"] = "60",
                });

                plan.PlaybackPlans.Should().ContainSingle().Which.Max(input => input.StartTimeSeconds + input.DurationSeconds)
                    .Should().BeLessThanOrEqualTo(30, "the mixer refuses mixes longer than 30 seconds");
            }
        }
    }

    // A round in the time signature: the levels draw theirs at random.
    private ExercisePlan PlanIn(string exerciseName, string timeSignature, Dictionary<string, string> filters)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var plan = _planner.Plan(new Exercise { ExerciseId = 1, Name = exerciseName }, filters);
            if (JObject.Parse(plan.ExpectedAnswerJson).Value<string>("timeSignature") == timeSignature)
            {
                return plan;
            }
        }
        throw new InvalidOperationException($"{exerciseName} drew no round in {timeSignature}.");
    }
}
