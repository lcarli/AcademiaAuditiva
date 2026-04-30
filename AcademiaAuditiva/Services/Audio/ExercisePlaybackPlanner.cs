using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
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
///     GuessInterval / GuessFullInterval
///   - 2 plans for GuessMissingNote (one per melody)
///   - 0 plans for SolfegeMelody / IntervalMelodico (out of scope; the
///     caller short-circuits by not invoking the mixer for those).
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

    /// <summary>
    /// Returns the JSON to cache as <c>ExpectedAnswer</c> together with
    /// the playback plans that need to be mixed and tokenized. An empty
    /// list of plans means "no audio to issue" (the front will render
    /// the question some other way — currently only sheet-music
    /// exercises, which are out of scope for this anti-cheat work).
    /// </summary>
    public ExercisePlan Plan(Exercise exercise, Dictionary<string, string> filters)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        ArgumentNullException.ThrowIfNull(filters);

        var raw = MusicTheoryService.GenerateNoteForExercise(exercise, filters);
        var expectedJson = JsonConvert.SerializeObject(raw);
        var token = JObject.Parse(expectedJson);

        var plans = new List<IReadOnlyList<MixInput>>();
        switch (exercise.Name)
        {
            case "GuessNote":
                plans.Add(new[] { Note(token.Value<string>("note") ?? throw Bad("note")) });
                break;

            case "GuessChords":
            case "GuessFunction":
            case "GuessQuality":
                plans.Add(NotesAtOnce(StringArray(token, "notes")));
                break;

            case "GuessInterval":
            case "GuessFullInterval":
                plans.Add(NotesInSequence(new[]
                {
                    token.Value<string>("note1") ?? throw Bad("note1"),
                    token.Value<string>("note2") ?? throw Bad("note2"),
                }));
                break;

            case "GuessMissingNote":
                plans.Add(MelodyPlan(token["melody1"] as JArray ?? throw Bad("melody1")));
                plans.Add(MelodyPlan(token["melody2"] as JArray ?? throw Bad("melody2")));
                break;

            case "SolfegeMelody":
            case "IntervalMelodico":
                // Sheet-music exercises — no audio token is issued.
                break;

            default:
                throw new InvalidOperationException(
                    $"ExercisePlaybackPlanner does not know how to plan '{exercise.Name}'.");
        }

        return new ExercisePlan(expectedJson, plans);
    }

    private static MixInput Note(string note, double startTime = 0.0) =>
        new(NoteToBlob(note), startTime, NoteClipSeconds);

    private static IReadOnlyList<MixInput> NotesAtOnce(IReadOnlyList<string> notes)
    {
        var plan = new MixInput[notes.Count];
        for (var i = 0; i < notes.Count; i++)
        {
            plan[i] = Note(notes[i]);
        }
        return plan;
    }

    private static IReadOnlyList<MixInput> NotesInSequence(IReadOnlyList<string> notes)
    {
        var plan = new MixInput[notes.Count];
        var t = 0.0;
        for (var i = 0; i < notes.Count; i++)
        {
            plan[i] = Note(notes[i], t);
            t += IntervalGapSeconds + NoteClipSeconds;
        }
        return plan;
    }

    private static IReadOnlyList<MixInput> MelodyPlan(JArray melody)
    {
        var plan = new List<MixInput>();
        var t = 0.0;
        foreach (var entry in melody)
        {
            var type = entry.Value<string>("type");
            var note = entry.Value<string>("note");
            var beats = entry.Value<double?>("duration") ?? 1.0;
            var seconds = beats * BeatDurationSeconds;

            if (type == "note" && !string.IsNullOrEmpty(note) && note != "rest")
            {
                plan.Add(new MixInput(NoteToBlob(note), t, seconds));
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
    /// Converts a music-theoretic note name (e.g. <c>C#4</c>, <c>Db5</c>)
    /// to the blob filename in the <c>piano-audio</c> container. The
    /// existing samples use <c>s</c> in place of <c>#</c> (e.g.
    /// <c>Cs4.mp3</c>) and don't ship flat-named files — flats are
    /// rewritten to their enharmonic sharp.
    /// </summary>
    private static string NoteToBlob(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Note name must not be empty.", nameof(note));
        }

        // Normalize flats to sharps — only sharp samples exist.
        var normalized = note switch
        {
            _ when note.Contains('b') => FlatToSharp(note),
            _ => note,
        };
        return normalized.Replace("#", "s") + ".mp3";
    }

    private static string FlatToSharp(string note)
    {
        // E.g. Db4 → C#4, Gb4 → F#4. Keep this small; complete tables
        // already exist in MusicTheoryService for actual music theory.
        var letter = note[0].ToString();
        var octave = note[^1];
        var prevLetter = letter switch
        {
            "A" => "G",
            "B" => "A",
            "C" => "B",
            "D" => "C",
            "E" => "D",
            "F" => "E",
            "G" => "F",
            _ => letter,
        };
        // Cb / Fb don't exist as enharmonic sharps — fall through.
        if (letter is "C" or "F")
        {
            return prevLetter + octave;
        }
        return prevLetter + "#" + octave;
    }

    private static InvalidOperationException Bad(string field) =>
        new($"Generated exercise data is missing field '{field}'.");
}

/// <summary>
/// Result of <see cref="ExercisePlaybackPlanner.Plan"/>.
/// </summary>
/// <param name="ExpectedAnswerJson">JSON to cache and feed to validators (unchanged shape).</param>
/// <param name="PlaybackPlans">
/// Mixer plans, in playback order. Empty list means "no audio token to
/// issue" (sheet-music exercises). Most exercises produce one plan;
/// GuessMissingNote produces two (melody1, melody2).
/// </param>
public sealed record ExercisePlan(
    string ExpectedAnswerJson,
    IReadOnlyList<IReadOnlyList<MixInput>> PlaybackPlans);
