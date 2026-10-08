using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Storage deletes a mixed clip about a day after its last write, while the app can
/// run for days. The mixer must therefore never hand out the name of a clip that may
/// be gone: it rechecks, and touches, any mix it has not written for half a day.
/// When it does mix, the samples of the other instruments come from the app itself.
/// </summary>
public class AudioMixerServiceTests
{
    private const string MixedContainer = "piano-audio-mixed";
    private const string FifthMix = "mix-5ef716d444d62c7197b41e1997f63d9b183699d6ad9bcf61edd5e1cadcd7bba7.wav";
    private const int SampleRate = 44100;

    private static readonly DateTimeOffset Start = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly MixInput[] Fifth = [new("C4.mp3", 0, 1.5), new("G4.mp3", 1.75, 2.0)];

    private readonly ManualClock _clock = new(Start);
    private readonly Mock<BlobContainerClient> _mixedContainer = new();
    private readonly Mock<BlobClient> _mixedBlob = new();
    private readonly List<string> _downloads = [];
    private readonly BlobServiceClient _blobService;
    private readonly AudioMixerService _mixer;

    public AudioMixerServiceTests()
    {
        _mixedContainer.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(_mixedBlob.Object);
        _mixedBlob.Setup(b => b.SetMetadataAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobInfo(new ETag("\"1\""), Start), Mock.Of<Response>()));

        // Mixing starts by downloading the samples; stopping there shows a new mix was needed.
        var sourceContainer = new Mock<BlobContainerClient>();
        sourceContainer.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns((string name) =>
        {
            var sample = new Mock<BlobClient>();
            sample.Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback(() => _downloads.Add(name))
                .ThrowsAsync(new MixingStarted());
            return sample.Object;
        });

        var blobService = new Mock<BlobServiceClient>();
        blobService.Setup(s => s.GetBlobContainerClient(MixedContainer)).Returns(_mixedContainer.Object);
        blobService.Setup(s => s.GetBlobContainerClient("piano-audio")).Returns(sourceContainer.Object);
        _blobService = blobService.Object;
        _mixer = Mixer(new AudioSourceLibrary(TempAudioSources.BundledRoot));
    }

    private AudioMixerService Mixer(AudioSourceLibrary sources) =>
        new(_blobService, new BundledSamples(BundledSamplesTests.Root), sources, _clock, NullLogger<AudioMixerService>.Instance);

    [Fact]
    public async Task FreshMix_IsReused_WithoutAskingStorageAgain()
    {
        StoredMixWrittenAt(Start - TimeSpan.FromHours(1));

        var first = await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(10));
        var second = await _mixer.MixAsync(Fifth);

        first.Should().Be(new MixedAudio(MixedContainer, FifthMix));
        second.Should().Be(first);
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyTouched(Times.Never());
        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task OldMix_IsTouched_SoStorageKeepsIt()
    {
        StoredMixWrittenAt(Start - TimeSpan.FromHours(13));

        var first = await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(1));
        var second = await _mixer.MixAsync(Fifth);

        second.Should().Be(first);
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyTouched(Times.Once(), at: Start);
        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task RememberedMix_IsCheckedAgain_HalfADayAfterItsLastWrite()
    {
        StoredMixWrittenAt(Start);

        await _mixer.MixAsync(Fifth);
        _clock.Advance(TimeSpan.FromHours(12));
        var later = await _mixer.MixAsync(Fifth);

        later.Should().Be(new MixedAudio(MixedContainer, FifthMix));
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        VerifyTouched(Times.Once(), at: Start + TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task DeletedMix_IsMixedAgain()
    {
        StorageAnswers(NotFound());

        await FluentActions.Awaiting(() => _mixer.MixAsync(Fifth)).Should().ThrowAsync<MixingStarted>();

        _downloads.Should().Equal("C4.mp3");
        VerifyTouched(Times.Never());
    }

    [Fact]
    public async Task OtherStorageErrors_AreNotMistakenForADeletedMix()
    {
        StorageAnswers(new RequestFailedException(403, "This request is not authorized.", "AuthorizationFailure", null));

        (await FluentActions.Awaiting(() => _mixer.MixAsync(Fifth)).Should().ThrowAsync<RequestFailedException>())
            .Which.Status.Should().Be(403);

        _downloads.Should().BeEmpty();
    }

    // Before, a pt-BR or fr-CA request wrote "0,0000" into the plan and so stored
    // a second copy of the same mix under another name.
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public async Task MixName_DependsOnlyOnThePlan_NotOnTheLanguage(string culture)
    {
        StoredMixWrittenAt(Start);
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            (await _mixer.MixAsync(Fifth)).Should().Be(new MixedAudio(MixedContainer, FifthMix));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        _mixedContainer.Verify(c => c.GetBlobClient(FifthMix), Times.Once);
    }

    [Fact]
    public async Task GuitarNote_IsReadFromTheApp_AndFadesOutInsteadOfClicking()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();

        await _mixer.MixAsync([new("guitar/C4.mp3", 0, 1.5)]);

        _downloads.Should().BeEmpty("the guitar samples ship with the app");
        var mix = Wav.Parse(uploaded());
        mix.SampleRate.Should().Be(SampleRate);
        mix.Channels.Should().Be(1);
        mix.Samples.Should().HaveCount((int)(1.5 * SampleRate));
        Peak(mix.Samples.Take(SampleRate / 2)).Should().BeGreaterThan(0.05f, "the note is heard");
        Peak(mix.Samples.TakeLast(10)).Should().BeLessThan(0.01f, "the note is cut short: it fades out over its last 25 ms");
    }

    [Fact]
    public async Task NotesAddingUpPastFullScale_AreScaledDown_InsteadOfClipped()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        await _mixer.MixAsync([new("violin/C5.mp3", 0, 1.5)]);
        var single = Wav.Parse(uploaded()).Samples;

        await _mixer.MixAsync([.. Enumerable.Repeat(new MixInput("violin/C5.mp3", 0, 1.5), 4)]);
        var loud = Wav.Parse(uploaded()).Samples;

        (Peak(single) * 4).Should().BeGreaterThan(0.98f, "four of these notes add up past full scale");
        Peak(loud).Should().BeApproximately(0.98f, 0.0005f);
        var scale = 0.98f / Peak(single);
        loud.Zip(single, (l, s) => Math.Abs(l - s * scale)).Max()
            .Should().BeLessThan(0.0005f, "the whole mix is scaled down, so the notes keep their shape");
        _downloads.Should().BeEmpty();
    }

    // "In tune or not?" plays a note and then the same note a few cents off.
    [Theory]
    [InlineData(50)]
    [InlineData(25)]
    [InlineData(10)]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(-50)]
    public async Task ShiftedNote_SoundsThatManyCentsAway(double cents)
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        await _mixer.MixAsync([new("guitar/A4.mp3", 0, 1.5)]);
        var original = Wav.Parse(uploaded()).Samples;

        await _mixer.MixAsync([new("guitar/A4.mp3", 0, 1.5, cents)]);
        var shifted = Wav.Parse(uploaded()).Samples;

        shifted.Should().HaveCount((int)(1.5 * SampleRate), "the note still lasts as long as the plan says");
        // The same stretch of the recording, which the shifted note plays sooner or later.
        var ratio = Math.Pow(2, cents / 1200);
        var measured = 1200 * Math.Log2(Period(original, 0.4, 0.9) / Period(shifted, 0.4 / ratio, 0.9 / ratio));
        measured.Should().BeApproximately(cents, 0.5);
    }

    [Fact]
    public void ShiftedNote_KeepsItsHighPartials()
    {
        // An 8 kHz partial, high among a note's overtones: interpolating
        // linearly between samples would lose about a tenth of it.
        var sine = Enumerable.Range(0, SampleRate)
            .Select(i => (float)Math.Sin(2 * Math.PI * 8000 * i / SampleRate)).ToArray();

        var shifted = AudioMixerService.ShiftPitch(sine, channels: 1, cents: 25, maxFrames: null);

        var rms = Math.Sqrt(shifted.Skip(100).Take(SampleRate / 2).Average(s => (double)s * s));
        rms.Should().BeApproximately(Math.Sqrt(0.5), 0.005);
    }

    [Fact]
    public async Task ShiftedNote_IsMixedUnderItsOwnName()
    {
        StoredMixWrittenAt(Start);

        var names = new List<string>();
        foreach (var cents in new[] { 0, 10, -10 })
        {
            names.Add((await _mixer.MixAsync([new("C4.mp3", 0, 1.5, cents)])).BlobName);
        }

        names.Should().OnlyHaveUniqueItems();
        (await _mixer.MixAsync([new("C4.mp3", 0, 1.5)])).BlobName.Should().Be(names[0]);
    }

    [Theory]
    [InlineData(1201)]
    [InlineData(-1201)]
    [InlineData(double.NaN)]
    public async Task NotesAreShifted_ByAnOctaveAtMost(double cents)
    {
        await FluentActions.Awaiting(() => _mixer.MixAsync([new("guitar/A4.mp3", 0, 1, cents)]))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("guitar/../../appsettings.json")]
    [InlineData("drums/C4.mp3")]
    public async Task OnlyInstrumentSamples_AreReadFromTheApp(string sampleName)
    {
        StorageAnswers(NotFound());

        await FluentActions.Awaiting(() => _mixer.MixAsync([new(sampleName, 0, 1)])).Should().ThrowAsync<ArgumentException>();

        _downloads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pink-noise", 1)]
    [InlineData("full-mix", 2)]
    public async Task AudioSource_IsReadFromTheApp_AsRecorded(string key, int channels)
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        var sources = new AudioSourceLibrary(TempAudioSources.BundledRoot);
        var source = await sources.ReadAsync(sources.Find(key)!);

        await _mixer.MixAsync([new(AudioSourceLibrary.SampleName(key), 0)]);

        _downloads.Should().BeEmpty("the audio sources ship with the app");
        var mix = Wav.Parse(uploaded());
        mix.SampleRate.Should().Be(SampleRate);
        mix.Channels.Should().Be(channels);
        mix.Samples.Should().HaveCount(source.Samples.Length);
        // All but the last 25 ms, which the mixer fades out as it does every input.
        var compared = source.Samples.Length - (int)(0.025 * SampleRate) * channels;
        mix.Samples.Take(compared).Zip(source.Samples, (m, s) => Math.Abs(m - s)).Max().Should().BeLessThan(0.0002f);
    }

    [Fact]
    public async Task ReplacedAudioSource_IsMixedUnderANewName()
    {
        StoredMixWrittenAt(Start);
        MixInput[] plan = [new(AudioSourceLibrary.SampleName("pink-noise"), 0, 2)];
        var original = (await _mixer.MixAsync(plan)).BlobName;

        using var folder = new TempAudioSources();
        var library = new AudioSourceLibrary(folder.Root);
        var pink = await library.ReadAsync(library.Find("pink-noise")!);
        (await Mixer(library).MixAsync(plan)).BlobName.Should().Be(original, "the recording is the same");

        folder.Replace("pink-noise", pink with { Samples = [.. pink.Samples.Select(s => s / 2)] });

        (await Mixer(new AudioSourceLibrary(folder.Root)).MixAsync(plan)).BlobName.Should().NotBe(original);
    }

    [Theory]
    [InlineData("source:")]
    [InlineData("source:unknown")]
    [InlineData("source:../appsettings")]
    [InlineData("source:pink-noise.wav")]
    public async Task OnlyListedAudioSources_AreRead(string sampleName)
    {
        StorageAnswers(NotFound());

        await FluentActions.Awaiting(() => _mixer.MixAsync([new(sampleName, 0, 1)])).Should().ThrowAsync<ArgumentException>();

        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessedClip_IsTheSourceThroughItsProcessors_Untrimmed()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        var sources = new AudioSourceLibrary(TempAudioSources.BundledRoot);
        var pink = await sources.ReadAsync(sources.Find("pink-noise")!);
        AudioProcessor[] chain = [new GainProcessor(-6), new PanProcessor(0.5)];

        var clip = await _mixer.RenderAsync(new("pink-noise", chain));

        clip.Container.Should().Be(MixedContainer);
        clip.BlobName.Should().MatchRegex($"^{AudioMixerService.ProcessedBlobPrefix}[0-9a-f]{{64}}\\.wav$");
        _downloads.Should().BeEmpty("the audio sources ship with the app");
        var wav = Wav.Parse(uploaded());
        var expected = AudioProcessing.Process(pink, chain);
        wav.SampleRate.Should().Be(SampleRate);
        wav.Channels.Should().Be(2);
        wav.Samples.Should().HaveCount(expected.Samples.Length, "a processed clip is neither trimmed nor padded");
        // Every sample, the last ones too: unlike a note, a processed clip is not faded out.
        wav.Samples.Zip(expected.Samples, (w, e) => Math.Abs(w - e)).Max().Should().BeLessThan(0.0001f);
    }

    [Fact]
    public async Task ProcessedClip_IsTheSameEveryTime()
    {
        StorageAnswers(NotFound());
        var uploaded = CaptureUploads();
        AudioProcessingPlan plan = new("bass-line", [new GainProcessor(2.5), new PanProcessor(-0.3)]);

        await _mixer.RenderAsync(plan);
        var first = uploaded();
        await Mixer(new AudioSourceLibrary(TempAudioSources.BundledRoot)).RenderAsync(plan);

        uploaded().Should().Equal(first);
    }

    [Fact]
    public async Task SameProcessingPlan_ReusesTheStoredClip()
    {
        StoredMixWrittenAt(Start);

        var first = await _mixer.RenderAsync(new("pink-noise", [new GainProcessor(-6)]));
        var second = await _mixer.RenderAsync(new("pink-noise", [new GainProcessor(-6)]));

        second.Should().Be(first);
        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mixedBlob.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(), It.IsAny<AccessTier?>(),
            It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangedProcessingPlan_IsStoredUnderANewName()
    {
        StoredMixWrittenAt(Start);
        AudioProcessingPlan plan = new("pink-noise", [new GainProcessor(-6), new PanProcessor(0.5)]);
        var original = (await _mixer.RenderAsync(plan)).BlobName;

        AudioProcessingPlan[] changed =
        [
            plan with { Processors = [new GainProcessor(-6.5), new PanProcessor(0.5)] },
            plan with { Processors = [new GainProcessor(-6), new PanProcessor(-0.5)] },
            plan with { Processors = [new PanProcessor(0.5), new GainProcessor(-6)] },
            plan with { Processors = [new GainProcessor(-6)] },
            new("bass-line", plan.Processors),
        ];

        var names = new List<string> { original };
        foreach (var other in changed)
        {
            names.Add((await _mixer.RenderAsync(other)).BlobName);
        }
        names.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr-CA")]
    public async Task ProcessedClipName_DependsOnlyOnThePlan_NotOnTheLanguage(string culture)
    {
        StoredMixWrittenAt(Start);
        AudioProcessingPlan plan = new("pink-noise", [new GainProcessor(-1.5), new PanProcessor(0.25)]);
        var invariant = (await Mixer(new AudioSourceLibrary(TempAudioSources.BundledRoot)).RenderAsync(plan)).BlobName;

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            (await _mixer.RenderAsync(plan)).BlobName.Should().Be(invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task ReplacedAudioSource_IsProcessedUnderANewName()
    {
        StoredMixWrittenAt(Start);
        AudioProcessingPlan plan = new("pink-noise", [new GainProcessor(-3)]);
        using var folder = new TempAudioSources();
        var library = new AudioSourceLibrary(folder.Root);
        var original = (await Mixer(library).RenderAsync(plan)).BlobName;
        var pink = await library.ReadAsync(library.Find("pink-noise")!);

        folder.Replace("pink-noise", pink with { Samples = [.. pink.Samples.Select(s => s / 2)] });

        (await Mixer(new AudioSourceLibrary(folder.Root)).RenderAsync(plan)).BlobName.Should().NotBe(original);
    }

    [Fact]
    public async Task ProcessingAndMixing_KeepTheirNamesApart()
    {
        StoredMixWrittenAt(Start);

        var processed = await _mixer.RenderAsync(new("pink-noise", []));
        var mixed = await _mixer.MixAsync([new(AudioSourceLibrary.SampleName("pink-noise"), 0)]);

        processed.BlobName.Should().StartWith(AudioMixerService.ProcessedBlobPrefix);
        mixed.BlobName.Should().StartWith("mix-");
        (await _mixer.MixAsync(Fifth)).BlobName.Should().Be(FifthMix, "rendering leaves the names of mixes alone");
    }

    public static TheoryData<string, AudioProcessingPlan> InvalidProcessingPlans => new()
    {
        { "an unknown source", new("unknown", [new GainProcessor(-6)]) },
        { "a path for a source", new("../sources", []) },
        { "no source", new(null!, []) },
        { "a gain that is not a number", new("pink-noise", [new GainProcessor(double.NaN)]) },
        { "an infinite gain", new("pink-noise", [new GainProcessor(double.PositiveInfinity)]) },
        { "an out-of-range pan", new("pink-noise", [new PanProcessor(-2)]) },
        { "a gain that would clip", new("pink-noise", [new GainProcessor(12)]) },
        { "a stereo source panned", new("full-mix", [new PanProcessor(0.5)]) },
    };

    [Theory]
    [MemberData(nameof(InvalidProcessingPlans))]
    public async Task InvalidProcessingPlans_AreRefused_BeforeAnythingIsReadOrStored(string why, AudioProcessingPlan plan)
    {
        StorageAnswers(NotFound());

        await FluentActions.Awaiting(() => _mixer.RenderAsync(plan)).Should().ThrowAsync<ArgumentException>(why);

        _mixedBlob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
        _mixedContainer.Verify(c => c.GetBlobClient(It.IsAny<string>()), Times.Never);
        _downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedProcessing_IsLogged_WithoutItsParameters()
    {
        var logger = new RecordingLogger();
        var mixer = new AudioMixerService(_blobService, new BundledSamples(BundledSamplesTests.Root),
            new AudioSourceLibrary(TempAudioSources.BundledRoot), _clock, logger);

        await FluentActions.Awaiting(() => mixer.RenderAsync(new("pink-noise", [new PanProcessor(0.375), new GainProcessor(9.75)])))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();

        var entry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        entry.Message.Should().Contain("pink-noise").And.Contain("pan,gain");
        var logged = entry.Message + entry.Exception?.Message;
        logged.Should().NotContain("9.75").And.NotContain("9,75").And.NotContain("0.375").And.NotContain("0,375");
    }

    private static RequestFailedException NotFound() =>
        new(404, "The specified blob does not exist.", "BlobNotFound", null);

    private static float Peak(IEnumerable<float> samples) => samples.Max(Math.Abs);

    /// <summary>
    /// The period, in samples, of an A4 between two times: the lag at which the
    /// signal best matches itself, refined between lags with a parabola.
    /// </summary>
    private static double Period(float[] samples, double from, double to)
    {
        var start = (int)(from * SampleRate);
        var length = (int)((to - from) * SampleRate);
        double Correlation(int lag)
        {
            var sum = 0.0;
            for (var i = 0; i < length; i++)
            {
                sum += samples[start + i] * (double)samples[start + i + lag];
            }
            return sum;
        }

        // A4 repeats about every 100 samples; stay clear of its octaves.
        var best = Enumerable.Range(70, 80).MaxBy(Correlation);
        best.Should().BeInRange(71, 148);
        var (before, at, after) = (Correlation(best - 1), Correlation(best), Correlation(best + 1));
        return best + 0.5 * (before - after) / (before - 2 * at + after);
    }

    /// <summary>Returns the last WAV the mixer uploaded.</summary>
    private Func<byte[]> CaptureUploads()
    {
        byte[]? uploaded = null;
        _mixedBlob.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(), It.IsAny<AccessTier?>(),
                It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .Callback((Stream content, BlobHttpHeaders _, IDictionary<string, string> _, BlobRequestConditions _,
                IProgress<long> _, AccessTier? _, StorageTransferOptions _, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                uploaded = copy.ToArray();
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        return () => uploaded ?? throw new InvalidOperationException("Nothing was uploaded.");
    }

    private void StoredMixWrittenAt(DateTimeOffset lastModified) =>
        _mixedBlob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobProperties(lastModified: lastModified), Mock.Of<Response>()));

    private void StorageAnswers(RequestFailedException error) =>
        _mixedBlob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

    private void VerifyTouched(Times times, DateTimeOffset? at = null) =>
        _mixedBlob.Verify(b => b.SetMetadataAsync(
            It.Is<IDictionary<string, string>>(m => at == null
                || (m.Count == 1
                    && m.ContainsKey("touched")
                    && m["touched"] == at.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()), times);

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MixingStarted : Exception;

    private sealed class RecordingLogger : ILogger<AudioMixerService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    /// <summary>The 16-bit PCM WAV the mixer writes, with its samples back in [-1, 1].</summary>
    private sealed record Wav(int SampleRate, int Channels, float[] Samples)
    {
        public static Wav Parse(byte[] bytes)
        {
            Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
            Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WAVE");
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)).Should().Be(16, "the mix is 16-bit PCM");
            Encoding.ASCII.GetString(bytes, 36, 4).Should().Be("data");
            var dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            dataSize.Should().Be(bytes.Length - 44);

            var samples = new float[dataSize / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + i * 2, 2)) / 32767f;
            }

            return new Wav(
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)),
                samples);
        }
    }
}
