namespace AcademiaAuditiva.Services.Audio.Sources;

/// <summary>
/// Level measurements: the sample peak, and the integrated loudness of ITU-R BS.1770-4 (LUFS),
/// with its K-weighting and its absolute (-70 LUFS) and relative (-10 LU) gates over 400 ms
/// blocks that overlap by 75 %. Every channel is weighted 1, as left, right and center are.
/// </summary>
public static class Loudness
{
    private const double BlockSeconds = 0.4;
    private const double StepSeconds = 0.1;
    private const double AbsoluteGate = -70;
    private const double RelativeGate = -10;

    /// <summary>The highest absolute sample, in dBFS; negative infinity for silence.</summary>
    public static double SamplePeakDbfs(PcmAudio audio)
    {
        var peak = 0f;
        foreach (var s in audio.Samples) peak = Math.Max(peak, Math.Abs(s));
        return 20 * Math.Log10(peak);
    }

    /// <summary>
    /// The integrated loudness, in LUFS; negative infinity when no block is above the absolute
    /// gate, or the audio is shorter than one block.
    /// </summary>
    public static double IntegratedLufs(PcmAudio audio)
    {
        var channels = audio.Channels;
        var frames = audio.Frames;
        var block = (int)Math.Round(BlockSeconds * audio.SampleRate);
        var step = (int)Math.Round(StepSeconds * audio.SampleRate);
        if (frames < block) return double.NegativeInfinity;

        // Running sums of the K-weighted squares per channel, so each block is a difference.
        var cumulative = new double[channels][];
        for (var c = 0; c < channels; c++)
        {
            var shelf = HighShelf(audio.SampleRate);
            var highPass = HighPass(audio.SampleRate);
            var sums = cumulative[c] = new double[frames + 1];
            for (var f = 0; f < frames; f++)
            {
                var y = highPass.Next(shelf.Next(audio.Samples[f * channels + c]));
                sums[f + 1] = sums[f] + y * y;
            }
        }

        var blocks = (frames - block) / step + 1;
        var power = new double[blocks];
        for (var j = 0; j < blocks; j++)
        {
            for (var c = 0; c < channels; c++)
                power[j] += (cumulative[c][j * step + block] - cumulative[c][j * step]) / block;
        }

        var aboveAbsolute = power.Where(p => Lufs(p) > AbsoluteGate).ToList();
        if (aboveAbsolute.Count == 0) return double.NegativeInfinity;

        var threshold = Lufs(aboveAbsolute.Average()) + RelativeGate;
        return Lufs(aboveAbsolute.Where(p => Lufs(p) > threshold).Average());
    }

    private static double Lufs(double power) => -0.691 + 10 * Math.Log10(power);

    // The two K-weighting stages for any sample rate, as libebur128 derives them: at 48 kHz
    // they are the filters BS.1770 tabulates.
    private static Biquad HighShelf(int sampleRate)
    {
        const double frequency = 1681.974450955533, gainDb = 3.999843853973347, q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * frequency / sampleRate);
        var vh = Math.Pow(10, gainDb / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        return new Biquad(vh + vb * k / q + k * k, 2 * (k * k - vh), vh - vb * k / q + k * k,
            1 + k / q + k * k, 2 * (k * k - 1), 1 - k / q + k * k);
    }

    private static Biquad HighPass(int sampleRate)
    {
        const double frequency = 38.13547087602444, q = 0.5003270373238773;
        var k = Math.Tan(Math.PI * frequency / sampleRate);
        var a0 = 1 + k / q + k * k;
        return new Biquad(a0, -2 * a0, a0, a0, 2 * (k * k - 1), 1 - k / q + k * k);
    }

    /// <summary>A biquad in direct form I, its coefficients divided by a0.</summary>
    private sealed class Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        private readonly double _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0;
        private double _x1, _x2, _y1, _y2;

        public double Next(double x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return y;
        }
    }
}
