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
        var durations = new HashSet<string> { "w", "h", "q", "8", "wr", "hr", "qr", "8r" };
        foreach (var token in answerString.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "bar" or "barline")
            {
                continue;
            }

            durations.Should().Contain(token, $"duration '{token}' must be available in the rhythm editor");
        }
    }

    // The sixteenths of each note value, the unit staff-editor.js counts a measure in.
    private static readonly Dictionary<string, int> Sixteenths = new()
    {
        { "w", 16 }, { "h", 8 }, { "q", 4 }, { "8", 2 },
    };

    /// <summary>
    /// Rebuilds a dictation answer the way staff-editor.js writes it: a measure holds
    /// numerator × 16 / denominator sixteenths, and a note that comes once it is full starts the
    /// next one, after a "bar". The round's answer must be exactly that, with every measure full,
    /// or a student who wrote what they heard would be marked wrong.
    /// </summary>
    private static void AssertEditorWritesTheAnswer(JObject json, bool rhythm)
    {
        var timeSignature = json.Value<string>("timeSignature")!.Split('/');
        var capacity = int.Parse(timeSignature[0]) * 16 / int.Parse(timeSignature[1]);
        var answer = json.Value<string>("answerString")!.Split('|');

        // The melody's first note is given on the staff.
        var used = rhythm ? 0 : Sixteenths[json.Value<string>("firstDuration")!];
        var measure = 1;
        var written = new List<string>();
        foreach (var token in answer.Where(t => t != "bar"))
        {
            if (used >= capacity)
            {
                measure++;
                used = 0;
                written.Add("bar");
            }

            written.Add(token);
            used += Sixteenths[(rhythm ? token : token.Split(':')[1]).TrimEnd('r')];
            used.Should().BeLessThanOrEqualTo(capacity, $"'{token}' must fit in measure {measure} of {json}");
        }

        used.Should().Be(capacity, $"the last measure of {json} is full");
        measure.Should().Be(json.Value<int>("numMeasures"));
        written.Should().Equal(answer, "the editor writes \"bar\" before each note that starts a measure");
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
            AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: 4);
        }
    }

    [Fact]
    public void CompleteChord_InvalidOctave_FallsBackToSampleSafeRound()
    {
        var json = GenerateJson("CompleteChord", new() { { "ccQuality", "x" }, { "ccOctave", "5" } });

        json["error"].Should().BeNull();
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: 4);
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
        foreach (var root in new[] { "any", "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B", "Db", "Gb" })
        foreach (var scale in new[] { "major", "minor" })
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var json = GenerateJson("TransposeScale", new() { { "tsRoot", root }, { "tsScale", scale } });

            json["error"].Should().BeNull($"filters {root}/{scale} should be supported");
            AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "q" }, totalSlots: 8);
        }
    }

    [Fact]
    public void TransposeScale_FinalSharpTarget_IsEnterableBecauseAccidentalsRemainAvailableAfterFinalSlot()
    {
        JObject? sharpTarget = null;
        for (var attempt = 0; attempt < 500 && sharpTarget is null; attempt++)
        {
            var json = GenerateJson("TransposeScale", new() { { "tsRoot", "C" }, { "tsScale", "major" } });
            if (json.Value<string>("targetRoot")!.Contains('#'))
            {
                sharpTarget = json;
            }
        }

        sharpTarget.Should().NotBeNull("random transposition should produce a sharp target within many rounds");
        var answer = sharpTarget!.Value<string>("answerString")!;
        answer.Split('|').Last().Should().Contain("#5");

        var oldEditorCheck = () => AssertEditorCanEnter(
            answer,
            new HashSet<string> { "q" },
            totalSlots: 8,
            accidentalsAvailableAfterFinalSlot: false);
        oldEditorCheck.Should().Throw<Exception>("the old palette hid accidentals after the 8th TransposeScale note");
        AssertEditorCanEnter(answer, new HashSet<string> { "q" }, totalSlots: 8);
    }

    [Fact]
    public void TransposeScale_TargetSharpKeys_UseTextbookSpelling()
    {
        JObject? fSharpTarget = null;
        for (var attempt = 0; attempt < 500 && fSharpTarget is null; attempt++)
        {
            var json = GenerateJson("TransposeScale", new() { { "tsRoot", "C" }, { "tsScale", "major" }, { "tsOctave", "4" } });
            if (json.Value<string>("targetRoot") == "F#")
            {
                fSharpTarget = json;
            }
        }

        fSharpTarget.Should().NotBeNull("random target keys should include F# within many rounds");
        fSharpTarget!.Value<string>("answerString")!
            .Should().Contain("E#5:q", "F# major's leading tone is spelled E#, not F");
    }

    [Fact]
    public void TransposeScale_InvalidFilters_FallBackToValidRound()
    {
        var json = GenerateJson("TransposeScale", new() { { "tsRoot", "H" }, { "tsScale", "x" }, { "tsOctave", "9" } });

        json["error"].Should().BeNull();
        json.Value<string>("scale").Should().Be("major");
        json.Value<int>("octave").Should().Be(4);
        AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "q" }, totalSlots: 8);
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
            AssertEditorCanEnter(
                json.Value<string>("answerString")!,
                new HashSet<string> { "w", "h", "q", "8", "wr", "hr", "qr", "8r" },
                totalSlots: 64);
        }
    }

    [Fact]
    public void MelodicDictation_FirstNoteIsGiven_AndOmittedFromSubmittedAnswer()
    {
        for (var round = 0; round < 20; round++)
        {
            var json = GenerateJson("MelodicDictation", new() { { "mdRoot", "C" }, { "mdScale", "major" }, { "mdLevel", "1" }, { "mdMeasures", "short" } });
            var melody = (JArray)json["melody"]!;

            json.Value<string>("firstNote").Should().Be(melody[0]!.Value<string>("note"));
            json.Value<string>("firstDuration").Should().Be(melody[0]!.Value<string>("durationLabel"));

            // Compare by position: the melody may legitimately repeat the given first note.
            var answerTokens = json.Value<string>("answerString")!.Split('|', StringSplitOptions.RemoveEmptyEntries);
            answerTokens.Where(t => t != "bar").Should().Equal(
                melody.Skip(1).Select(e => $"{e.Value<string>("note")}:{e.Value<string>("durationLabel")}"));
            answerTokens.Count(t => t == "bar").Should().Be(json.Value<int>("numMeasures") - 1);
        }
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
        AssertEditorCanEnter(
            json.Value<string>("answerString")!,
            new HashSet<string> { "w", "h", "q", "8", "wr", "hr", "qr", "8r" },
            totalSlots: 64);
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

    [Theory]
    [InlineData("MelodicDictation", "mdLevel", "1", "w,h", false)]
    [InlineData("MelodicDictation", "mdLevel", "3", "w,h,q", true)]
    [InlineData("MelodicDictation", "mdLevel", "4", "w,h,q,8", true)]
    [InlineData("RhythmDictation", "rdLevel", "1", "w,h", false)]
    [InlineData("RhythmDictation", "rdLevel", "3", "w,h,q", true)]
    [InlineData("RhythmDictation", "rdLevel", "4", "w,h,q,8", true)]
    public void Dictation_TellsTheEditorTheNoteValuesOfItsLevel(
        string exercise, string levelFilter, string level, string durations, bool rests)
    {
        var json = GenerateJson(exercise, new() { { levelFilter, level } });

        json["durations"]!.Values<string>().Should().Equal(durations.Split(','));
        json.Value<bool>("rests").Should().Be(rests);
    }

    [Theory]
    [InlineData("MelodicDictation", "md")]
    [InlineData("RhythmDictation", "rd")]
    public void Dictation_AnswerIsWhatTheEditorWrites(string exercise, string prefix)
    {
        var rhythm = exercise == "RhythmDictation";
        foreach (var level in new[] { "1", "3", "4" })
        foreach (var length in new[] { "short", "long" })
        {
            for (var round = 0; round < 25; round++)
            {
                var json = GenerateJson(exercise, new() { { prefix + "Level", level }, { prefix + "Measures", length } });
                var offered = json["durations"]!.Values<string>().ToHashSet();
                var rests = json.Value<bool>("rests");

                if (!rhythm)
                {
                    offered.Should().Contain(json.Value<string>("firstDuration"));
                }

                foreach (var token in json.Value<string>("answerString")!.Split('|').Where(t => t != "bar"))
                {
                    var duration = rhythm ? token : token.Split(':')[1];
                    offered.Should().Contain(duration.TrimEnd('r'), $"the editor offers only the note values of level {level}");
                    if (duration.EndsWith('r'))
                    {
                        rests.Should().BeTrue($"the editor offers rests from level 3, not in level {level}");
                    }
                }

                AssertEditorWritesTheAnswer(json, rhythm);
            }
        }
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
