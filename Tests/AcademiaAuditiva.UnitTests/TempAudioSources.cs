using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>A copy of the bundled audio sources in a temporary folder, to be changed by a test.</summary>
internal sealed class TempAudioSources : IDisposable
{
    public static string BundledRoot => Path.Combine(AppContext.BaseDirectory, "Audio", "Sources");

    public TempAudioSources()
    {
        Root = Directory.CreateTempSubdirectory("aa-sources-").FullName;
        foreach (var file in Directory.GetFiles(BundledRoot))
        {
            File.Copy(file, Path.Combine(Root, Path.GetFileName(file)));
        }
    }

    public string Root { get; }

    public string PathOf(string key) => Path.Combine(Root, $"{key}.wav");

    /// <summary>Replaces a source's recording and its measurements, as the script would.</summary>
    public void Replace(string key, PcmAudio audio)
    {
        var bytes = WavFile.Write(audio);
        File.WriteAllBytes(PathOf(key), bytes);
        WriteCatalog(Catalog().Select(s => s.Key == key ? s with { Audio = AudioSourceMeasurement.Of(bytes) } : s));
    }

    public IReadOnlyList<AudioSource> Catalog() =>
        AudioSourceCatalog.Parse(File.ReadAllText(Path.Combine(Root, AudioSourceCatalog.FileName)));

    public void WriteCatalog(IEnumerable<AudioSource> sources) =>
        File.WriteAllText(Path.Combine(Root, AudioSourceCatalog.FileName), AudioSourceCatalog.Serialize(sources));

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
