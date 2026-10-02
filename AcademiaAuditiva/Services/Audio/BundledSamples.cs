using System.Text.RegularExpressions;

namespace AcademiaAuditiva.Services.Audio;

/// <summary>
/// The samples of every instrument but the piano ship with the app, as
/// <c>Audio/Instruments/{folder}/{note}.mp3</c> named like the piano's
/// (<see cref="PianoSamples"/>), and are read from disk instead of blob storage.
/// Audio/Instruments/LICENSE.txt credits their source, and
/// scripts/build-instrument-samples.ps1 builds them.
/// </summary>
public sealed partial class BundledSamples
{
    /// <param name="root">The <c>Audio/Instruments</c> folder.</param>
    public BundledSamples(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }

    /// <summary>
    /// Whether <paramref name="sampleName"/> is a bundled sample (<c>guitar/C4.mp3</c>)
    /// rather than a piano blob (<c>C4.mp3</c>).
    /// </summary>
    public static bool IsBundled(string sampleName) => sampleName.Contains('/');

    /// <summary>Opens the bundled sample <paramref name="sampleName"/>.</summary>
    /// <exception cref="ArgumentException">No bundled sample has that name.</exception>
    public Stream Open(string sampleName) =>
        new FileStream(PathOf(sampleName), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

    /// <summary>The samples of the bundled <paramref name="instrument"/> that are not on disk.</summary>
    public IEnumerable<string> Missing(Instrument instrument) =>
        Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
            .Select(instrument.SampleName)
            .Where(name => !File.Exists(PathOf(name)));

    // The planner makes the names, not the request, but they become paths:
    // only an instrument folder and a note file get through.
    private string PathOf(string sampleName)
    {
        var parts = sampleName.Split('/');
        if (parts.Length != 2
            || !Instrument.Bundled.Any(i => i.Folder == parts[0])
            || !NoteFile().IsMatch(parts[1]))
        {
            throw new ArgumentException($"'{sampleName}' is not a bundled sample.", nameof(sampleName));
        }

        return Path.Combine(Root, parts[0], parts[1]);
    }

    [GeneratedRegex(@"^[A-G]s?[1-7]\.mp3\z")]
    private static partial Regex NoteFile();
}
