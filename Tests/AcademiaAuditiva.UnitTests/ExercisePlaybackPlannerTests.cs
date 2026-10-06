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
}
