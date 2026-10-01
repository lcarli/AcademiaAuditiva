using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Smoke coverage for the 5 staff-based exercise generators ported in
/// Phase 0. Verifies each generator returns the unified shape expected
/// by ExercisePlaybackPlanner (a `melody` array with `durationBeats`
/// numeric values + a pipe-separated `answerString`).
/// </summary>
public class StaffExerciseGeneratorTests
{
    private static Exercise NewExercise(string name) => new() { ExerciseId = 900, Name = name };

    private static JObject GenerateJson(string name, Dictionary<string, string>? filters = null)
    {
        var raw = MusicTheoryService.GenerateNoteForExercise(NewExercise(name), filters ?? new());
        return JObject.FromObject(raw);
    }

    private static void AssertEditorCanEnter(
        string answerString,
        IReadOnlySet<string> allowedDurations,
        int totalSlots,
        int minOctave = 3,
        int maxOctave = 6,
        bool accidentalsAvailableAfterFinalSlot = true)
    {
        var tokens = answerString.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var placedNotes = 0;
        foreach (var token in tokens)
        {
            if (token is "bar" or "barline")
            {
                continue;
            }

            var parts = token.Split(':');
            parts.Length.Should().Be(2, $"token '{token}' must be note:duration");
            allowedDurations.Should().Contain(parts[1], $"duration '{parts[1]}' must be available in the editor");
            if (parts[0] == "rest" || parts[1].EndsWith('r'))
            {
                continue;
            }

            placedNotes++;
            placedNotes.Should().BeLessThanOrEqualTo(totalSlots, "the editor enforces totalSlots before adding a note");
            var octave = int.Parse(parts[0][^1].ToString());
            octave.Should().BeInRange(minOctave, maxOctave, $"note '{parts[0]}' must be within editor octave controls");
            var pitch = parts[0].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            pitch.Should().MatchRegex("^[A-G](#{0,2}|b{0,2})$");
            if (pitch.Contains('#') || pitch.Contains('b'))
            {
                (placedNotes < totalSlots || accidentalsAvailableAfterFinalSlot).Should().BeTrue(
                    $"the editor must expose accidentals after placing '{parts[0]}'");
            }
        }
    }

    private static void AssertRhythmEditorCanEnter(string answerString)
    {
        var durations = new HashSet<string> { "w", "h", "q", "8", "16", "wr", "hr", "qr", "8r", "16r" };
        foreach (var token in answerString.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "bar" or "barline")
            {
                continue;
            }

            durations.Should().Contain(token, $"duration '{token}' must be available in the rhythm editor");
        }
    }

    [Fact]
    public void CompleteScale_ProducesMelodyWithRootAndAnswerWithRest()
    {
        var json = GenerateJson("CompleteScale", new() { { "csRoot", "C" }, { "csScale", "major" }, { "csOctave", "4" } });

        var melody = (JArray)json["melody"]!;
        melody.Count.Should().Be(1, "audio plays only the tonic");
        melody[0].Value<string>("note").Should().Be("C4");
        melody[0].Value<double>("durationBeats").Should().Be(4.0);

        var answer = json.Value<string>("answerString")!;
        answer.Should().Be("D4:w|E4:w|F4:w|G4:w|A4:w|B4:w|C5:w");
    }

    [Fact]
    public void CompleteScale_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        var roots = new[] { "any", "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B", "Db", "Gb" };
        var scales = new[] { "all", "major", "minor", "majorPentatonic", "minorPentatonic" };
        var octaves = new[] { "3", "4" };

        foreach (var root in roots)
        foreach (var scale in scales)
        foreach (var octave in octaves)
        {
            var json = GenerateJson("CompleteScale", new() { { "csRoot", root }, { "csScale", scale }, { "csOctave", octave } });
            json["error"].Should().BeNull($"filters {root}/{scale}/{octave} should be supported");
            AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: 10);
        }
    }

    [Fact]
    public void CompleteScale_InvalidFilters_FallBackToValidRound()
    {
        var json = GenerateJson("CompleteScale", new()
        {
            { "csRoot", "H" },
            { "csScale", "x" },
            { "csOctave", "9" }
        });

        json["error"].Should().BeNull();
        json.Value<string>("root").Should().NotBe("H");
        json.Value<string>("scale").Should().BeOneOf("major", "minor", "majorPentatonic", "minorPentatonic");
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: 10);
    }

    [Fact]
    public void CompleteChord_ProducesPromptRoot_AndTwoChordTonesInAnswer()
    {
        var json = GenerateJson("CompleteChord", new() { { "ccQuality", "major" }, { "ccOctave", "4" } });

        json["promptNotes"].Should().NotBeNull();
        var melody = (JArray)json["melody"]!;
        melody.Count.Should().Be(1);

        var answer = json.Value<string>("answerString")!;
        answer.Split('|').Length.Should().Be(2, "the user must complete the 3rd and 5th of a triad");
    }

    [Fact]
    public void CompleteChord_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        foreach (var quality in new[] { "major", "minor", "both" })
        foreach (var octave in new[] { "3", "4" })
        {
            var json = GenerateJson("CompleteChord", new() { { "ccQuality", quality }, { "ccOctave", octave } });

            json["error"].Should().BeNull($"filters {quality}/{octave} should be supported");
            json.Value<string>("quality").Should().BeOneOf("major", "minor");
            AssertEditorCanEnter(json.Value<string>("answerString")!);
        }
    }

    [Fact]
    public void CompleteChord_InvalidOctave_FallsBackToSampleSafeRound()
    {
        var json = GenerateJson("CompleteChord", new() { { "ccQuality", "x" }, { "ccOctave", "5" } });

        json["error"].Should().BeNull();
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!);
    }

    [Fact]
    public void TransposeScale_ProducesFullOriginalScaleAudio_AndDifferentTargetRoot()
    {
        var json = GenerateJson("TransposeScale", new() { { "tsRoot", "C" }, { "tsScale", "major" }, { "tsOctave", "4" } });

        json.Value<string>("originalRoot").Should().Be("C");
        json.Value<string>("targetRoot").Should().NotBe("C");

        var melody = (JArray)json["melody"]!;
        melody.Count.Should().BeGreaterThan(1, "audio plays the entire original scale");
        var answer = json.Value<string>("answerString")!;
        answer.Should().NotBeEmpty();
    }

    [Fact]
    public void TransposeScale_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        foreach (var root in new[] { "any", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" })
        foreach (var scale in new[] { "major", "minor" })
        {
            var json = GenerateJson("TransposeScale", new() { { "tsRoot", root }, { "tsScale", scale } });

            json["error"].Should().BeNull($"filters {root}/{scale} should be supported");
            AssertEditorCanEnter(json.Value<string>("answerString")!);
        }
    }

    [Fact]
    public void TransposeScale_InvalidFilters_FallBackToValidRound()
    {
        var json = GenerateJson("TransposeScale", new() { { "tsRoot", "Db" }, { "tsScale", "x" }, { "tsOctave", "9" } });

        json["error"].Should().BeNull();
        json.Value<string>("scale").Should().Be("major");
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!);
    }

    [Fact]
    public void MelodicDictation_RespectsMeasureCountForLevelOne()
    {
        var json = GenerateJson("MelodicDictation", new()
        {
            { "mdRoot", "C" }, { "mdScale", "major" }, { "mdLevel", "1" }, { "mdMeasures", "short" }
        });

        json.Value<int>("numMeasures").Should().Be(2);
        json.Value<int>("level").Should().Be(1);
        var melody = (JArray)json["melody"]!;
        melody.Count.Should().BeGreaterThan(0);

        // Every entry has a numeric durationBeats so MelodyPlan can work.
        foreach (var e in melody)
        {
            e["durationBeats"].Should().NotBeNull();
            e.Value<double>("durationBeats").Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void MelodicDictation_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        foreach (var level in new[] { "1", "3", "4" })
        foreach (var measures in new[] { "short", "long" })
        {
            var json = GenerateJson("MelodicDictation", new() { { "mdLevel", level }, { "mdMeasures", measures } });

            json["error"].Should().BeNull($"filters {level}/{measures} should be supported");
            json.Value<string>("firstNote").Should().NotBeNullOrWhiteSpace();
            json.Value<string>("firstDuration").Should().NotBeNullOrWhiteSpace();
            AssertEditorCanEnter(json.Value<string>("answerString")!);
        }
    }

    [Fact]
    public void MelodicDictation_FirstNoteIsGiven_AndOmittedFromSubmittedAnswer()
    {
        var json = GenerateJson("MelodicDictation", new() { { "mdRoot", "C" }, { "mdScale", "major" }, { "mdLevel", "1" }, { "mdMeasures", "short" } });
        var melody = (JArray)json["melody"]!;
        var firstToken = $"{melody[0]!.Value<string>("note")}:{melody[0]!.Value<string>("durationLabel")}";

        json.Value<string>("firstNote").Should().Be(melody[0]!.Value<string>("note"));
        json.Value<string>("answerString")!.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            .Should().NotBe(firstToken);
    }

    [Fact]
    public void MelodicDictation_InvalidFilters_FallBackToEnterableRound()
    {
        var json = GenerateJson("MelodicDictation", new()
        {
            { "mdRoot", "Db" },
            { "mdScale", "x" },
            { "mdOctave", "9" },
            { "mdLevel", "5" },
            { "mdMeasures", "huge" }
        });

        json["error"].Should().BeNull();
        json.Value<int>("level").Should().Be(1);
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!);
    }

    [Fact]
    public void RhythmDictation_UsesSinglePitchAndRhythmOnlyAnswer()
    {
        var json = GenerateJson("RhythmDictation", new() { { "rdLevel", "1" } });

        var melody = (JArray)json["melody"]!;
        foreach (var e in melody)
        {
            e.Value<string>("note").Should().Be("C5", "rhythm dictation uses a fixed pitch");
        }

        var answer = json.Value<string>("answerString")!;
        answer.Should().NotContain(":", "rhythm answer encodes durations only");
    }

    [Fact]
    public void RhythmDictation_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        foreach (var level in new[] { "1", "3", "4" })
        foreach (var measures in new[] { "short", "long" })
        {
            var json = GenerateJson("RhythmDictation", new() { { "rdLevel", level }, { "rdMeasures", measures } });

            json["error"].Should().BeNull($"filters {level}/{measures} should be supported");
            AssertRhythmEditorCanEnter(json.Value<string>("answerString")!);
        }
    }

    [Fact]
    public void RhythmDictation_InvalidFilters_FallBackToEnterableRound()
    {
        var json = GenerateJson("RhythmDictation", new() { { "rdLevel", "5" }, { "rdMeasures", "huge" } });

        json["error"].Should().BeNull();
        json.Value<int>("level").Should().Be(1);
        AssertRhythmEditorCanEnter(json.Value<string>("answerString")!);
    }

    [Fact]
    public void StaffPlanner_TranslatesMelodyToMixInputsWithProperTiming()
    {
        var planner = new ExercisePlaybackPlanner();
        var plan = planner.Plan(NewExercise("CompleteScale"),
            new() { { "csRoot", "C" }, { "csScale", "major" }, { "csOctave", "4" } });

        plan.PlaybackPlans.Should().HaveCount(1);
        plan.PlaybackPlans[0].Should().HaveCount(1, "audio plays only the tonic for CompleteScale");
        plan.PlaybackPlans[0][0].StartTimeSeconds.Should().Be(0.0);
    }
}
