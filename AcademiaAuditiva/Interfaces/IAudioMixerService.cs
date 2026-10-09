namespace AcademiaAuditiva.Interfaces;

using AcademiaAuditiva.Services.Audio.Processing;

/// <summary>
/// Composes one playable audio file from a list of source notes: piano
/// samples from the <c>piano-audio</c> container, or samples of another
/// instrument that ship with the app
/// (<see cref="AcademiaAuditiva.Services.Audio.BundledSamples"/>), or the
/// training recordings of the audio track. The result is uploaded to a
/// short-lived container and addressed by an opaque hash-based name —
/// the front-end only ever sees the token issued by
/// <see cref="IAudioTokenService"/>, never the mixed blob name.
/// </summary>
public interface IAudioMixerService
{
    /// <summary>
    /// Mixes <paramref name="inputs"/> into a single 16-bit PCM WAV and
    /// returns its address, understood by the audio streaming endpoint.
    /// Every plan is mixed, even a single note: the endpoint varies each
    /// clip it streams and needs PCM to do it. Identical input sets reuse
    /// a cached mixed blob (the blob name is a SHA-256 of the input plan).
    /// </summary>
    Task<MixedAudio> MixAsync(
        IReadOnlyList<MixInput> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders <paramref name="plan"/>: one training recording of the audio
    /// track through its processors, in order, into a 16-bit PCM WAV, and
    /// returns its address like <see cref="MixAsync"/>. The plan is checked
    /// before any audio is read; malformed or out-of-range parameters throw.
    /// Loudness matching runs only when explicitly requested, and the final
    /// PCM is refused if it would clip, never peak-normalized or replaced by
    /// unprocessed audio. The same plan on the same source and processing-engine
    /// versions reuses the cached clip.
    /// </summary>
    /// <exception cref="ArgumentException">The plan is invalid (see <see cref="AudioProcessing.Validate"/>).</exception>
    /// <exception cref="InvalidOperationException">Matching cannot reach a safe target, or final PCM would clip.</exception>
    Task<MixedAudio> RenderAsync(
        AudioProcessingPlan plan,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One source note placed at a specific offset on the mixer timeline.
/// </summary>
/// <param name="SampleName">
/// A piano sample in the <c>piano-audio</c> container (<c>C4.mp3</c>), a
/// bundled sample of another instrument (<c>guitar/C4.mp3</c>), or a
/// training recording of the audio track (<c>source:pink-noise</c>,
/// <see cref="AcademiaAuditiva.Services.Audio.Sources.AudioSourceLibrary"/>).
/// </param>
/// <param name="StartTimeSeconds">Offset from the start of the mix.</param>
/// <param name="DurationSeconds">
/// Optional cap on how much of the source to consume; <c>null</c> means
/// "play the entire sample". Useful for trimming sustained notes inside
/// melodies.
/// </param>
/// <param name="Cents">
/// Plays the sample this many cents higher (positive) or lower (negative)
/// than it was recorded, up to an octave either way. Zero leaves it as is.
/// </param>
public sealed record MixInput(
    string SampleName,
    double StartTimeSeconds,
    double? DurationSeconds = null,
    double Cents = 0);

/// <summary>
/// Address of a mixed audio asset.
/// </summary>
/// <param name="Container">Container holding the blob.</param>
/// <param name="BlobName">Blob name inside <paramref name="Container"/>.</param>
public sealed record MixedAudio(string Container, string BlobName);
