namespace AcademiaAuditiva.Services.Audio.Processing;

/// <summary>
/// What to do to one training recording of the audio track: its
/// <paramref name="Processors"/> run in order on the decoded source
/// (docs/Audio-Ear-Training.md, "Processing plans"). Rendered by
/// <see cref="Interfaces.IAudioMixerService.RenderAsync"/>, which checks it
/// with <see cref="AudioProcessing.Validate"/> before reading any audio.
/// </summary>
/// <param name="SourceKey">A key of <c>Audio/Sources/sources.json</c>.</param>
public sealed record AudioProcessingPlan(string SourceKey, IReadOnlyList<AudioProcessor> Processors);

/// <summary>
/// One step of an <see cref="AudioProcessingPlan"/>. Its parameters can be
/// the answer of a round, so it is never logged; log <see cref="Name"/>.
/// </summary>
public abstract record AudioProcessor
{
    private protected AudioProcessor()
    {
    }

    /// <summary>What it does, without its parameters.</summary>
    public abstract string Name { get; }
}

/// <summary>Changes the level by <paramref name="Decibels"/>; positive is louder.</summary>
public sealed record GainProcessor(double Decibels) : AudioProcessor
{
    public override string Name => "gain";
}

/// <summary>
/// Places a mono signal in the stereo field at <paramref name="Position"/>:
/// −1 is hard left, 0 the centre and 1 hard right. The pan law keeps the
/// power constant, so the signal is as loud wherever it is placed.
/// </summary>
public sealed record PanProcessor(double Position) : AudioProcessor
{
    public override string Name => "pan";
}

/// <summary>A peaking EQ band, with the requested gain at its centre frequency.</summary>
public sealed record PeakingEqProcessor(double FrequencyHz, double GainDb, double Q) : AudioProcessor
{
    public override string Name => "peaking-eq";
}

/// <summary>
/// Matches BS.1770 integrated loudness to <paramref name="TargetLufs"/>.
/// Explicit and last in a plan: it must not erase a level-training difference.
/// A target that would clip is refused, never replaced by peak normalization.
/// </summary>
public sealed record LoudnessMatchProcessor(double TargetLufs) : AudioProcessor
{
    public override string Name => "loudness-match";
}
