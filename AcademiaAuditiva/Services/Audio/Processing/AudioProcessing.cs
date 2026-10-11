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
    public const string EngineVersion = "p5";

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
                case CutFilterProcessor cut:
                    ValidateFilterFrequency(cut.FrequencyHz, sampleRate);
                    _ = CutFilter(cut, sampleRate);
                    measurePeak = true;
                    break;
                case ShelfEqProcessor shelf:
                    ValidateFilterFrequency(shelf.FrequencyHz, sampleRate);
                    if (!(shelf.GainDb is >= MinEqGainDb and <= MaxEqGainDb))
                        throw new ArgumentOutOfRangeException(nameof(processors), "A shelf gain is between -12 and 12 dB.");
                    _ = ShelfFilter(shelf, sampleRate);
                    measurePeak = true;
                    break;
                case WhiteNoiseProcessor noise:
                    if (!(noise.SignalToNoiseDb is >= 0 and <= 60) || noise.Seed <= 0)
                        throw new ArgumentOutOfRangeException(nameof(processors),
                            "White noise needs an SNR from 0 to 60 dB and a positive seed.");
                    measurePeak = true;
                    break;
                case CompressorProcessor compressor:
                    if (!(compressor.ThresholdDbfs is >= -60 and <= 0)
                        || !(compressor.Ratio is >= 1 and <= 20)
                        || !(compressor.AttackMs is >= 0 and <= 200)
                        || !(compressor.ReleaseMs is >= 5 and <= 2000))
                    {
                        throw new ArgumentOutOfRangeException(nameof(processors),
                            "Compression needs a threshold from -60 to 0 dBFS, ratio from 1 to 20, attack from 0 to 200 ms and release from 5 to 2000 ms.");
                    }
                    measurePeak = true;
                    break;
                case StereoWidthProcessor width:
                    if (!(width.Width is >= 0 and <= 2))
                        throw new ArgumentOutOfRangeException(nameof(processors), "Stereo width is between 0 and 2.");
                    if (channels != 2)
                        throw new ArgumentException("Stereo width requires two channels.", nameof(processors));
                    measurePeak = true;
                    break;
                case ChannelPolarityProcessor:
                    if (channels != 2)
                        throw new ArgumentException("Channel polarity requires two channels.", nameof(processors));
                    break;
                case EchoProcessor echo:
                    if (!(echo.DelayMs is >= 20 and <= 1000)
                        || !(echo.Feedback is >= 0 and <= 0.75)
                        || !(echo.WetMix is >= 0 and <= 1))
                        throw new ArgumentOutOfRangeException(nameof(processors),
                            "Echo needs a delay from 20 to 1000 ms, feedback from 0 to 0.75 and wet mix from 0 to 1.");
                    measurePeak = true;
                    break;
                case ReverbProcessor reverb:
                    if (!(reverb.DecaySeconds is >= 0.2 and <= 4)
                        || !(reverb.PreDelayMs is >= 0 and <= 200)
                        || !(reverb.WetMix is >= 0 and <= 1))
                        throw new ArgumentOutOfRangeException(nameof(processors),
                            "Reverb needs a decay from 0.2 to 4 seconds, pre-delay from 0 to 200 ms and wet mix from 0 to 1.");
                    measurePeak = true;
                    break;
                case SaturationProcessor saturation:
                    if (!(saturation.ThresholdDbfs is >= -60 and <= 0))
                        throw new ArgumentOutOfRangeException(nameof(processors), "Saturation threshold is between -60 and 0 dBFS.");
                    measurePeak = true;
                    break;
                case FadeOutProcessor fade:
                    if (!(fade.DurationMs is >= 5 and <= 500))
                        throw new ArgumentOutOfRangeException(nameof(processors), "Fade-out duration is between 5 and 500 ms.");
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
        ValidateFilterFrequency(eq.FrequencyHz, sampleRate);
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

    private static void ValidateFilterFrequency(double frequency, int sampleRate)
    {
        if (!(frequency is >= MinEqFrequencyHz and <= MaxEqFrequencyHz)
            || frequency > sampleRate * MaxEqFrequencyRateRatio)
            throw new ArgumentOutOfRangeException(nameof(frequency),
                $"A filter frequency is between {MinEqFrequencyHz} and {MaxEqFrequencyHz} Hz, and at most {MaxEqFrequencyRateRatio} times the sample rate.");
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
                CutFilterProcessor cut => Filter(audio, () => CutFilter(cut, audio.SampleRate)),
                ShelfEqProcessor shelf => shelf.GainDb == 0 ? audio : Filter(audio, () => ShelfFilter(shelf, audio.SampleRate)),
                WhiteNoiseProcessor noise => WhiteNoise(audio, noise),
                CompressorProcessor compressor => Compress(audio, compressor),
                StereoWidthProcessor width => StereoWidth(audio, width.Width),
                ChannelPolarityProcessor polarity => ChannelPolarity(audio, polarity.InvertRight),
                EchoProcessor echo => TimeBasedAudioProcessing.Echo(audio, echo),
                ReverbProcessor reverb => TimeBasedAudioProcessing.Reverb(audio, reverb),
                SaturationProcessor saturation => Saturate(audio, saturation.ThresholdDbfs),
                FadeOutProcessor fade => FadeOut(audio, fade.DurationMs),
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
        CutFilterProcessor cut => $"cut-filter({Number(cut.FrequencyHz)},{cut.HighPass})",
        ShelfEqProcessor shelf => $"shelf-eq({Number(shelf.FrequencyHz)},{Number(shelf.GainDb)},{shelf.HighShelf})",
        WhiteNoiseProcessor noise => $"white-noise({Number(noise.SignalToNoiseDb)},{noise.Seed})",
        CompressorProcessor compressor => $"compressor({Number(compressor.ThresholdDbfs)},{Number(compressor.Ratio)},{Number(compressor.AttackMs)},{Number(compressor.ReleaseMs)})",
        StereoWidthProcessor width => $"stereo-width({Number(width.Width)})",
        ChannelPolarityProcessor polarity => $"channel-polarity({polarity.InvertRight})",
        EchoProcessor echo => $"echo({Number(echo.DelayMs)},{Number(echo.Feedback)},{Number(echo.WetMix)})",
        ReverbProcessor reverb => $"reverb({Number(reverb.DecaySeconds)},{Number(reverb.PreDelayMs)},{Number(reverb.WetMix)})",
        SaturationProcessor saturation => $"saturation({Number(saturation.ThresholdDbfs)})",
        FadeOutProcessor fade => $"fade-out({Number(fade.DurationMs)})",
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

        return Filter(audio, () => PeakingFilter(eq, audio.SampleRate));
    }

    private static Biquad CutFilter(CutFilterProcessor cut, int sampleRate)
    {
        var angle = 2 * Math.PI * cut.FrequencyHz / sampleRate;
        var cosine = Math.Cos(angle);
        var alpha = Math.Sin(angle) / Math.Sqrt(2);
        var edge = cut.HighPass ? 1 + cosine : 1 - cosine;
        return new Biquad(edge / 2, cut.HighPass ? -edge : edge, edge / 2,
            1 + alpha, -2 * cosine, 1 - alpha);
    }

    private static Biquad ShelfFilter(ShelfEqProcessor shelf, int sampleRate)
    {
        var a = Math.Pow(10, shelf.GainDb / 40);
        var angle = 2 * Math.PI * shelf.FrequencyHz / sampleRate;
        var cosine = Math.Cos(angle);
        var spread = Math.Sin(angle) * Math.Sqrt(2 * a);
        var sum = a + 1;
        var difference = a - 1;
        return shelf.HighShelf
            ? new Biquad(a * (sum + difference * cosine + spread), -2 * a * (difference + sum * cosine),
                a * (sum + difference * cosine - spread), sum - difference * cosine + spread,
                2 * (difference - sum * cosine), sum - difference * cosine - spread)
            : new Biquad(a * (sum - difference * cosine + spread), 2 * a * (difference - sum * cosine),
                a * (sum - difference * cosine - spread), sum + difference * cosine + spread,
                -2 * (difference + sum * cosine), sum + difference * cosine - spread);
    }

    private static PcmAudio Filter(PcmAudio audio, Func<Biquad> create)
    {
        var filters = Enumerable.Range(0, audio.Channels).Select(_ => create()).ToArray();
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

    private static PcmAudio WhiteNoise(PcmAudio audio, WhiteNoiseProcessor noise)
    {
        var power = audio.Samples.Sum(sample => (double)sample * sample) / audio.Samples.Length;
        if (!(power > 0 && double.IsFinite(power)))
            throw new InvalidOperationException("RMS-relative white noise requires non-silent audio.");
        var amplitude = Math.Sqrt(3 * power) * LinearGain(-noise.SignalToNoiseDb);
        var state = (uint)noise.Seed;
        for (var index = 0; index < audio.Samples.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var uniform = 2 * (state / (double)uint.MaxValue) - 1;
            audio.Samples[index] = (float)(audio.Samples[index] + amplitude * uniform);
        }
        return audio;
    }

    private static PcmAudio Compress(PcmAudio audio, CompressorProcessor compressor)
    {
        var attack = compressor.AttackMs == 0 ? 0 : Math.Exp(-1 / (audio.SampleRate * compressor.AttackMs / 1000));
        var release = Math.Exp(-1 / (audio.SampleRate * compressor.ReleaseMs / 1000));
        var envelope = 0.0;
        for (var frame = 0; frame < audio.Frames; frame++)
        {
            var offset = frame * audio.Channels;
            var peak = 0.0;
            for (var channel = 0; channel < audio.Channels; channel++)
                peak = Math.Max(peak, Math.Abs(audio.Samples[offset + channel]));
            var coefficient = peak > envelope ? attack : release;
            envelope = coefficient * envelope + (1 - coefficient) * peak;
            var inputDb = envelope > 0 ? 20 * Math.Log10(envelope) : double.NegativeInfinity;
            var desiredDb = -Math.Max(0, inputDb - compressor.ThresholdDbfs) * (1 - 1 / compressor.Ratio);
            var gain = LinearGain(desiredDb);
            for (var channel = 0; channel < audio.Channels; channel++)
                audio.Samples[offset + channel] = (float)(audio.Samples[offset + channel] * gain);
        }
        return audio;
    }

    private static PcmAudio StereoWidth(PcmAudio audio, double width)
    {
        for (var frame = 0; frame < audio.Frames; frame++)
        {
            var offset = frame * 2;
            var mid = (audio.Samples[offset] + audio.Samples[offset + 1]) / 2.0;
            var side = (audio.Samples[offset] - audio.Samples[offset + 1]) / 2.0 * width;
            audio.Samples[offset] = (float)(mid + side);
            audio.Samples[offset + 1] = (float)(mid - side);
        }
        return audio;
    }

    private static PcmAudio ChannelPolarity(PcmAudio audio, bool invertRight)
    {
        if (invertRight)
            for (var frame = 0; frame < audio.Frames; frame++)
                audio.Samples[frame * 2 + 1] = -audio.Samples[frame * 2 + 1];
        return audio;
    }

    private static PcmAudio Saturate(PcmAudio audio, double thresholdDbfs)
    {
        var threshold = LinearGain(thresholdDbfs);
        for (var sample = 0; sample < audio.Samples.Length; sample++)
            audio.Samples[sample] = (float)(threshold * Math.Tanh(audio.Samples[sample] / threshold));
        return audio;
    }

    private static PcmAudio FadeOut(PcmAudio audio, double durationMs)
    {
        var frames = Math.Min(audio.Frames, (int)Math.Round(audio.SampleRate * durationMs / 1000));
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = frames == 1 ? 0 : (frames - 1 - frame) / (double)(frames - 1);
            var offset = (audio.Frames - frames + frame) * audio.Channels;
            for (var channel = 0; channel < audio.Channels; channel++)
                audio.Samples[offset + channel] = (float)(audio.Samples[offset + channel] * gain);
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
