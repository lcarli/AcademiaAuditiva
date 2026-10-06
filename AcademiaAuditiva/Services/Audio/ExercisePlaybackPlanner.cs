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
///   - 1 plan for GuessNote / GuessChords / GuessFunction / GuessQuality /
///     GuessInterval / GuessFullInterval / IntervalMelodico
///   - 2 plans for GuessMissingNote (one per melody)
///   - 0 plans for SolfegeMelody: the melody is shown as sheet music for
///     the student to sing, so there is nothing to hide. Only its first
///     note is played, on the piano, when the student asks for it
///     (<see cref="StartingNote"/>).
///
/// Every plan is played on the instrument the student picked (the
/// <c>instrument</c> filter): the piano plays a chord's notes together, the
/// guitar strums it on a chord shape of its neck (<see cref="GuitarVoicing"/>)
/// where the student picked (the <c>guitarPosition</c> filter), and the
/// exercises about chords are played on the piano instead of the violin,
/// which plays one note at a time.
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

    // The exercises that play chords, which the violin can't.
    private static readonly HashSet<string> ChordsPlayed =
        ["GuessChords", "GuessFunction", "GuessQuality", "GuessInversion", "GuessCadence"];

    // The exercises about chords: those that play them, and CompleteChord, which plays
    // the root of a chord for the student to complete it.
    private static readonly HashSet<string> ChordExercises = [.. ChordsPlayed, "CompleteChord"];

    /// <summary>
    /// Whether <paramref name="exerciseName"/> is about chords: it is then only played on (and
    /// only offers) the instruments that play them, see <see cref="Instrument.Offered"/>.
    /// </summary>
    public static bool IsChordExercise(string exerciseName) => ChordExercises.Contains(exerciseName);

    /// <summary>
    /// Whether <paramref name="exerciseName"/> plays chords. On the guitar, the student then
    /// picks where on the neck to play them (<see cref="GuitarPosition"/>), which sets their
    /// octaves instead of the note range.
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
        // guitar plays them where on the neck the student picked.
        var instrument = Instrument.FromName(filters.GetValueOrDefault("instrument"), IsChordExercise(exercise.Name));
        var position = GuitarVoicing.PositionFromName(filters.GetValueOrDefault("guitarPosition"));
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
            case "GuessFunction":
            case "GuessQuality":
            case "GuessInversion":
                plans.Add([.. Chord(instrument, StringArray(token, "notes"), position, 0.0, NoteClipSeconds)]);
                break;

            case "HigherOrLower":
            case "GuessInterval":
            case "GuessFullInterval":
                plans.Add(NotesInSequence(instrument, new[]
                {
                    token.Value<string>("note1") ?? throw Bad("note1"),
                    token.Value<string>("note2") ?? throw Bad("note2"),
                }));
                break;

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

            case "CompleteScale":
            case "CompleteChord":
            case "TransposeScale":
            case "MelodicDictation":
            case "RhythmDictation":
                // Staff-based exercises share a unified melody contract:
                // ExpectedAnswerJson contains a `melody` JArray with
                // entries { type, note, durationBeats, durationLabel }.
                plans.Add(MelodyPlan(instrument, token["melody"] as JArray ?? throw Bad("melody")));
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
    /// (see <see cref="Chord"/>) after <c>chordSeconds + gapSeconds</c> times
    /// <c>i</c>. Used by GuessCadence so the four chords play sequentially as
    /// a single audio mix.
    /// </summary>
    private static IReadOnlyList<MixInput> ChordsInSequence(
        Instrument instrument,
        IReadOnlyList<IReadOnlyList<string>> chords,
        GuitarPosition position,
        double chordSeconds,
        double gapSeconds)
    {
        var plan = new List<MixInput>();
        var t = 0.0;
        foreach (var chord in chords)
        {
            plan.AddRange(Chord(instrument, chord, position, t, chordSeconds));
            t += chordSeconds + gapSeconds;
        }
        return plan;
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

    private static IReadOnlyList<MixInput> MelodyPlan(Instrument instrument, JArray melody)
    {
        var plan = new List<MixInput>();
        var t = 0.0;
        foreach (var entry in melody)
        {
            var type = entry.Value<string>("type");
            var note = entry.Value<string>("note");
            // Prefer durationBeats (new staff exercises); fall back to duration (legacy GuessMissingNote).
            var beats = entry.Value<double?>("durationBeats") ?? entry.Value<double?>("duration") ?? 1.0;
            var seconds = beats * BeatDurationSeconds;

            if (type == "note" && !string.IsNullOrEmpty(note) && note != "rest")
            {
                plan.Add(new MixInput(instrument.SampleFor(note), t, seconds));
            }
            // rests advance the cursor without emitting an input.
            t += seconds;
        }
        return plan;
    }

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
