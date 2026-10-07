using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Smoke coverage for the 5 staff-based exercise generators ported in
/// Phase 0. Verifies each generator returns the shape expected by
/// ExercisePlaybackPlanner (a `melody` array with `durationBeats` numeric
/// values, or the `chordNotes` of CompleteChord) + a pipe-separated
/// `answerString` the staff editor can write.
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

    private static void AssertRhythmEditorCanEnter(JObject json)
    {
        var offered = Offered(json);
        foreach (var token in json.Value<string>("answerString")!.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "bar" or "barline")
            {
                continue;
            }

            offered.Should().Contain(token, $"duration '{token}' must be available in the rhythm editor");
        }
    }

    // The figures staff-editor.js has a button for (FIGURE_LABELS).
    private static readonly HashSet<string> EditorFigures =
    [
        "w", "h.", "h", "q.", "q", "8.", "8", "16", "wr", "hr", "q.r", "qr", "8r",
    ];

    /// <summary>The figures the editor offers for a dictation round: its note values and its rests.</summary>
    private static HashSet<string> Offered(JObject json)
    {
        var offered = json["durations"]!.Values<string>().Concat(json["restDurations"]!.Values<string>())
            .Select(figure => figure!)
            .ToHashSet();
        offered.Should().BeSubsetOf(EditorFigures, "the editor has a button for each figure of {0}", json);
        return offered;
    }

    // The sixteenths of each note value, the unit staff-editor.js counts a measure in.
    private static readonly Dictionary<string, int> Sixteenths = new()
    {
        { "w", 16 }, { "h.", 12 }, { "h", 8 }, { "q.", 6 }, { "q", 4 }, { "8.", 3 }, { "8", 2 }, { "16", 1 },
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
    public void CompleteChord_PlaysAChord_AndAsksForTheNotesAboveItsGivenRoot()
    {
        var json = GenerateJson("CompleteChord", new() { { "ccQuality", "major" }, { "ccOctave", "4" } });

        json["melody"].Should().BeNull("the chord is played as written, all its notes together");
        var notes = json["chordNotes"]!.Values<string>().ToList();
        notes.Should().HaveCount(3);
        notes[0].Should().BeOneOf("C4", "F4", "G4");
        json["promptNotes"]!.Values<string>().Should().Equal(notes[0]);
        json.Value<string>("answerString").Should().Be($"{notes[1]}:w|{notes[2]}:w",
            "the student writes the third and the fifth over the given root");
        json.Value<int>("slots").Should().Be(2);
    }

    [Fact]
    public void CompleteChord_OfferedFilters_AlwaysProduceEditorEnterableAnswers()
    {
        foreach (var quality in new[] { "major", "minor", "both", "triads", "sevenths", "all" })
        foreach (var accidentals in new[] { "none", "any" })
        foreach (var root in new[] { "given", "hidden" })
        foreach (var octave in new[] { "3", "4" })
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var json = GenerateJson("CompleteChord", new()
            {
                { "ccQuality", quality }, { "ccAccidentals", accidentals }, { "ccRoot", root }, { "ccOctave", octave },
            });

            json["error"].Should().BeNull($"filters {quality}/{accidentals}/{root}/{octave} should be supported");
            AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: json.Value<int>("slots"));
        }
    }

    [Fact]
    public void CompleteChord_InvalidFilters_FallBackToSampleSafeRound()
    {
        var json = GenerateJson("CompleteChord", new() { { "ccQuality", "x" }, { "ccOctave", "5" } });

        json["error"].Should().BeNull();
        json.Value<int>("octave").Should().Be(4);
        json.Value<string>("clef").Should().Be("treble");
        AssertEditorCanEnter(json.Value<string>("answerString")!, new HashSet<string> { "w" }, totalSlots: json.Value<int>("slots"));
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
            AssertEditorCanEnter(json.Value<string>("answerString")!, Offered(json), totalSlots: 64);
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
            { "mdMeasures", "huge" },
            { "mdTempo", "45" }
        });

        json["error"].Should().BeNull();
        json.Value<int>("level").Should().Be(1);
        json.Value<int>("octave").Should().Be(4);
        json.Value<int>("tempo").Should().Be(120);
        AssertEditorCanEnter(json.Value<string>("answerString")!, Offered(json), totalSlots: 64);
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
        foreach (var level in new[] { "1", "3", "4", "5", "6", "7", "8" })
        foreach (var measures in new[] { "short", "long" })
        {
            var json = GenerateJson("RhythmDictation", new() { { "rdLevel", level }, { "rdMeasures", measures } });

            json["error"].Should().BeNull($"filters {level}/{measures} should be supported");
            AssertRhythmEditorCanEnter(json);
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("9")]
    [InlineData("five")]
    public void RhythmDictation_InvalidFilters_FallBackToEnterableRound(string level)
    {
        var json = GenerateJson("RhythmDictation", new() { { "rdLevel", level }, { "rdMeasures", "huge" }, { "rdTempo", "fast" } });

        json["error"].Should().BeNull();
        json.Value<int>("level").Should().Be(1);
        json.Value<int>("tempo").Should().Be(120);
        AssertRhythmEditorCanEnter(json);
    }

    [Theory]
    [InlineData("MelodicDictation", "mdLevel", "1", "4/4", "w,h", "")]
    [InlineData("MelodicDictation", "mdLevel", "3", "4/4,3/4", "w,h,q", "wr,hr,qr")]
    [InlineData("MelodicDictation", "mdLevel", "4", "4/4,3/4,2/4,6/8", "w,h,q,8", "wr,hr,qr,8r")]
    [InlineData("RhythmDictation", "rdLevel", "1", "4/4", "w,h", "")]
    [InlineData("RhythmDictation", "rdLevel", "3", "4/4,3/4", "w,h,q", "wr,hr,qr")]
    [InlineData("RhythmDictation", "rdLevel", "4", "4/4,3/4,2/4", "w,h,q,8", "wr,hr,qr,8r")]
    [InlineData("RhythmDictation", "rdLevel", "5", "4/4,3/4,2/4", "h.,h,q.,q,8", "qr")]
    [InlineData("RhythmDictation", "rdLevel", "6", "4/4,3/4,2/4", "h,q,8.,8,16", "qr")]
    [InlineData("RhythmDictation", "rdLevel", "7", "4/4,3/4,2/4", "h,q,8", "qr,8r")]
    [InlineData("RhythmDictation", "rdLevel", "8", "6/8", "h.,q.,q,8", "q.r")]
    public void Dictation_TellsTheEditorTheNoteValuesOfItsLevel(
        string exercise, string levelFilter, string level, string timeSignatures, string durations, string restDurations)
    {
        for (var round = 0; round < 20; round++)
        {
            var json = GenerateJson(exercise, new() { { levelFilter, level } });

            // A melody in 6/8 is written in the figures of compound meter.
            var timeSignature = json.Value<string>("timeSignature");
            var compound = exercise == "MelodicDictation" && timeSignature == "6/8";
            timeSignature.Should().BeOneOf(timeSignatures.Split(','));
            json["durations"]!.Values<string>().Should().Equal(
                compound ? new[] { "h.", "q.", "q", "8" } : durations.Split(','));
            json["restDurations"]!.Values<string>().Should().Equal(
                compound ? new[] { "q.r" } : restDurations.Split(',', StringSplitOptions.RemoveEmptyEntries));
            json.Value<bool>("rests").Should().Be(compound || restDurations.Length > 0);
        }
    }

    [Theory]
    [InlineData("MelodicDictation", "md", "1,3,4")]
    [InlineData("RhythmDictation", "rd", "1,3,4,5,6,7,8")]
    public void Dictation_AnswerIsWhatTheEditorWrites(string exercise, string prefix, string levels)
    {
        var rhythm = exercise == "RhythmDictation";
        foreach (var level in levels.Split(','))
        foreach (var length in new[] { "short", "long" })
        {
            for (var round = 0; round < 25; round++)
            {
                var json = GenerateJson(exercise, new() { { prefix + "Level", level }, { prefix + "Measures", length } });
                var notes = json["durations"]!.Values<string>().ToHashSet();
                var rests = json["restDurations"]!.Values<string>().ToHashSet();

                json.Value<bool>("rests").Should().Be(rests.Count > 0);
                if (!rhythm)
                {
                    notes.Should().Contain(json.Value<string>("firstDuration"), "the given first note is a note, not a rest");
                }

                foreach (var token in json.Value<string>("answerString")!.Split('|').Where(t => t != "bar"))
                {
                    var duration = rhythm ? token : token.Split(':')[1];
                    (duration.EndsWith('r') ? rests : notes).Should().Contain(duration,
                        $"the editor offers only the figures of level {level}");
                }

                AssertEditorWritesTheAnswer(json, rhythm);
            }
        }
    }

    [Fact]
    public void MelodicDictation_In68_IsWrittenInTheFiguresOfCompoundMeter()
    {
        var compound = 0;
        for (var round = 0; round < 400 && compound < 20; round++)
        {
            var json = GenerateJson("MelodicDictation", new() { { "mdLevel", "4" }, { "mdMeasures", "long" } });
            if (json.Value<string>("timeSignature") != "6/8")
            {
                continue;
            }

            compound++;
            var values = json["melody"]!.Select(entry => entry.Value<string>("durationLabel")).ToList();
            values.Should().OnlyContain(value => new[] { "h.", "q.", "q", "8", "q.r" }.Contains(value));
            values.Should().Contain(value => value == "q" || value == "8",
                "every round splits a beat in a quarter and an eighth, or in three eighths");
            AssertEditorWritesTheAnswer(json, rhythm: false);
        }

        compound.Should().Be(20, "a round of level 4 is in 6/8 one time in four");
    }

    [Theory]
    [InlineData("MelodicDictation", "mdTempo", "60", 60)]
    [InlineData("MelodicDictation", "mdTempo", "90", 90)]
    [InlineData("MelodicDictation", "mdTempo", null, 120)]
    [InlineData("RhythmDictation", "rdTempo", "60", 60)]
    [InlineData("RhythmDictation", "rdTempo", "90", 90)]
    [InlineData("RhythmDictation", "rdTempo", "75", 120)]
    [InlineData("GuessRhythmPattern", "grpTempo", "60", 60)]
    [InlineData("GuessRhythmPattern", "grpTempo", "90", 90)]
    [InlineData("GuessRhythmPattern", "grpTempo", null, 120)]
    [InlineData("RhythmTap", "rtTempo", "60", 60)]
    [InlineData("RhythmTap", "rtTempo", "90", 90)]
    [InlineData("RhythmTap", "rtTempo", "100", 120)]
    public void Dictation_IsPlayedAtTheTempoTheStudentPicked(string exercise, string filter, string? tempo, int expected)
    {
        var filters = new Dictionary<string, string>();
        if (tempo is not null)
        {
            filters[filter] = tempo;
        }

        GenerateJson(exercise, filters).Value<int>("tempo").Should().Be(expected);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("8")]
    public void GuessRhythmPattern_PlaysOneOfTheFourRhythmsItOffers_OnOneNote(string level)
    {
        for (var round = 0; round < 25; round++)
        {
            var json = GenerateJson("GuessRhythmPattern", new() { { "grpLevel", level } });
            var answer = json.Value<string>("answerString")!;

            json["error"].Should().BeNull();
            json.Value<int>("level").Should().Be(int.Parse(level));
            json.Value<int>("numMeasures").Should().Be(2);
            var options = json["options"]!.Values<string>().ToList();
            options.Should().HaveCount(4).And.OnlyHaveUniqueItems().And.Contain(answer, "the rhythm played is offered");

            // Each rhythm is written as a rhythm dictation's answer, so it is drawn as one.
            foreach (var option in options)
            {
                AssertEditorWritesTheAnswer(new JObject
                {
                    ["timeSignature"] = json["timeSignature"], ["numMeasures"] = 2, ["answerString"] = option,
                }, rhythm: true);
            }

            // The melody is the rhythm played, on one note, as a rhythm dictation's.
            var melody = json["melody"]!.ToList();
            melody.Select(entry => entry.Value<string>("durationLabel")).Should().Equal(answer.Split('|').Where(t => t != "bar"));
            melody.Should().AllSatisfy(entry =>
            {
                var value = entry.Value<string>("durationLabel")!;
                entry.Value<string>("note").Should().Be("C5");
                entry.Value<string>("type").Should().Be(value.EndsWith('r') ? "rest" : "note");
                entry.Value<double>("durationBeats").Should().Be(Sixteenths[value.TrimEnd('r')] / 4.0);
            });
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    [InlineData("9")]
    [InlineData("five")]
    public void GuessRhythmPattern_AnUnknownLevel_IsTheFirst(string? level)
    {
        var filters = new Dictionary<string, string> { ["grpTempo"] = "fast" };
        if (level is not null)
        {
            filters["grpLevel"] = level;
        }

        var json = GenerateJson("GuessRhythmPattern", filters);

        json.Value<int>("level").Should().Be(1);
        json.Value<int>("tempo").Should().Be(120);
        json.Value<string>("timeSignature").Should().Be("4/4");
        json["options"]!.Values<string>().Should().BeEquivalentTo("w|bar|w", "w|bar|h|h", "h|h|bar|w", "h|h|bar|h|h");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("8")]
    public void RhythmTap_PlaysARhythmOfTheLevel_OnOneNote(string level)
    {
        for (var round = 0; round < 25; round++)
        {
            var json = GenerateJson("RhythmTap", new() { { "rtLevel", level }, { "rtTempo", "90" } });
            var answer = json.Value<string>("answerString")!;
            var timeSignature = json.Value<string>("timeSignature")!;

            json["error"].Should().BeNull();
            json.Value<int>("level").Should().Be(int.Parse(level));
            json.Value<int>("numMeasures").Should().Be(2);
            json.Value<double>("tapsFrom").Should().Be(RhythmTaps.TapsFrom(timeSignature, 90));
            AssertEditorWritesTheAnswer(json, rhythm: true);

            // The melody is the rhythm, on one note, as a rhythm dictation's.
            var melody = json["melody"]!.ToList();
            melody.Select(entry => entry.Value<string>("durationLabel")).Should().Equal(answer.Split('|').Where(t => t != "bar"));
            melody.Should().AllSatisfy(entry =>
            {
                var value = entry.Value<string>("durationLabel")!;
                entry.Value<string>("note").Should().Be("C5");
                entry.Value<string>("type").Should().Be(value.EndsWith('r') ? "rest" : "note");
                entry.Value<double>("durationBeats").Should().Be(Sixteenths[value.TrimEnd('r')] / 4.0);
            });
            melody.Count(entry => entry.Value<string>("type") == "note").Should().BeGreaterThanOrEqualTo(RhythmTaps.FewestNotes);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    [InlineData("9")]
    [InlineData("five")]
    public void RhythmTap_AnUnknownLevel_IsTheFirst(string? level)
    {
        var filters = new Dictionary<string, string> { ["rtTempo"] = "fast" };
        if (level is not null)
        {
            filters["rtLevel"] = level;
        }

        var json = GenerateJson("RhythmTap", filters);

        json.Value<int>("level").Should().Be(1);
        json.Value<int>("tempo").Should().Be(120);
        json.Value<string>("timeSignature").Should().Be("4/4");
        json.Value<double>("tapsFrom").Should().Be(7.75, "the taps count from half a beat before the bar after the second count-in");
        json.Value<string>("answerString").Should().BeOneOf("w|bar|h|h", "h|h|bar|w", "h|h|bar|h|h");
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
