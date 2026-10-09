using System.Security.Cryptography;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// The training recordings of the audio track: <c>Audio/Sources/sources.json</c> and the WAV files
/// next to it, which ship with the app outside wwwroot, so no URL reaches them. The mixer reads them
/// by key (<see cref="SampleName"/>), never by a path or an address from a request.
/// </summary>
public sealed class AudioSourceLibrary
{
    /// <summary>Marks a mixer input as a source rather than an instrument sample.</summary>
    public const string SampleNamePrefix = "source:";

    private readonly Dictionary<string, AudioSource> _byKey;

    /// <param name="root">The <c>Audio/Sources</c> folder.</param>
    /// <exception cref="InvalidDataException">The catalog is malformed, or a source breaks a rule.</exception>
    public AudioSourceLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        Sources = AudioSourceCatalog.Parse(File.ReadAllText(Path.Combine(Root, AudioSourceCatalog.FileName)));
        _byKey = Sources.ToDictionary(s => s.Key, StringComparer.Ordinal);
    }

    public string Root { get; }

    public IReadOnlyList<AudioSource> Sources { get; }

    public AudioSource? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>The mixer input name of the source <paramref name="key"/>.</summary>
    public static string SampleName(string key) => SampleNamePrefix + key;

    public static bool IsSource(string sampleName) => sampleName.StartsWith(SampleNamePrefix, StringComparison.Ordinal);

    /// <summary>The source a mixer input names.</summary>
    /// <exception cref="ArgumentException">No source has that name.</exception>
    public AudioSource Resolve(string sampleName) =>
        IsSource(sampleName) && Find(sampleName[SampleNamePrefix.Length..]) is { } source
            ? source
            : throw new ArgumentException($"'{sampleName}' is not an audio source.", nameof(sampleName));

    /// <summary>Reads a source, provided its file is still the one the catalog measured.</summary>
    /// <exception cref="InvalidDataException">The file changed since it was measured.</exception>
    public async Task<PcmAudio> ReadAsync(AudioSource source, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(PathOf(source), cancellationToken).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != source.Version)
            throw new InvalidDataException($"The audio source '{source.Key}' changed since it was measured: run scripts/audio-sources.cs.");
        return WavFile.Read(bytes);
    }

    /// <summary>The sources whose file is missing or changed since it was measured.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach (var source in Sources)
        {
            var path = PathOf(source);
            if (!File.Exists(path))
                problems.Add($"{source.Key}: {source.FileName} is missing");
            else if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != source.Version)
                problems.Add($"{source.Key}: {source.FileName} changed since it was measured");
        }
        return problems;
    }

    public string PathOf(AudioSource source) => Path.Combine(Root, source.FileName);
}
