namespace AcademiaAuditiva.Interfaces;

/// <summary>
/// Composes one playable audio file from a list of source notes drawn
/// from the <c>piano-audio</c> container. The result is uploaded to a
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
}

/// <summary>
/// One source note placed at a specific offset on the mixer timeline.
/// </summary>
/// <param name="BlobName">Filename in the source container (e.g. <c>C4.mp3</c>).</param>
/// <param name="StartTimeSeconds">Offset from the start of the mix.</param>
/// <param name="DurationSeconds">
/// Optional cap on how much of the source to consume; <c>null</c> means
/// "play the entire sample". Useful for trimming sustained notes inside
/// melodies.
/// </param>
public sealed record MixInput(
    string BlobName,
    double StartTimeSeconds,
    double? DurationSeconds = null);

/// <summary>
/// Address of a mixed audio asset.
/// </summary>
/// <param name="Container">Container holding the blob.</param>
/// <param name="BlobName">Blob name inside <paramref name="Container"/>.</param>
public sealed record MixedAudio(string Container, string BlobName);
