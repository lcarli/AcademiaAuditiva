using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// The sources are set to, and checked at, a loudness measured as ITU-R BS.1770-4 does,
/// so recordings of different kinds sound equally loud before an exercise changes them.
/// </summary>
public class LoudnessTests
{
    // BS.1770's reference: a 997 Hz sine at -20 dBFS in one channel reads -23.01 LUFS.
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void ReferenceSine_ReadsAsTheStandardSays(int rate)
    {
        var audio = Mono(Sine(997, -20, 5, rate), rate);

        Loudness.IntegratedLufs(audio).Should().BeApproximately(-23.01, 0.02);
        Loudness.SamplePeakDbfs(audio).Should().BeApproximately(-20, 0.01);
    }

    [Fact]
    public void Channels_AddUp()
    {
        var sine = Sine(997, -23, 5);
        var stereo = new PcmAudio(44100, 2, [.. sine.SelectMany(s => new[] { s, s })]);

        Loudness.IntegratedLufs(stereo).Should().BeApproximately(-23.01, 0.02);
    }

    [Fact]
    public void KWeighting_DiscountsLowsAndLiftsHighs()
    {
        var reference = Loudness.IntegratedLufs(Mono(Sine(997, -20, 5)));

        (Loudness.IntegratedLufs(Mono(Sine(30, -20, 5))) - reference).Should().BeLessThan(-6);
        (Loudness.IntegratedLufs(Mono(Sine(10000, -20, 5))) - reference).Should().BeInRange(3, 4);
    }

    [Fact]
    public void Silence_IsLeftOut()
    {
        var tone = Sine(997, -20, 4);
        var thenSilence = Mono([.. tone, .. new float[20 * 44100]]);

        // Averaged over all 24 s it would read about 7.8 LU lower.
        Loudness.IntegratedLufs(thenSilence).Should().BeApproximately(-23.01, 0.5);
    }

    [Fact]
    public void QuietPassages_AreLeftOut()
    {
        var loud = Sine(997, -20, 4);
        var quiet = Sine(997, -45, 20);

        Loudness.IntegratedLufs(Mono([.. loud, .. quiet])).Should().BeApproximately(-23.01, 0.5);
    }

    [Fact]
    public void Silence_AndClipsShorterThanABlock_HaveNoLoudness()
    {
        Loudness.IntegratedLufs(Mono(new float[44100])).Should().Be(double.NegativeInfinity);
        Loudness.IntegratedLufs(Mono(Sine(997, -20, 0.3))).Should().Be(double.NegativeInfinity);
        Loudness.SamplePeakDbfs(Mono(new float[10])).Should().Be(double.NegativeInfinity);
    }

    private static PcmAudio Mono(float[] samples, int rate = 44100) => new(rate, 1, samples);

    private static float[] Sine(double frequency, double peakDbfs, double seconds, int rate = 44100)
    {
        var amplitude = Math.Pow(10, peakDbfs / 20);
        return [.. Enumerable.Range(0, (int)(seconds * rate)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate)))];
    }
}
