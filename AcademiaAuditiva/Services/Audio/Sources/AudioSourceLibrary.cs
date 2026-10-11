using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// The training recordings of the audio track: bundled catalog WAVs and optional prepared chunks
/// in private Blob Storage. The mixer reads them by a catalog key (<see cref="SampleName"/>), never
/// by a path or an address from a request.
/// </summary>
public sealed partial class AudioSourceLibrary
{
    /// <summary>Marks a mixer input as a source rather than an instrument sample.</summary>
    public const string SampleNamePrefix = "source:";

    private readonly Dictionary<string, AudioSource> _byKey;
    private readonly BlobContainerClient? _storage;
    private readonly object _storageLock = new();
    private IReadOnlyList<AudioSource>? _sources;
    private string? _storageProblem;

    /// <param name="root">The <c>Audio/Sources</c> folder.</param>
    /// <exception cref="InvalidDataException">The catalog is malformed, or a source breaks a rule.</exception>
    public AudioSourceLibrary(string root, BlobContainerClient? storage = null)
    {
        Root = Path.GetFullPath(root);
        var bundled = AudioSourceCatalog.Parse(File.ReadAllText(Path.Combine(Root, AudioSourceCatalog.FileName)));
        _sources = storage is null ? bundled : null;
        _byKey = bundled.ToDictionary(s => s.Key, StringComparer.Ordinal);
        _storage = storage;
    }

    public string Root { get; }

    public IReadOnlyList<AudioSource> Sources
    {
        get
        {
            EnsureStorageSources(throwOnFailure: true);
            return _sources!;
        }
    }

    public AudioSource? Find(string key)
    {
        EnsureStorageSources(throwOnFailure: true);
        return _byKey.GetValueOrDefault(key);
    }

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
        if (source.BlobName is { } blobName)
        {
            if (_storage is null)
                throw new InvalidOperationException($"The remote audio source '{source.Key}' has no storage container.");

            var response = await _storage.GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);
            var remoteBytes = response.Value.Content.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(remoteBytes));
            if (!hash.StartsWith(source.Audio.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"The remote audio source '{source.Key}' does not match its content-addressed name.");
            return WavFile.Read(remoteBytes);
        }

        var bytes = await File.ReadAllBytesAsync(PathOf(source), cancellationToken).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != source.Version)
            throw new InvalidDataException($"The audio source '{source.Key}' changed since it was measured: run scripts/audio-sources.cs.");
        return WavFile.Read(bytes);
    }

    /// <summary>The sources whose file is missing or changed since it was measured.</summary>
    public IReadOnlyList<string> Problems()
    {
        EnsureStorageSources(throwOnFailure: false);
        var problems = new List<string>();
        if (_storageProblem is not null)
            problems.Add(_storageProblem);
        foreach (var source in _byKey.Values.Where(source => !source.IsRemote))
        {
            var path = PathOf(source);
            if (!File.Exists(path))
                problems.Add($"{source.Key}: {source.FileName} is missing");
            else if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != source.Version)
                problems.Add($"{source.Key}: {source.FileName} changed since it was measured");
        }
        return problems;
    }

    public string PathOf(AudioSource source) =>
        source.IsRemote
            ? throw new ArgumentException("A remote audio source has no local path.", nameof(source))
            : Path.Combine(Root, source.FileName);

    private void EnsureStorageSources(bool throwOnFailure)
    {
        if (_sources is not null)
            return;

        lock (_storageLock)
        {
            if (_sources is not null)
                return;

            try
            {
                var remote = _storage!.GetBlobs(BlobTraits.None, BlobStates.None, "chunks/", CancellationToken.None)
                    .Select(item => FromStorageBlob(item.Name))
                    .Where(source => source is not null)
                    .Cast<AudioSource>()
                    .ToArray();
                _storageProblem = null;
                if (remote.Length != 96)
                    _storageProblem = $"{_storage.Name}: expected 96 prepared chunks but found {remote.Length}";
                foreach (var source in remote)
                    _byKey.Add(source.Key, source);
            }
            catch (RequestFailedException ex)
            {
                _storageProblem = $"{_storage!.Name}: cannot list prepared chunks ({ex.ErrorCode ?? ex.Status.ToString()})";
                if (throwOnFailure)
                    throw new InvalidOperationException(_storageProblem, ex);
                return;
            }

            _sources = _byKey.Values.ToArray();
        }
    }

    internal static AudioSource? FromStorageBlob(string blobName)
    {
        var match = StorageBlobPattern().Match(blobName);
        if (!match.Success)
            return null;

        var key = match.Groups["key"].Value;
        var layout = match.Groups["layout"].Value;
        var hashPrefix = match.Groups["hash"].Value;
        var stereo = layout == "stereo";
        return new AudioSource(
            key,
            "Licensed ten-second music excerpt.",
            "mix",
            ["full-mix"],
            stereo ? ["level", "eq"] : ["pan"],
            [1, 2, 3],
            AudioSourceRules.External,
            new AudioSourceLicense(
                "Pixabay Content License",
                RecordingAuthor(key),
                "https://pixabay.com/service/license-summary/",
                "https://pixabay.com/music/"),
            new AudioSourceMeasurement(44100, stereo ? 2 : 1, 441000, -1.5, -23, hashPrefix),
            blobName);
    }

    internal static bool SupportsStorageEqDifficulty(string key, int difficulty)
    {
        if (AdvancedOnlyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            return difficulty == 3;
        if (IntermediateAndAdvancedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            return difficulty >= 2;
        if (UnsupportedEqPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            return false;
        return true;
    }

    private static string RecordingAuthor(string key)
    {
        var recording = key[..key.LastIndexOf("-w", StringComparison.Ordinal)];
        return StorageAuthors.GetValueOrDefault(recording, "Pixabay contributor");
    }

    private static readonly string[] AdvancedOnlyPrefixes =
    [
        "pixabay-571483-", "pixabay-550792-", "pixabay-445661-",
        "pixabay-597971-a78bd1d57612-w02-", "pixabay-554494-",
        "pixabay-609202-7c72deb45dcc-w01-", "pixabay-609202-7c72deb45dcc-w02-",
        "pixabay-537751-632a4576b992-w02-",
    ];

    private static readonly string[] IntermediateAndAdvancedPrefixes =
    [
        "pixabay-513717-46359a8d6a14-w02-",
        "pixabay-550792-5462037249c8-w03-",
        "pixabay-513936-4b4c76157bbd-w02-",
    ];

    private static readonly string[] UnsupportedEqPrefixes =
    [
        "pixabay-554494-dd712bb896f7-w03-",
    ];

    private static readonly IReadOnlyDictionary<string, string> StorageAuthors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pixabay-484622-fe69012eef59"] = "alec_koff",
            ["pixabay-541097-de26e9ce6782"] = "alexguz",
            ["pixabay-571483-065025864943"] = "audiocopper",
            ["pixabay-513717-46359a8d6a14"] = "energysound",
            ["pixabay-513718-3631fce6b19c"] = "energysound",
            ["pixabay-513744-232d9e166653"] = "energysound",
            ["pixabay-412230-a601824f4aaf"] = "fassounds",
            ["pixabay-550792-5462037249c8"] = "grand_project",
            ["pixabay-445661-40ba74cba8bf"] = "kontraa",
            ["pixabay-513936-4b4c76157bbd"] = "lightbeatsmusic",
            ["pixabay-597971-a78bd1d57612"] = "lnplusmusic",
            ["pixabay-323181-434a83706615"] = "lnplusmusic",
            ["pixabay-554494-dd712bb896f7"] = "mickeyscat",
            ["pixabay-609202-7c72deb45dcc"] = "musicdream",
            ["pixabay-551346-6d726554fe43"] = "sigmamusicart",
            ["pixabay-537751-632a4576b992"] = "sigmamusicart",
        };

    [GeneratedRegex(@"^chunks/(?<key>pixabay-[a-z0-9-]+-w0[1-3]-(?<layout>mono|stereo))-(?<hash>[0-9a-f]{12})\.wav\z")]
    private static partial Regex StorageBlobPattern();
}
