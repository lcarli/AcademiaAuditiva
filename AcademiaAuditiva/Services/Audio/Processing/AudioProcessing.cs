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
    public const string EngineVersion = "p2";

    public const int MaxProcessors = 8;

    /// <summary>The range of one gain, and of all the gains of a plan added up.</summary>
    public const double MinGainDb = -24;

    public const double MaxGainDb = 12;

    public const double MinEqFrequencyHz = 20;
    public const double MaxEqFrequencyHz = 20000;
    public const double MaxEqFrequencyRateRatio = 0.45;
    public const double MinEqGainDb = -12;
    public const double MaxEqGainDb = 12;
    public const double MinEqQ = 0.25;
    public const double MaxEqQ = 8;

    public const double DefaultLoudnessLufs = -23;

    /// <summary>Each clip is within 0.1 LU of its target, so a matched pair differs by at most 0.2 LU.</summary>
    public const double LoudnessToleranceLu = 0.1;

    /// <summary>
    /// The loudest a plan may make its source, from the source's measured peak.
    /// Just under the mixer's peak guard (0.98, about −0.18 dBFS), with room
    /// for the measurement's rounding to 0.01 dB. A louder plan is refused
    /// rather than scaled down, which would change the level it asks for.
    /// </summary>
    public const double MaxPeakDbfs = -0.2;

    /// <summary>
    /// Checks parameters and returns the final channel count before audio is read.
    /// Gain/pan-only plans also have their predicted peak checked against the
    /// source measurement. EQ and matching need the mixer's measured peak guard:
    /// a filter's transients and the matching gain cannot be known from metadata.
    /// Messages name the limits, never the plan's values.
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
        var result = ValidateProcessors(plan.Processors, source.Audio.SampleRate, source.Audio.Channels);

        // Preserve the gain/pan-only headroom rule. EQ and matching are checked on final PCM.
        if (!result.MeasurePeak && source.Audio.PeakDbfs + result.GainDb > MaxPeakDbfs)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), $"The gains would take the source's peak above {MaxPeakDbfs} dBFS.");
        }
        return result.Channels;
    }

    private static (int Channels, double GainDb, bool MeasurePeak) ValidateProcessors(
        IReadOnlyList<AudioProcessor> processors, int sampleRate, int channels)
    {
        if (processors is null)
        {
            throw new ArgumentException("A plan needs a list of processors.", nameof(processors));
        }
        if (processors.Count > MaxProcessors)
        {
            throw new ArgumentException($"A plan has {MaxProcessors} processors at most.", nameof(processors));
        }

        var totalGainDb = 0.0;
        var measurePeak = false;
        for (var i = 0; i < processors.Count; i++)
        {
            switch (processors[i])
            {
                case GainProcessor gain:
                    if (!(gain.Decibels is >= MinGainDb and <= MaxGainDb))
                    {
                        throw new ArgumentOutOfRangeException(nameof(processors), $"A gain is between {MinGainDb} and {MaxGainDb} dB.");
                    }
                    totalGainDb += gain.Decibels;
                    break;
                case PanProcessor pan:
                    if (!(pan.Position is >= -1 and <= 1))
                    {
                        throw new ArgumentOutOfRangeException(nameof(processors), "A pan position is between -1 and 1.");
                    }
                    if (channels != 1)
                    {
                        throw new ArgumentException("Only a mono signal can be panned.", nameof(processors));
                    }
                    channels = 2;
                    break;
                case PeakingEqProcessor eq:
                    ValidateEq(eq, sampleRate);
                    measurePeak = true;
                    break;
                case LoudnessMatchProcessor match:
                    if (!(match.TargetLufs is >= AudioSourceRules.MinLoudnessLufs and <= AudioSourceRules.MaxLoudnessLufs))
                    {
                        throw new ArgumentOutOfRangeException(nameof(processors),
                            $"A loudness target is between {AudioSourceRules.MinLoudnessLufs} and {AudioSourceRules.MaxLoudnessLufs} LUFS.");
                    }
                    if (i != processors.Count - 1)
                    {
                        throw new ArgumentException("Loudness matching is allowed once, at the end of a plan.", nameof(processors));
                    }
                    measurePeak = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown processor {processors[i]?.GetType().Name ?? "null"}.", nameof(processors));
            }
        }

        if (!(totalGainDb is >= MinGainDb and <= MaxGainDb))
        {
            throw new ArgumentOutOfRangeException(nameof(processors), $"The gains of a plan add up to between {MinGainDb} and {MaxGainDb} dB.");
        }
        return (channels, totalGainDb, measurePeak);
    }

    private static void ValidateEq(PeakingEqProcessor eq, int sampleRate)
    {
        if (!(eq.FrequencyHz is >= MinEqFrequencyHz and <= MaxEqFrequencyHz)
            || eq.FrequencyHz > sampleRate * MaxEqFrequencyRateRatio)
        {
            throw new ArgumentOutOfRangeException(nameof(eq),
                $"An EQ frequency is between {MinEqFrequencyHz} and {MaxEqFrequencyHz} Hz, and at most {MaxEqFrequencyRateRatio} times the sample rate.");
        }
        if (!(eq.GainDb is >= MinEqGainDb and <= MaxEqGainDb))
        {
            throw new ArgumentOutOfRangeException(nameof(eq), $"An EQ gain is between {MinEqGainDb} and {MaxEqGainDb} dB.");
        }
        if (!(eq.Q is >= MinEqQ and <= MaxEqQ))
        {
            throw new ArgumentOutOfRangeException(nameof(eq), $"An EQ Q is between {MinEqQ} and {MaxEqQ}.");
        }
        _ = PeakingFilter(eq, sampleRate);
    }

    /// <summary>
    /// Runs <paramref name="processors"/> in order on <paramref name="audio"/>,
    /// which is left untouched. Call <see cref="Validate"/> first.
    /// </summary>
    public static PcmAudio Process(PcmAudio audio, IReadOnlyList<AudioProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(processors);
        ValidatePcm(audio);
        ValidateProcessors(processors, audio.SampleRate, audio.Channels);
        if (processors.Count == 0)
        {
            return audio;
        }

        // One private working buffer for all same-layout stages, not one full copy per EQ band.
        audio = audio with { Samples = audio.Samples.ToArray() };
        foreach (var processor in processors)
        {
            audio = processor switch
            {
                GainProcessor gain => Gain(audio, gain.Decibels),
                PanProcessor pan => Pan(audio, pan.Position),
                PeakingEqProcessor eq => PeakingEq(audio, eq),
                LoudnessMatchProcessor match => MatchLoudness(audio, match.TargetLufs),
                _ => throw new ArgumentException($"Unknown processor {processor?.GetType().Name ?? "null"}.", nameof(processors)),
            };
        }
        return audio;
    }

    private static void ValidatePcm(PcmAudio audio)
    {
        if (audio.SampleRate != AudioSourceRules.SampleRate || audio.Channels is not (1 or 2)
            || audio.Samples is null || audio.Samples.Length % audio.Channels != 0
            || audio.Frames > AudioSourceRules.MaxSeconds * audio.SampleRate)
        {
            throw new ArgumentException(
                $"Processing needs complete mono or stereo frames at {AudioSourceRules.SampleRate} Hz, lasting at most {AudioSourceRules.MaxSeconds} seconds.",
                nameof(audio));
        }
        foreach (var sample in audio.Samples)
        {
            if (!(Math.Abs(sample) <= 1))
            {
                throw new ArgumentException("Processing needs finite PCM samples within full scale.", nameof(audio));
            }
        }
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
        PeakingEqProcessor eq => $"peaking-eq({Number(eq.FrequencyHz)},{Number(eq.GainDb)},{Number(eq.Q)})",
        LoudnessMatchProcessor match => $"loudness-match({Number(match.TargetLufs)})",
        _ => throw new ArgumentException($"Unknown processor {processor?.GetType().Name ?? "null"}.", nameof(processor)),
    };

    // "R" keeps every distinct value distinct; -0 sounds like 0.
    private static string Number(double value) => (value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture);

    private static PcmAudio Gain(PcmAudio audio, double decibels)
    {
        var gain = LinearGain(decibels);
        for (var i = 0; i < audio.Samples.Length; i++)
        {
            audio.Samples[i] = (float)(audio.Samples[i] * gain);
        }
        return audio;
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

    private static Biquad PeakingFilter(PeakingEqProcessor eq, int sampleRate)
    {
        var a = Math.Pow(10, eq.GainDb / 40);
        var angle = 2 * Math.PI * eq.FrequencyHz / sampleRate;
        var alpha = Math.Sin(angle) / (2 * eq.Q);
        var middle = -2 * Math.Cos(angle);
        return new Biquad(1 + alpha * a, middle, 1 - alpha * a,
            1 + alpha / a, middle, 1 - alpha / a);
    }

    private static PcmAudio PeakingEq(PcmAudio audio, PeakingEqProcessor eq)
    {
        if (eq.GainDb == 0)
        {
            return audio;
        }

        var filters = new Biquad[audio.Channels];
        for (var c = 0; c < filters.Length; c++)
        {
            filters[c] = PeakingFilter(eq, audio.SampleRate);
        }
        for (var f = 0; f < audio.Frames; f++)
        {
            for (var c = 0; c < filters.Length; c++)
            {
                var i = f * audio.Channels + c;
                audio.Samples[i] = (float)filters[c].Next(audio.Samples[i]);
            }
        }
        return audio;
    }

    private static PcmAudio MatchLoudness(PcmAudio audio, double targetLufs)
    {
        var measured = Loudness.IntegratedLufs(audio);
        var totalGainDb = 0.0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!double.IsFinite(measured))
            {
                throw new InvalidOperationException("Loudness matching needs measurable audio above the BS.1770 gate.");
            }

            var correctionDb = targetLufs - measured;
            if (Math.Abs(correctionDb) <= LoudnessToleranceLu)
            {
                return audio;
            }
            totalGainDb += correctionDb;
            if (!(totalGainDb is >= MinGainDb and <= MaxGainDb))
            {
                throw new InvalidOperationException($"Loudness matching needs a gain within {MinGainDb} to {MaxGainDb} dB.");
            }
            Gain(audio, correctionDb);
            measured = Loudness.IntegratedLufs(audio);
        }

        if (!double.IsFinite(measured) || Math.Abs(targetLufs - measured) > LoudnessToleranceLu)
        {
            throw new InvalidOperationException("Loudness matching did not reach the target tolerance.");
        }
        return audio;
    }
}
