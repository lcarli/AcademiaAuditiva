using AcademiaAuditiva.Services.Audio.Sources;

namespace AcademiaAuditiva.Services.Audio.Processing;

internal static class TimeBasedAudioProcessing
{
    internal static PcmAudio Echo(PcmAudio audio, EchoProcessor echo)
    {
        if (echo.WetMix == 0) return audio;
        var delayFrames = (int)Math.Round(audio.SampleRate * echo.DelayMs / 1000);
        var buffer = new double[delayFrames * audio.Channels];
        var cursor = 0;
        for (var frame = 0; frame < audio.Frames; frame++)
        {
            for (var channel = 0; channel < audio.Channels; channel++)
            {
                var index = frame * audio.Channels + channel;
                var delayed = buffer[cursor + channel];
                var input = audio.Samples[index];
                buffer[cursor + channel] = input + delayed * echo.Feedback;
                audio.Samples[index] = (float)((1 - echo.WetMix) * input + echo.WetMix * delayed);
            }
            cursor = (cursor + audio.Channels) % buffer.Length;
        }
        return audio;
    }

    internal static PcmAudio Reverb(PcmAudio audio, ReverbProcessor reverb)
    {
        if (reverb.WetMix == 0) return audio;
        var preDelayFrames = (int)Math.Round(audio.SampleRate * reverb.PreDelayMs / 1000);
        for (var channel = 0; channel < audio.Channels; channel++)
        {
            // Parallel damped combs, then all-pass diffusion; each channel owns its state.
            var spread = channel * 1.3;
            var combs = new[] { 29.7, 37.1, 41.1, 43.7 }
                .Select(milliseconds => new Comb(
                    (int)Math.Round(audio.SampleRate * (milliseconds + spread) / 1000),
                    audio.SampleRate, reverb.DecaySeconds)).ToArray();
            var diffusers = new[] { 5.0, 1.7 }
                .Select(milliseconds => new AllPass((int)Math.Round(audio.SampleRate * milliseconds / 1000))).ToArray();
            var preDelay = preDelayFrames == 0 ? null : new double[preDelayFrames];
            var cursor = 0;
            for (var frame = 0; frame < audio.Frames; frame++)
            {
                var index = frame * audio.Channels + channel;
                var dry = audio.Samples[index];
                var input = (double)dry;
                if (preDelay is not null)
                {
                    input = preDelay[cursor];
                    preDelay[cursor] = dry;
                    cursor = (cursor + 1) % preDelay.Length;
                }
                var wet = 0.0;
                foreach (var comb in combs) wet += comb.Next(input);
                wet /= combs.Length;
                foreach (var diffuser in diffusers) wet = diffuser.Next(wet);
                audio.Samples[index] = (float)((1 - reverb.WetMix) * dry + reverb.WetMix * wet);
            }
        }
        return audio;
    }

    private sealed class Comb(int frames, int sampleRate, double decaySeconds)
    {
        private readonly double[] _buffer = new double[frames];
        private readonly double _feedback = Math.Pow(0.001, frames / (sampleRate * decaySeconds));
        private double _damped;
        private int _cursor;

        internal double Next(double input)
        {
            var delayed = _buffer[_cursor];
            _damped = 0.65 * delayed + 0.35 * _damped;
            _buffer[_cursor] = input + _feedback * _damped;
            _cursor = (_cursor + 1) % _buffer.Length;
            return delayed;
        }
    }

    private sealed class AllPass(int frames)
    {
        private readonly double[] _buffer = new double[frames];
        private int _cursor;

        internal double Next(double input)
        {
            var delayed = _buffer[_cursor];
            var output = delayed - 0.5 * input;
            _buffer[_cursor] = input + 0.5 * output;
            _cursor = (_cursor + 1) % _buffer.Length;
            return output;
        }
    }
}
