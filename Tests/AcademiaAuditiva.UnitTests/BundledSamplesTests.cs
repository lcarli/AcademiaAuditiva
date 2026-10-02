using AcademiaAuditiva.Services.Audio;
using NLayer;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The guitar and violin samples ship with the app, so their names become paths:
/// only an instrument folder and a note file may be read, and every note must be there.
/// </summary>
public class BundledSamplesTests
{
    /// <summary>The samples copied next to the tests, as they are next to the app.</summary>
    internal static string Root => Path.Combine(AppContext.BaseDirectory, "Audio", "Instruments");

    private readonly BundledSamples _samples = new(Root);

    public static TheoryData<string> Folders => [.. Instrument.Bundled.Select(i => i.Folder!)];

    [Theory]
    [InlineData("guitar/C4.mp3", true)]
    [InlineData("violin/As6.mp3", true)]
    [InlineData("C4.mp3", false)]
    public void IsBundled_TellsInstrumentSamples_FromPianoBlobs(string sampleName, bool bundled)
    {
        BundledSamples.IsBundled(sampleName).Should().Be(bundled);
    }

    [Fact]
    public async Task Open_ReadsTheSampleFromTheInstrumentFolder()
    {
        await using var stream = _samples.Open("guitar/Cs4.mp3");

        stream.Length.Should().Be(new FileInfo(Path.Combine(Root, "guitar", "Cs4.mp3")).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C4.mp3")]
    [InlineData("../C4.mp3")]
    [InlineData("guitar/..")]
    [InlineData("guitar/../../appsettings.json")]
    [InlineData("/guitar/C4.mp3")]
    [InlineData("guitar/C4.mp3/")]
    [InlineData("guitar/sub/C4.mp3")]
    [InlineData("guitar\\C4.mp3")]
    [InlineData("Guitar/C4.mp3")]
    [InlineData("drums/C4.mp3")]
    [InlineData("guitar/C4.wav")]
    [InlineData("guitar/C8.mp3")]
    [InlineData("guitar/H4.mp3")]
    [InlineData("guitar/C4.mp3\n")]
    public void Open_RefusesAnythingButANoteOfAnInstrument(string sampleName)
    {
        FluentActions.Invoking(() => _samples.Open(sampleName)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void Missing_IsEmpty_WhenEveryNoteShipped(string folder)
    {
        _samples.Missing(Instrument.Bundled.Single(i => i.Folder == folder)).Should().BeEmpty();
    }

    [Fact]
    public void Missing_ListsEveryNote_OfAnEmptyFolder()
    {
        var empty = Directory.CreateTempSubdirectory("aa-samples-");
        try
        {
            new BundledSamples(empty.FullName).Missing(Instrument.Violin).Should()
                .HaveCount(PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
                .And.StartWith("violin/C1.mp3")
                .And.EndWith("violin/B7.mp3");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    // The mixer needs every sample in the piano's format, and never asks for any other file.
    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryNote_IsAMonoMp3_LikeThePiano(string folder)
    {
        var files = Directory.GetFiles(Path.Combine(Root, folder));
        files.Select(Path.GetFileName).Should().BeEquivalentTo(
            Enumerable.Range(PianoSamples.LowestMidi, PianoSamples.HighestMidi - PianoSamples.LowestMidi + 1)
                .Select(PianoSamples.BlobName));

        foreach (var file in files)
        {
            using var mpeg = new MpegFile(file);
            mpeg.SampleRate.Should().Be(44100, file);
            mpeg.Channels.Should().Be(1, file);
            mpeg.Duration.Should().BeGreaterThan(TimeSpan.FromSeconds(1.5), "{0} lasts as long as a note is played", file);

            var start = new float[44100 / 2];
            mpeg.ReadSamples(start, 0, start.Length).Should().Be(start.Length, file);
            start.Max(Math.Abs).Should().BeGreaterThan(0.01f, "{0} is heard as soon as it starts", file);
        }
    }
}
