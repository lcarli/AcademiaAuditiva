using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// Translates the existing untyped <see cref="MusicTheoryService.GenerateNoteForExercise"/>
/// output into a list of <see cref="MixInput"/> playback plans without
/// changing the <c>ExpectedAnswer</c> shape (the validators still consume
/// the original JSON unchanged).
///
/// Produces:
///   - 1 plan for GuessNote / GuessChords / GuessFunction / GuessDegree /
///     GuessProgression / GuessQuality / GuessInterval / GuessFullInterval /
///     IntervalMelodico (GuessFunction, GuessDegree and GuessProgression play
///     a cadence in their key before their chord, note or progression, in
///     the same plan; GuessInterval and GuessFullInterval play their two
///     notes one after the other, or together when the round is harmonic)
///   - 1 plan for the exercises written on the staff (CompleteChord,
///     CompleteScale, TransposeScale, MelodicDictation, RhythmDictation),
///     and for GuessRhythmPattern and RhythmTap, which play a rhythm as
///     RhythmDictation does. The dictations play at the tempo the student
///     picked, and count the student in on the piano first, in the same plan
///     (<see cref="Dictation"/>); RhythmTap counts them in again after it
///   - 2 plans for GuessMissingNote (one per melody)
///   - 1 plan for GuessMeter: its twelve beats, clicked on the piano or
///     played by an accompaniment (<see cref="Meter"/>)
///   - 0 plans for SolfegeMelody: the melody is shown as sheet music for
///     the student to sing, so there is nothing to hide. Only its first
///     note is played, on the piano, when the student asks for it
///     (<see cref="StartingNote"/>).
///
/// Every plan is played on the instrument the student picked (the
/// <c>instrument</c> filter): the piano plays a chord's notes together, the
/// guitar strums it on a chord shape of its neck (<see cref="GuitarVoicing"/>)
/// where the student picked (the <c>guitarPosition</c> filter), or exactly as
/// written when the student writes it on the staff (CompleteChord), names
/// its top note (GuessTopNote) or names a harmonic interval, and the
/// exercises about chords are played on the piano instead of the violin,
/// which plays one note at a time, or two together in a harmonic interval,
/// as a double stop. So is the cadence that sets the key of a
/// note played on the violin (GuessDegree), and the accompaniment GuessMeter
/// plays for it.
/// </summary>
public sealed class ExercisePlaybackPlanner
{
    // 120 BPM → one beat is half a second. Matches the Tone.js default
    // tempo the front-end used when scheduling melodies, so playback
    // duration stays the same end-to-end.
    private const double BeatDurationSeconds = 0.5;

    // Gap between sequential notes in interval exercises. Matches the
    // 0.5s "delay" the JS sequencer used for GuessInterval/FullInterval.
    private const double IntervalGapSeconds = 0.5;

    // Chord/note clip length. Most piano sample blobs are ~3s sustained
    // tones; clip them so the mix doesn't drag.
    private const double NoteClipSeconds = 1.5;

    // IntervalMelodico melodies: one note every 0.6s, each held for 0.8s
    // (slightly legato), as the former Tone.js player scheduled them.
    private const double MelodyStepSeconds = 0.6;
    private const double MelodyNoteSeconds = 0.8;

    // Scales play many notes in a row — at the default 1.5s+0.5s pace a
    // 7-note diatonic scale runs ~14s, which feels sluggish. Halve the
    // clip and shrink the gap so the whole scale lands in ~5s.
    private const double ScaleNoteClipSeconds = 0.75;
    private const double ScaleNoteGapSeconds = 0.1;

    // Cadence progression timing — each chord rings for ~1.2s with a
    // tiny gap so the four chords land in ~5s without dragging.
    private const double CadenceChordSeconds = 1.2;
    private const double CadenceChordGapSeconds = 0.05;

    // A guitar strum sweeps the strings from the low one up, one every 15 ms.
    private const double StrumStepSeconds = 0.015;

    // A written chord rings for a whole note: 4 beats.
    private const double WholeNoteBeats = 4.0;

    // The count-in of a dictation: a short click high on the piano for each beat, an
    // octave higher on the accented beats, so the bar is heard.
    private const double ClickSeconds = 0.12;
    private const int ClickOctave = 6;
    private const int AccentOctave = 7;

    // GuessMeter's accompaniment: the bass rings through most of the first beat of a bar, and
    // the chord is short on each of the others, so a bar of 3/4 goes oom-pah-pah.
    private const double BassBeats = 0.9;
    private const double OffbeatChordBeats = 0.6;

    // The exercises that play chords on a shape of the guitar's neck; the violin can't play them.
    private static readonly HashSet<string> ChordsPlayed =
        ["GuessChords", "GuessFunction", "GuessQuality", "GuessInversion", "GuessCadence", "GuessProgression"];

    // The exercises about chords: those above, and those that play a chord as it is written:
    // CompleteChord, for the student to write it on the staff, and GuessTopNote, whose voicing
    // is the question. On the guitar they are strummed as written, not on a shape of the neck,
    // so they offer no position to pick.
    private static readonly HashSet<string> ChordExercises = [.. ChordsPlayed, "CompleteChord", "GuessTopNote"];

    /// <summary>
    /// Whether <paramref name="exerciseName"/> is about chords: it is then only played on (and
    /// only offers) the instruments that play them, see <see cref="Instrument.Offered"/>.
    /// </summary>
    public static bool IsChordExercise(string exerciseName) => ChordExercises.Contains(exerciseName);

    /// <summary>
    /// Whether <paramref name="exerciseName"/> plays chords on a shape of the guitar's neck. On
    /// the guitar, the student then picks where on the neck to play them (<see cref="GuitarPosition"/>),
    /// which sets their octaves instead of the note range. CompleteChord and GuessTopNote play
    /// their chords as written.
    /// </summary>
    public static bool PlaysChords(string exerciseName) => ChordsPlayed.Contains(exerciseName);

    /// <summary>
    /// Returns the JSON to cache as <c>ExpectedAnswer</c> together with
    /// the playback plans that need to be mixed and tokenized. An empty
    /// list of plans means the question isn't played: it is shown as
    /// sheet music (SolfegeMelody, see <see cref="StartingNote"/>).
    /// </summary>
    public ExercisePlan Plan(Exercise exercise, Dictionary<string, string> filters)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        ArgumentNullException.ThrowIfNull(filters);

        // The note range comes from a cookie or the request: keep it where the
        // instrument sounds natural, without touching the caller's filters.
        // Chords the instrument can't play are played on the piano, and the
        // guitar plays them where on the neck the student picked, in the
        // exercises that let them pick it: the others play its open chords.
        var instrument = Instrument.FromName(filters.GetValueOrDefault("instrument"), IsChordExercise(exercise.Name));
        var position = PlaysChords(exercise.Name)
            ? GuitarVoicing.PositionFromName(filters.GetValueOrDefault("guitarPosition"))
            : GuitarPosition.Open;
        var instrumentFilters = new Dictionary<string, string>(filters, filters.Comparer)
        {
            ["noteRange"] = instrument.ClampRange(filters.GetValueOrDefault("noteRange")),
        };

        var raw = MusicTheoryService.GenerateNoteForExercise(exercise, instrumentFilters, instrument);
        var expectedJson = JsonConvert.SerializeObject(raw);
        var token = JObject.Parse(expectedJson);

        var plans = new List<IReadOnlyList<MixInput>>();
        switch (exercise.Name)
        {
            case "GuessNote":
                plans.Add(new[] { Note(instrument, token.Value<string>("note") ?? throw Bad("note")) });
                break;

            case "GuessChords":
            case "GuessQuality":
            case "GuessInversion":
                plans.Add([.. Chord(instrument, StringArray(token, "notes"), position, 0.0, NoteClipSeconds)]);
                break;

            case "GuessFunction":
                // A function is heard against its key: the cadence first, then the chord.
                plans.Add(AfterTheKey(
                    instrument,
                    ChordArray(token, "cadence"),
                    position,
                    start => Chord(instrument, StringArray(token, "notes"), position, start, NoteClipSeconds)));
                break;

            case "GuessDegree":
                // A degree is heard against its key too: the cadence first, then the note.
                plans.Add(AfterTheKey(
                    instrument,
                    ChordArray(token, "cadence"),
                    position,
                    start => [Note(instrument, token.Value<string>("note") ?? throw Bad("note"), start)]));
                break;

            case "GuessProgression":
                // So is a progression: the cadence, then its chords at the cadence's pace.
                plans.Add(AfterTheKey(
                    instrument,
                    ChordArray(token, "cadence"),
                    position,
                    start => ChordsInSequence(
                        instrument,
                        ChordArray(token, "chords"),
                        position,
                        CadenceChordSeconds,
                        CadenceChordGapSeconds,
                        start)));
                break;

            case "HigherOrLower":
            case "GuessInterval":
            case "GuessFullInterval":
            {
                string[] interval =
                [
                    token.Value<string>("note1") ?? throw Bad("note1"),
                    token.Value<string>("note2") ?? throw Bad("note2"),
                ];
                // A harmonic interval sounds both notes at once, in the octaves written: a shape
                // of the guitar's neck would move them. HigherOrLower is always melodic.
                plans.Add(token.Value<bool?>("harmonic") == true
                    ? WrittenChord(instrument, interval)
                    : NotesInSequence(instrument, interval));
                break;
            }

            case "GuessScaleType":
            case "GuessGreekMode":
                plans.Add(NotesInSequence(
                    instrument,
                    StringArray(token, "notes"),
                    ScaleNoteClipSeconds,
                    ScaleNoteGapSeconds));
                break;

            case "GuessCadence":
                plans.Add(ChordsInSequence(
                    instrument,
                    ChordArray(token, "chords"),
                    position,
                    CadenceChordSeconds,
                    CadenceChordGapSeconds));
                break;

            case "GuessMissingNote":
                plans.Add(MelodyPlan(instrument, token["melody1"] as JArray ?? throw Bad("melody1")));
                plans.Add(MelodyPlan(instrument, token["melody2"] as JArray ?? throw Bad("melody2")));
                break;

            case "IntervalMelodico":
                plans.Add(EvenMelody(instrument, StringArray(token, "melody")));
                break;

            case "SolfegeMelody":
                // Sheet music: only its starting note is played (StartingNote).
                break;

            case "CompleteChord":
                plans.Add(WrittenChord(instrument, StringArray(token, "chordNotes")));
                break;

            case "GuessTopNote":
                // Its voicing is the question: a shape of the guitar's neck would change the top note.
                plans.Add(WrittenChord(instrument, StringArray(token, "notes")));
                break;

            case "CompleteScale":
            case "TransposeScale":
                // Staff-based exercises share a unified melody contract:
                // ExpectedAnswerJson contains a `melody` JArray with
                // entries { type, note, durationBeats, durationLabel }.
                plans.Add(MelodyPlan(instrument, token["melody"] as JArray ?? throw Bad("melody")));
                break;

            case "MelodicDictation":
                // The dictations share it too, and count the student in first: on the tonic of a melody.
                plans.Add(Dictation(instrument, token, token.Value<string>("root") ?? throw Bad("root")));
                break;

            case "RhythmDictation":
            case "GuessRhythmPattern":
                plans.Add(Dictation(instrument, token, "C"));
                break;

            case "RhythmTap":
                plans.Add(Dictation(instrument, token, "C", countInAgain: true));
                break;

            case "GuessMeter":
                plans.Add(Meter(instrument, token));
                break;

            default:
                throw new InvalidOperationException(
                    $"ExercisePlaybackPlanner does not know how to plan '{exercise.Name}'.");
        }

        return new ExercisePlan(expectedJson, plans);
    }

    /// <summary>
    /// The starting note of a sight-singing melody (the expected answer of SolfegeMelody):
    /// its first note, on the piano, for the student to find the pitch before singing.
    /// The melody is on the staff anyway, so the note gives nothing away.
    /// </summary>
    public IReadOnlyList<MixInput> StartingNote(string expectedAnswerJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedAnswerJson);

        var melody = JObject.Parse(expectedAnswerJson)["melody"] as JArray ?? throw Bad("melody");
        var first = melody.OfType<JObject>().FirstOrDefault(item => item.Value<string>("type") == "note");
        return [Note(Instrument.Piano, first?.Value<string>("note") ?? throw Bad("melody"))];
    }

    private static MixInput Note(Instrument instrument, string note, double startTime = 0.0) =>
        new(instrument.SampleFor(note), startTime, NoteClipSeconds);

    /// <summary>
    /// One chord, starting at <paramref name="startTime"/> and ringing for
    /// <paramref name="seconds"/>. The piano plays its notes together; the guitar
    /// strums them on a chord shape in <paramref name="position"/> on its neck
    /// (<see cref="GuitarVoicing"/>), from the low string up, and every string rings
    /// until the chord ends. The shape, not the note range, sets the octaves of the strings.
    /// </summary>
    private static IEnumerable<MixInput> Chord(
        Instrument instrument,
        IReadOnlyList<string> notes,
        GuitarPosition position,
        double startTime,
        double seconds)
    {
        if (instrument.Chords != ChordStyle.Strummed)
            return notes.Select(note => new MixInput(instrument.SampleFor(note), startTime, seconds));

        var midis = notes.Select(Midi).ToList();
        var strings = GuitarVoicing.Find(midis, position)?.Notes ?? [.. midis.Order()];
        return strings.Select((midi, i) => new MixInput(
            instrument.SampleName(midi),
            startTime + i * StrumStepSeconds,
            seconds - i * StrumStepSeconds));
    }

    private static IReadOnlyList<MixInput> NotesInSequence(Instrument instrument, IReadOnlyList<string> notes)
        => NotesInSequence(instrument, notes, NoteClipSeconds, IntervalGapSeconds);

    /// <summary>
    /// A chord as it is written on the staff, from <paramref name="startTime"/> for
    /// <paramref name="seconds"/>, a whole note unless told: the piano plays its notes together
    /// and the guitar strums exactly those notes, from the lowest up, so the student hears what
    /// they write (<see cref="Chord"/> plays a shape of the neck instead). The violin plays them
    /// together too, which only a harmonic interval asks of it: a double stop.
    /// </summary>
    private static IReadOnlyList<MixInput> WrittenChord(
        Instrument instrument,
        IReadOnlyList<string> notes,
        double startTime = 0.0,
        double seconds = WholeNoteBeats * BeatDurationSeconds)
    {
        if (instrument.Chords != ChordStyle.Strummed)
            return [.. notes.Select(note => new MixInput(instrument.SampleFor(note), startTime, seconds))];

        return [.. notes.Select(Midi).Order().Select((midi, i) => new MixInput(
            instrument.SampleName(midi),
            startTime + i * StrumStepSeconds,
            seconds - i * StrumStepSeconds))];
    }

    private static IReadOnlyList<MixInput> NotesInSequence(
        Instrument instrument,
        IReadOnlyList<string> notes,
        double clipSeconds,
        double gapSeconds)
    {
        var plan = new MixInput[notes.Count];
        var t = 0.0;
        for (var i = 0; i < notes.Count; i++)
        {
            plan[i] = new MixInput(instrument.SampleFor(notes[i]), t, clipSeconds);
            t += gapSeconds + clipSeconds;
        }
        return plan;
    }

    /// <summary>
    /// Stacks several chords into a single playback plan: chord <c>i</c> starts
    /// (see <see cref="Chord"/>) <c>chordSeconds + gapSeconds</c> times <c>i</c>
    /// after <paramref name="startTime"/>. Used by GuessCadence so the four chords
    /// play sequentially as a single audio mix, and by GuessProgression after the
    /// cadence of its key.
    /// </summary>
    private static IReadOnlyList<MixInput> ChordsInSequence(
        Instrument instrument,
        IReadOnlyList<IReadOnlyList<string>> chords,
        GuitarPosition position,
        double chordSeconds,
        double gapSeconds,
        double startTime = 0.0)
    {
        var plan = new List<MixInput>();
        var t = startTime;
        foreach (var chord in chords)
        {
            plan.AddRange(Chord(instrument, chord, position, t, chordSeconds));
            t += chordSeconds + gapSeconds;
        }
        return plan;
    }

    /// <summary>
    /// The cadence that sets the key (<see cref="MusicTheoryService.KeyCadence"/>), at
    /// GuessCadence's pace, then a silent beat of that pace, then the question, from the start
    /// time it is given. It is all one mix, so Replay plays the key again before the question.
    /// The piano plays the cadence for an instrument that plays no chords (the violin).
    /// </summary>
    private static IReadOnlyList<MixInput> AfterTheKey(
        Instrument instrument,
        IReadOnlyList<IReadOnlyList<string>> cadence,
        GuitarPosition position,
        Func<double, IEnumerable<MixInput>> question)
    {
        var beat = CadenceChordSeconds + CadenceChordGapSeconds;
        var accompaniment = instrument.PlaysChords ? instrument : Instrument.Piano;
        return [
            .. ChordsInSequence(accompaniment, cadence, position, CadenceChordSeconds, CadenceChordGapSeconds),
            .. question((cadence.Count + 1) * beat),
        ];
    }

    private static IReadOnlyList<MixInput> EvenMelody(Instrument instrument, IReadOnlyList<string> notes)
    {
        var plan = new MixInput[notes.Count];
        for (var i = 0; i < notes.Count; i++)
        {
            plan[i] = new MixInput(instrument.SampleFor(notes[i]), i * MelodyStepSeconds, MelodyNoteSeconds);
        }
        return plan;
    }

    /// <summary>
    /// A dictation (MelodicDictation, RhythmDictation) at the <c>tempo</c> of its round, in
    /// quarter notes a minute. The count-in comes first: a click on the piano for each beat of
    /// its time signature (<see cref="DictationRhythm.CountIn"/>), an octave higher when it
    /// is accented, on <paramref name="clickPitch"/>. Then the melody starts on the next beat,
    /// on the student's instrument. RhythmTap counts the student in again once the melody is
    /// over (<paramref name="countInAgain"/>), for them to tap it back from the next beat.
    /// </summary>
    private static IReadOnlyList<MixInput> Dictation(
        Instrument instrument, JObject token, string clickPitch, bool countInAgain = false)
    {
        var beatSeconds = 60.0 / (token.Value<int?>("tempo") ?? DictationRhythm.Tempos[0]);
        var timeSignature = token.Value<string>("timeSignature") ?? throw Bad("timeSignature");
        var melody = token["melody"] as JArray ?? throw Bad("melody");
        var plan = new List<MixInput>();
        var t = CountIn(plan, timeSignature, clickPitch, beatSeconds, 0.0);
        plan.AddRange(MelodyPlan(instrument, melody, beatSeconds, t));
        if (countInAgain)
        {
            CountIn(plan, timeSignature, clickPitch, beatSeconds, t + melody.Sum(BeatsOf) * beatSeconds);
        }
        return plan;
    }

    // Adds the clicks of a count-in from `start` to the plan, and returns when the beat after them starts.
    private static double CountIn(List<MixInput> plan, string timeSignature, string clickPitch, double beatSeconds, double start)
    {
        var t = start;
        foreach (var (beats, accent) in DictationRhythm.CountIn(timeSignature))
        {
            var octave = accent ? AccentOctave : ClickOctave;
            plan.Add(new MixInput(Instrument.Piano.SampleFor($"{clickPitch}{octave}"), t, ClickSeconds));
            t += beats * beatSeconds;
        }
        return t;
    }

    /// <summary>
    /// GuessMeter: the <see cref="MusicTheoryService.MeterBeats"/> beats of its round at 120 BPM
    /// (<see cref="BeatDurationSeconds"/>), the first of each bar accented. They are clicks on the
    /// piano, as a dictation counts in, or an accompaniment: the bass of each bar's chord on its
    /// first beat and the chord on the others, played by the student's instrument when it plays
    /// chords (the guitar strums them as written) and by the piano when it doesn't (the violin).
    /// </summary>
    private static IReadOnlyList<MixInput> Meter(Instrument instrument, JObject token)
    {
        var beatsPerBar = token.Value<int?>("beatsPerBar") ?? throw Bad("beatsPerBar");
        var plan = new List<MixInput>();
        if (token.Value<string>("level") != "accompaniment")
        {
            for (var beat = 0; beat < MusicTheoryService.MeterBeats; beat++)
            {
                var octave = beat % beatsPerBar == 0 ? AccentOctave : ClickOctave;
                plan.Add(new MixInput(Instrument.Piano.SampleFor($"C{octave}"), beat * BeatDurationSeconds, ClickSeconds));
            }
            return plan;
        }

        var accompaniment = instrument.PlaysChords ? instrument : Instrument.Piano;
        var t = 0.0;
        foreach (var bar in (token["bars"] as JArray ?? throw Bad("bars")).OfType<JObject>())
        {
            plan.Add(new MixInput(
                accompaniment.SampleFor(bar.Value<string>("bass") ?? throw Bad("bass")), t, BassBeats * BeatDurationSeconds));
            var chord = StringArray(bar, "chord");
            for (var beat = 1; beat < beatsPerBar; beat++)
            {
                plan.AddRange(WrittenChord(
                    accompaniment, chord, t + beat * BeatDurationSeconds, OffbeatChordBeats * BeatDurationSeconds));
            }
            t += beatsPerBar * BeatDurationSeconds;
        }
        return plan;
    }

    private static IReadOnlyList<MixInput> MelodyPlan(
        Instrument instrument, JArray melody, double beatSeconds = BeatDurationSeconds, double startTime = 0.0)
    {
        var plan = new List<MixInput>();
        var t = startTime;
        foreach (var entry in melody)
        {
            var type = entry.Value<string>("type");
            var note = entry.Value<string>("note");
            var seconds = BeatsOf(entry) * beatSeconds;

            if (type == "note" && !string.IsNullOrEmpty(note) && note != "rest")
            {
                plan.Add(new MixInput(instrument.SampleFor(note), t, seconds));
            }
            // rests advance the cursor without emitting an input.
            t += seconds;
        }
        return plan;
    }

    // How long a melody entry lasts, in beats: durationBeats (the staff exercises), or duration (legacy GuessMissingNote).
    private static double BeatsOf(JToken entry) =>
        entry.Value<double?>("durationBeats") ?? entry.Value<double?>("duration") ?? 1.0;

    private static IReadOnlyList<string> StringArray(JObject token, string field)
    {
        if (token[field] is not JArray arr)
        {
            throw Bad(field);
        }
        var notes = new string[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            notes[i] = arr[i].Value<string>() ?? throw Bad(field);
        }
        return notes;
    }

    /// <summary>
    /// Reads a 2D array (array-of-arrays-of-strings) from the expected
    /// answer JSON — used by chord-progression exercises like
    /// GuessCadence where each entry is one chord's notes.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> ChordArray(JObject token, string field)
    {
        if (token[field] is not JArray arr)
        {
            throw Bad(field);
        }
        var chords = new IReadOnlyList<string>[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JArray inner)
                throw Bad($"{field}[{i}]");
            var notes = new string[inner.Count];
            for (var j = 0; j < inner.Count; j++)
            {
                notes[j] = inner[j].Value<string>() ?? throw Bad($"{field}[{i}][{j}]");
            }
            chords[i] = notes;
        }
        return chords;
    }

    private static InvalidOperationException Bad(string field) =>
        new($"Generated exercise data is missing field '{field}'.");

    private static int Midi(string note) =>
        MusicTheoryService.NoteToMidi(note)
        ?? throw new InvalidOperationException($"Generated exercise data has an invalid note '{note}'.");
}

/// <summary>
/// Result of <see cref="ExercisePlaybackPlanner.Plan"/>.
/// </summary>
/// <param name="ExpectedAnswerJson">JSON to cache and feed to validators (unchanged shape).</param>
/// <param name="PlaybackPlans">
/// Mixer plans, in playback order. Empty list means the question is shown
/// as sheet music instead (SolfegeMelody). Most exercises produce one plan;
/// GuessMissingNote produces two (melody1, melody2).
/// </param>
public sealed record ExercisePlan(
    string ExpectedAnswerJson,
    IReadOnlyList<IReadOnlyList<MixInput>> PlaybackPlans);
