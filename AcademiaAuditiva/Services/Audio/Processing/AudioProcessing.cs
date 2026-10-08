using System.Globalization;
using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.Services.Audio.Processing;

/// <summary>
/// Checks and runs <see cref="AudioProcessingPlan"/>s on decoded PCM. The
/// processing is deterministic: the same plan on the same source gives the
/// same samples, so its output can be cached by <see cref="Describe"/>.
/// </summary>
public static class AudioProcessing
{
    /// <summary>
    /// Part of every processed clip's name. Change it whenever the same plan
    /// starts to sound different, so clips processed the old way are not reused.
    /// </summary>
    public const string EngineVersion = "p1";

    public const int MaxProcessors = 8;

    /// <summary>The range of one gain, and of all the gains of a plan added up.</summary>
    public const double MinGainDb = -24;

    public const double MaxGainDb = 12;

    /// <summary>
    /// The loudest a plan may make its source, from the source's measured peak.
    /// Just under the mixer's peak guard (0.98, about −0.18 dBFS), with room
    /// for the measurement's rounding to 0.01 dB. A louder plan is refused
    /// rather than scaled down, which would change the level it asks for.
    /// </summary>
    public const double MaxPeakDbfs = -0.2;

    /// <summary>
    /// Throws unless <paramref name="plan"/> can be rendered from
    /// <paramref name="source"/> without clipping, and returns the number of
    /// channels it ends with. Reads no audio: only the source's measurement.
    /// The messages name the limits, never the plan's values.
    /// </summary>
    /// <exception cref="ArgumentException">The plan is malformed or does not suit the source.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range, not a number or infinite.</exception>
    public static int Validate(AudioProcessingPlan plan, AudioSource source)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        if (plan.SourceKey != source.Key)
        {
            throw new ArgumentException("The plan is for another source.", nameof(plan));
        }
        if (plan.Processors is null)
        {
            throw new ArgumentException("A plan needs a list of processors.", nameof(plan));
        }
        if (plan.Processors.Count > MaxProcessors)
        {
            throw new ArgumentException($"A plan has {MaxProcessors} processors at most.", nameof(plan));
        }

        var channels = source.Audio.Channels;
        var totalGainDb = 0.0;
        foreach (var processor in plan.Processors)
        {
            switch (processor)
            {
                case GainProcessor gain:
                    if (!(gain.Decibels is >= MinGainDb and <= MaxGainDb))
                    {
                        throw new ArgumentOutOfRangeException(nameof(plan), $"A gain is between {MinGainDb} and {MaxGainDb} dB.");
                    }
                    totalGainDb += gain.Decibels;
                    break;
                case PanProcessor pan:
                    if (!(pan.Position is >= -1 and <= 1))
                    {
                        throw new ArgumentOutOfRangeException(nameof(plan), "A pan position is between -1 and 1.");
                    }
                    if (channels != 1)
                    {
                        throw new ArgumentException("Only a mono signal can be panned.", nameof(plan));
                    }
                    channels = 2;
                    break;
                default:
                    throw new ArgumentException($"Unknown processor {processor?.GetType().Name ?? "null"}.", nameof(plan));
            }
        }

        if (!(totalGainDb is >= MinGainDb and <= MaxGainDb))
        {
            throw new ArgumentOutOfRangeException(nameof(plan), $"The gains of a plan add up to between {MinGainDb} and {MaxGainDb} dB.");
        }

        // Pan never raises the peak: each side gets at most the whole signal.
        if (source.Audio.PeakDbfs + totalGainDb > MaxPeakDbfs)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), $"The gains would take the source's peak above {MaxPeakDbfs} dBFS.");
        }
        return channels;
    }

    /// <summary>
    /// Runs <paramref name="processors"/> in order on <paramref name="audio"/>,
    /// which is left untouched. Call <see cref="Validate"/> first.
    /// </summary>
    public static PcmAudio Process(PcmAudio audio, IReadOnlyList<AudioProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(processors);
        foreach (var processor in processors)
        {
            audio = processor switch
            {
                GainProcessor gain => Gain(audio, gain.Decibels),
                PanProcessor pan => Pan(audio, pan.Position),
                _ => throw new ArgumentException($"Unknown processor {processor?.GetType().Name ?? "null"}.", nameof(processors)),
            };
        }
        return audio;
    }

    /// <summary>The amplitude ratio of <paramref name="decibels"/>.</summary>
    public static double LinearGain(double decibels) => Math.Pow(10, decibels / 20);

    /// <summary>
    /// The sine/cosine pan law: the left and right gains of
    /// <paramref name="position"/>, whose squares add up to one. The centre
    /// gives each side −3 dB; a hard pan gives one side the whole signal.
    /// </summary>
    public static (double Left, double Right) PanGains(double position)
    {
        var angle = (position + 1) * Math.PI / 4;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>
    /// The text the cache name of <paramref name="plan"/> is made from: the
    /// engine version, the source and its <paramref name="sourceVersion"/>,
    /// and every processor in order. Invariant, so a plan gets the same name
    /// whatever the request culture.
    /// </summary>
    public static string Describe(AudioProcessingPlan plan, string sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"{EngineVersion}:{plan.SourceKey}#{sourceVersion}:{string.Join("|", plan.Processors.Select(Describe))}";
    }

    private static string Describe(AudioProcessor processor) => processor switch
    {
        GainProcessor gain => $"gain({Number(gain.Decibels)})",
        PanProcessor pan => $"pan({Number(pan.Position)})",
        _ => throw new ArgumentException($"Unknown processor {processor?.GetType().Name ?? "null"}.", nameof(processor)),
    };

    // "R" keeps every distinct value distinct; -0 sounds like 0.
    private static string Number(double value) => (value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture);

    private static PcmAudio Gain(PcmAudio audio, double decibels)
    {
        var gain = LinearGain(decibels);
        var samples = new float[audio.Samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(audio.Samples[i] * gain);
        }
        return audio with { Samples = samples };
    }

    private static PcmAudio Pan(PcmAudio audio, double position)
    {
        if (audio.Channels != 1)
        {
            throw new ArgumentException("Only a mono signal can be panned.", nameof(audio));
        }

        var (left, right) = PanGains(position);
        var samples = new float[audio.Samples.Length * 2];
        for (var f = 0; f < audio.Samples.Length; f++)
        {
            samples[2 * f] = (float)(audio.Samples[f] * left);
            samples[2 * f + 1] = (float)(audio.Samples[f] * right);
        }
        return audio with { Channels = 2, Samples = samples };
    }
}
