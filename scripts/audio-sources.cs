#:project ../AcademiaAuditiva/AcademiaAuditiva.csproj
#:property PublishAot=false

// Builds the training recordings of the audio track (docs/Audio-Ear-Training.md) in
// AcademiaAuditiva/Audio/Sources and keeps sources.json in step with them. Run it from the
// repository, with the .NET 10 SDK (--no-cache, since the cached build would miss changes
// to the app code it reuses):
//
//   dotnet run --no-cache scripts/audio-sources.cs generate
//       Synthesizes the repository's own recordings (origin "generated", MIT) from a fixed
//       seed, then measures every source.
//
//   dotnet run --no-cache scripts/audio-sources.cs ingest <key> <file.wav>
//       Adds a recording obtained elsewhere (origin "external"). Its entry, with everything
//       but "audio", must already be in sources.json: kind, tags, uses, difficulties and a
//       license with its sourceUrl (and attribution, when the license asks for one). The file
//       must be 16- or 24-bit integer PCM at 44.1 kHz, mono or stereo, 2 to 20 s long; it is
//       set to -23 LUFS (lower if its peak would pass -1.5 dBFS), stored as 16-bit, then every
//       source is measured. Anything else is refused with the reason, never converted: to
//       convert, e.g. `ffmpeg -i in.flac -ar 44100 -c:a pcm_s16le out.wav`.
//
//   dotnet run --no-cache scripts/audio-sources.cs measure
//       Rewrites the "audio" measurements of every source from its file and checks the rules.

using System.Text.Json;
using System.Text.Json.Nodes;
using AcademiaAuditiva.Services.Audio.Sources;

const double TargetLufs = -23;
const double PeakCeilingDbfs = -1.5;
const int Rate = AudioSourceRules.SampleRate;

var root = FindSourcesFolder();
var catalogPath = Path.Combine(root, AudioSourceCatalog.FileName);

try
{
    switch (args)
    {
        case ["generate"]:
            Generate();
            break;
        case ["ingest", var key, var file]:
            Ingest(key, file);
            break;
        case ["measure"]:
            Measure();
            break;
        default:
            Console.Error.WriteLine("Usage: dotnet run --no-cache scripts/audio-sources.cs generate | ingest <key> <file.wav> | measure");
            return 2;
    }
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;

void Generate()
{
    var drums = Drums(new Random64(1));
    var bass = Bass();
    var chordVoices = ChordVoices();
    var chords = Sum(chordVoices);

    // The full mix: drums and bass in the center, the three chord voices spread across.
    var full = new float[drums.Length * 2];
    AddPanned(full, Normalized(drums, TargetLufs), 0);
    AddPanned(full, Normalized(bass, TargetLufs - 2), 0);
    for (var v = 0; v < chordVoices.Count; v++)
        AddPanned(full, Normalized(chords, TargetLufs - 5, chordVoices[v]), (v - 1) * 0.6);

    var generated = new (string Key, string Description, string Kind, string[] Tags, string[] Uses, int[] Difficulties, PcmAudio Audio)[]
    {
        ("pink-noise", "Pink noise: equal energy in every octave.", "noise", ["noise", "broadband"], ["level", "pan", "eq"], [1, 2],
            Mono(PinkNoise(new Random64(2), 8))),
        ("drum-loop", "A drum loop at 120 bpm: kick, snare and eighth-note hi-hats.", "drums", ["drums", "kick", "snare", "hihat", "percussion"], ["level", "pan", "eq"], [1, 2, 3],
            Mono(drums)),
        ("bass-line", "A synthesized bass line over A, F, C and G.", "bass", ["bass", "synth"], ["level", "pan", "eq"], [1, 2, 3],
            Mono(bass)),
        ("synth-chords", "Sustained synthesizer chords: Am, F, C and G.", "keys", ["keys", "chords", "synth"], ["level", "pan", "eq"], [1, 2, 3],
            Mono(chords)),
        ("full-mix", "The drum loop, bass line and chords mixed in stereo.", "mix", ["full-mix", "drums", "bass", "chords", "synth"], ["level", "eq"], [2, 3],
            new PcmAudio(Rate, 2, Fit(full, 2))),
    };

    List<JsonObject> sources = File.Exists(catalogPath) ? ReadEntries() : [];
    foreach (var g in generated)
    {
        File.WriteAllBytes(Path.Combine(root, $"{g.Key}.wav"), WavFile.Write(g.Audio));
        var entry = new AudioSource(g.Key, g.Description, g.Kind, g.Tags, g.Uses, g.Difficulties, AudioSourceRules.Generated,
            new AudioSourceLicense("MIT", "AcademiaAuditiva contributors", "https://opensource.org/license/mit"),
            null!);
        var node = JsonSerializer.SerializeToNode(entry, JsonOptions)!.AsObject();
        var at = sources.FindIndex(s => (string?)s["key"] == g.Key);
        if (at >= 0) sources[at] = node; else sources.Add(node);
    }
    WriteMeasured(sources);
}

void Ingest(string key, string file)
{
    var sources = ReadEntries();
    var entry = sources.Find(s => (string?)s["key"] == key)
        ?? throw new InvalidDataException($"Add the entry for '{key}' to {catalogPath} first: everything but \"audio\".");
    if ((string?)entry["origin"] != AudioSourceRules.External)
        throw new InvalidDataException($"'{key}' is not an external source.");

    var audio = WavFile.Read(File.ReadAllBytes(file));
    var bytes = WavFile.Write(audio with { Samples = Fit(audio.Samples, audio.Channels) });
    var problems = AudioSourceRules.CheckAudio(AudioSourceMeasurement.Of(bytes)).ToList();
    if (problems.Count > 0)
        throw new InvalidDataException($"{file} can't be a source: {string.Join("; ", problems)}.");

    File.WriteAllBytes(Path.Combine(root, $"{key}.wav"), bytes);
    WriteMeasured(sources);
}

void Measure() => WriteMeasured(ReadEntries());

// Fills in every entry's measurements, checks every rule, and only then writes the catalog.
void WriteMeasured(List<JsonObject> entries)
{
    foreach (var entry in entries)
    {
        var key = (string?)entry["key"] ?? throw new InvalidDataException("A source has no key.");
        var path = Path.Combine(root, $"{key}.wav");
        if (!File.Exists(path)) throw new InvalidDataException($"{key}: {path} is missing.");
        entry["audio"] = JsonSerializer.SerializeToNode(AudioSourceMeasurement.Of(File.ReadAllBytes(path)), JsonOptions);
    }

    var json = AudioSourceCatalog.Serialize(AudioSourceCatalog.Parse(new JsonObject { ["sources"] = new JsonArray([.. entries]) }.ToJsonString()));
    var listed = entries.Select(e => $"{(string?)e["key"]}.wav").ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unlisted = Directory.GetFiles(root, "*.wav").Select(Path.GetFileName).Where(f => !listed.Contains(f!)).ToList();
    if (unlisted.Count > 0)
        throw new InvalidDataException($"Not in {AudioSourceCatalog.FileName}: {string.Join(", ", unlisted)}.");

    File.WriteAllText(catalogPath, json);
    foreach (var source in AudioSourceCatalog.Parse(json))
        Console.WriteLine($"{source.Key,-14} {source.Audio.Channels}ch {source.Audio.DurationSeconds,5:F2} s  {source.Audio.LoudnessLufs,6:F2} LUFS  peak {source.Audio.PeakDbfs,6:F2} dBFS");
}

List<JsonObject> ReadEntries() =>
    JsonNode.Parse(File.ReadAllText(catalogPath), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip })?["sources"]?.AsArray()
        .Select(n => n!.AsObject().DeepClone().AsObject()).ToList()
    ?? throw new InvalidDataException($"{catalogPath} has no sources.");

static string FindSourcesFolder()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "AcademiaAuditiva", "Audio", "Sources");
        if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(candidate)) return candidate;
    }
    throw new InvalidDataException("Run this from the AcademiaAuditiva repository.");
}

// ---- Levels -------------------------------------------------------------------------------

static PcmAudio Mono(float[] samples) => new(Rate, 1, Fit(samples, 1));

// Sets the loudness to TargetLufs, or lower when the peak would pass PeakCeilingDbfs, after
// fading the ends so the clip neither starts nor stops with a click.
static float[] Fit(float[] samples, int channels)
{
    var faded = Faded(samples, channels);
    var audio = new PcmAudio(Rate, channels, faded);
    var gainDb = Math.Min(TargetLufs - Loudness.IntegratedLufs(audio), PeakCeilingDbfs - Loudness.SamplePeakDbfs(audio));
    return Scaled(faded, gainDb);
}

// <paramref name="part"/> scaled by the gain that sets <paramref name="whole"/> to the loudness.
static float[] Normalized(float[] whole, double lufs, float[]? part = null) =>
    Scaled(part ?? whole, lufs - Loudness.IntegratedLufs(new PcmAudio(Rate, 1, whole)));

static float[] Scaled(float[] samples, double gainDb)
{
    var gain = (float)Math.Pow(10, gainDb / 20);
    return [.. samples.Select(s => s * gain)];
}

static float[] Faded(float[] samples, int channels)
{
    var result = (float[])samples.Clone();
    var frames = samples.Length / channels;
    var fadeIn = (int)(0.005 * Rate);
    var fadeOut = (int)(0.05 * Rate);
    for (var f = 0; f < frames; f++)
    {
        var gain = Math.Min(1.0, Math.Min(f / (double)fadeIn, (frames - 1 - f) / (double)fadeOut));
        for (var c = 0; c < channels; c++) result[f * channels + c] *= (float)gain;
    }
    return result;
}

static float[] Sum(List<float[]> parts)
{
    var result = new float[parts.Max(p => p.Length)];
    foreach (var part in parts)
        for (var i = 0; i < part.Length; i++) result[i] += part[i];
    return result;
}

// Constant-power panning: -1 is left, 0 center (-3 dB each side), 1 right.
static void AddPanned(float[] stereo, float[] mono, double pan)
{
    var angle = (pan + 1) * Math.PI / 4;
    var (left, right) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
    for (var f = 0; f < mono.Length && 2 * f + 1 < stereo.Length; f++)
    {
        stereo[2 * f] += mono[f] * left;
        stereo[2 * f + 1] += mono[f] * right;
    }
}

// ---- Synthesis ------------------------------------------------------------------------------

// Paul Kellet's refined pink filter over white noise: within 0.05 dB of -3 dB/octave above 10 Hz.
static float[] PinkNoise(Random64 random, double seconds)
{
    var result = new float[(int)(seconds * Rate)];
    double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
    for (var i = 0; i < result.Length; i++)
    {
        var white = random.NextSigned();
        b0 = 0.99886 * b0 + white * 0.0555179;
        b1 = 0.99332 * b1 + white * 0.0750759;
        b2 = 0.96900 * b2 + white * 0.1538520;
        b3 = 0.86650 * b3 + white * 0.3104856;
        b4 = 0.55000 * b4 + white * 0.5329522;
        b5 = -0.7616 * b5 - white * 0.0168980;
        result[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362);
        b6 = white * 0.115926;
    }
    return result;
}

// Four bars of 4/4 at 120 bpm (8 s): kick on 1 and 3 (and the "and" of 3 in bars 2 and 4),
// snare on 2 and 4, a hi-hat on every eighth, accented on the beat.
static float[] Drums(Random64 random)
{
    const double beat = 0.5;
    var result = new float[(int)(16 * beat * Rate)];
    for (var bar = 0; bar < 4; bar++)
    {
        var start = bar * 4 * beat;
        Add(result, Kick(), start);
        Add(result, Kick(), start + 2 * beat);
        if (bar % 2 == 1) Add(result, Kick(), start + 2.5 * beat, 0.7f);
        Add(result, Snare(random), start + beat);
        Add(result, Snare(random), start + 3 * beat);
        for (var eighth = 0; eighth < 8; eighth++)
            Add(result, HiHat(random), start + eighth * beat / 2, eighth % 2 == 0 ? 0.5f : 0.3f);
    }
    return result;
}

// A sine falling from 150 Hz to 50 Hz, with a short click on top.
static float[] Kick()
{
    var result = new float[(int)(0.45 * Rate)];
    var phase = 0.0;
    for (var i = 0; i < result.Length; i++)
    {
        var t = i / (double)Rate;
        phase += 2 * Math.PI * (50 + 100 * Math.Exp(-t / 0.04)) / Rate;
        result[i] = (float)(Math.Sin(phase) * Math.Exp(-t / 0.18) + 0.3 * Math.Sin(2 * Math.PI * 3000 * t) * Math.Exp(-t / 0.002));
    }
    return result;
}

// Noise for the wires over a 185 Hz body.
static float[] Snare(Random64 random)
{
    var result = new float[(int)(0.3 * Rate)];
    var previous = 0.0;
    for (var i = 0; i < result.Length; i++)
    {
        var t = i / (double)Rate;
        var noise = random.NextSigned();
        var bright = noise - 0.6 * previous;
        previous = noise;
        result[i] = (float)(0.55 * bright * Math.Exp(-t / 0.07) + 0.6 * Math.Sin(2 * Math.PI * 185 * t) * Math.Exp(-t / 0.05));
    }
    return result;
}

// White noise through a second-order difference: little left below a few kHz.
static float[] HiHat(Random64 random)
{
    var result = new float[(int)(0.08 * Rate)];
    double x1 = 0, x2 = 0;
    for (var i = 0; i < result.Length; i++)
    {
        var x = random.NextSigned();
        result[i] = (float)((x - 2 * x1 + x2) / 4 * Math.Exp(-i / (double)Rate / 0.018));
        (x2, x1) = (x1, x);
    }
    return result;
}

// One bar per chord root (A, F, C, G) at 120 bpm: root, root, octave, root on the beats.
static float[] Bass()
{
    double[] roots = [55.0, 43.65, 65.41, 49.0];
    var result = new float[(int)(8.0 * Rate)];
    for (var bar = 0; bar < 4; bar++)
    {
        double[] pattern = [1, 1, 2, 1];
        for (var b = 0; b < 4; b++)
            Add(result, Saw(roots[bar] * pattern[b], 0.45, harmonicsUpTo: 2500, rolloff: 1.3, attack: 0.005, release: 0.05), bar * 2.0 + b * 0.5);
    }
    return result;
}

// The three voices of Am, F, C and G, two seconds each, kept apart so a mix can spread them.
static List<float[]> ChordVoices()
{
    double[][] chords =
    [
        [220.00, 261.63, 329.63], // A3 C4 E4
        [220.00, 261.63, 349.23], // A3 C4 F4
        [196.00, 261.63, 329.63], // G3 C4 E4
        [196.00, 246.94, 293.66], // G3 B3 D4
    ];
    var voices = new List<float[]>();
    for (var v = 0; v < 3; v++)
    {
        var voice = new float[(int)(8.0 * Rate)];
        for (var c = 0; c < chords.Length; c++)
        {
            // Two saws a few cents apart give the pad its slow beating.
            foreach (var detune in new[] { -6.0, 6.0 })
                Add(voice, Saw(chords[c][v] * Math.Pow(2, detune / 1200), 2.0, harmonicsUpTo: 6000, rolloff: 1.6, attack: 0.12, release: 0.25), c * 2.0);
        }
        voices.Add(voice);
    }
    return voices;
}

// A sawtooth summed from its harmonics below harmonicsUpTo (so nothing aliases), each falling
// by 1/k^rolloff, under an attack-sustain-release envelope that ends with the note.
static float[] Saw(double frequency, double seconds, double harmonicsUpTo, double rolloff, double attack, double release)
{
    var result = new float[(int)(seconds * Rate)];
    var count = (int)(harmonicsUpTo / frequency);
    for (var k = 1; k <= count; k++)
    {
        var amplitude = 1 / Math.Pow(k, rolloff);
        var step = 2 * Math.PI * k * frequency / Rate;
        for (var i = 0; i < result.Length; i++) result[i] += (float)(amplitude * Math.Sin(step * i));
    }
    for (var i = 0; i < result.Length; i++)
    {
        var t = i / (double)Rate;
        result[i] *= (float)Math.Clamp(Math.Min(t / attack, (seconds - t) / release), 0, 1);
    }
    return result;
}

static void Add(float[] into, float[] sound, double atSeconds, float gain = 1)
{
    var start = (int)Math.Round(atSeconds * Rate);
    for (var i = 0; i < sound.Length && start + i < into.Length; i++) into[start + i] += sound[i] * gain;
}

partial class Program
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>SplitMix64: the same numbers on every machine and every .NET version.</summary>
sealed class Random64(ulong seed)
{
    private ulong _state = seed;

    /// <summary>Uniform in [-1, 1).</summary>
    public double NextSigned()
    {
        var z = _state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        z ^= z >> 31;
        return (z >> 11) * (2.0 / (1UL << 53)) - 1;
    }
}
