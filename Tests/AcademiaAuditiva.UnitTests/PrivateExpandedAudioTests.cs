using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class PrivateExpandedAudioTests
{
    [PrivateTrainingSourcesTests.PrivateAudioFact]
    public async Task EveryPermittedMusicVariant_IsMatchedSafeAndDistinct()
    {
        var (library, _, _) = PrivateTrainingSourcesTests.Library();
        var generator = new TechnicalListeningRoundGenerator(library, new NewAudioExerciseTests.SequenceRandom());
        foreach (var definition in AudioComparisonCatalog.Expansion)
        foreach (var level in ExpandedAudioExerciseTests.Levels)
        {
            var eligible = generator.ExpansionSourcesFor(definition.Name, level);
            eligible.Should().HaveCountGreaterThan(1).And.OnlyContain(source => source.IsRemote);
            for (var sourceIndex = 0; sourceIndex < eligible.Count; sourceIndex++)
            {
                var input = await library.ReadAsync(eligible[sourceIndex]);
                for (var choice = 0; choice < (definition.ReferenceIsA ? definition.Answers.Count : 1); choice++)
                {
                    var plan = ExpandedAudioExerciseTests.Plan(library, definition.Name, level, sourceIndex, choice);
                    plan.Clips.Should().OnlyContain(clip => clip.Plan.SourceKey == eligible[sourceIndex].Key);
                    try
                    {
                        ExpandedAudioExerciseTests.RenderAndCheck(plan, input, definition.Name);
                    }
                    catch (Exception exception) when (exception is Xunit.Sdk.XunitException or InvalidOperationException)
                    {
                        throw new Xunit.Sdk.XunitException(
                            $"{definition.Name}/{level}/{choice}/{eligible[sourceIndex].Key}: {exception.Message}");
                    }
                }
            }
        }
    }

    [PrivateTrainingSourcesTests.PrivateAudioFact]
    public async Task StereoProfiles_HaveUsefulSideEnergy_AndProgressivelyWeakerPolarityCues()
    {
        var (library, _, sources) = PrivateTrainingSourcesTests.Library();
        foreach (var source in sources.Where(source => source.Audio.Channels == 2))
        {
            var input = await library.ReadAsync(source);
            var mid = 0.0;
            var side = 0.0;
            for (var frame = 0; frame < input.Frames; frame++)
            {
                var left = input.Samples[frame * 2];
                var right = input.Samples[frame * 2 + 1];
                mid += Math.Pow((left + right) / 2.0, 2);
                side += Math.Pow((left - right) / 2.0, 2);
            }
            if (AudioSourceLibrary.SupportsStorageWidth(source.Key))
                (side / (mid + side)).Should().BeGreaterThanOrEqualTo(0.04, source.Key);
            foreach (var level in ExpandedAudioExerciseTests.Levels)
            {
                if (!AudioSourceLibrary.SupportsStoragePolarity(source.Key, level)) continue;
                var loss = 10 * Math.Log10(mid / side);
                loss.Should().BeGreaterThanOrEqualTo(level == "beginner" ? 13 : level == "intermediate" ? 7 : 4.4,
                    $"{source.Key}, {level}: inversion must measurably cancel centered content in mono");
            }
        }
    }

    [PrivateTrainingSourcesTests.PrivateAudioFact]
    public async Task RatioChoices_AreDifferentAtMatchedLoudness_OnEveryEligibleRecording()
    {
        var (library, _, _) = PrivateTrainingSourcesTests.Library();
        var generator = new TechnicalListeningRoundGenerator(library, new NewAudioExerciseTests.SequenceRandom());
        foreach (var level in ExpandedAudioExerciseTests.Levels)
        {
            var eligible = generator.ExpansionSourcesFor("CompressionRatio", level);
            for (var index = 0; index < eligible.Count; index++)
            {
                var input = await library.ReadAsync(eligible[index]);
                var output = Enumerable.Range(0, 3).Select(choice =>
                {
                    var plan = ExpandedAudioExerciseTests.Plan(library, "CompressionRatio", level, index, choice);
                    return AudioProcessing.Process(input, plan.Clips[1].Plan.Processors);
                }).ToArray();
                for (var choice = 0; choice < 2; choice++)
                    NewAudioExerciseTests.RelativeDifference(output[choice], output[choice + 1])
                        .Should().BeGreaterThan(0.025, $"{eligible[index].Key}, {level}: adjacent ratio choices need a useful difference");
            }
        }
    }

    [PrivateTrainingSourcesTests.PrivateAudioFact]
    public async Task NextClassificationChoices_HaveDistinctAdjacentCuesOnEveryPermittedChunk()
    {
        var (library, _, _) = PrivateTrainingSourcesTests.Library();
        var generator = new TechnicalListeningRoundGenerator(library, new NewAudioExerciseTests.SequenceRandom());
        foreach (var name in NextAudioExerciseTests.Names.Where(name => AudioComparisonCatalog.Find(name)!.ReferenceIsA))
            foreach (var level in ExpandedAudioExerciseTests.Levels)
            {
                var definition = AudioComparisonCatalog.Find(name)!;
                var eligible = generator.ExpansionSourcesFor(name, level);
                for (var sourceIndex = 0; sourceIndex < eligible.Count; sourceIndex++)
                {
                    var input = await library.ReadAsync(eligible[sourceIndex]);
                    var plans = Enumerable.Range(0, definition.Answers.Count).Select(choice =>
                        ExpandedAudioExerciseTests.Plan(library, name, level, sourceIndex, choice)).ToArray();
                    plans.Should().OnlyContain(plan => plan.Clips.All(clip => clip.Plan.SourceKey == eligible[sourceIndex].Key),
                        "source eligibility must not depend on the correct answer");
                    var output = plans.Select(plan => AudioProcessing.Process(input, plan.Clips[1].Plan.Processors)).ToArray();
                    var context = $"{name}/{level}/{eligible[sourceIndex].Key}";
                    for (var choice = 0; choice < output.Length - 1; choice++)
                        for (var next = choice + 1; next < output.Length; next++)
                            NewAudioExerciseTests.RelativeDifference(output[choice], output[next])
                                .Should().BeGreaterThan(0.025, $"{context}: alternatives {choice}/{next}");

                    if (name is "HighPassCutoff" or "LowPassCutoff" or "EqGain")
                    {
                        var spectra = output.Select(audio => GuessFrequencyTests.Spectrum(audio, fullClip: true)).ToArray();
                        var cues = spectra.Select(spectrum => name switch
                        {
                            "HighPassCutoff" => BandBalance(spectrum, 40, 180, 1000, 3000),
                            "LowPassCutoff" => BandBalance(spectrum, 6000, 12000, 300, 1000),
                            _ => BandBalance(spectrum, 800, 1200, 200, 400),
                        }).ToArray();
                        for (var choice = 0; choice < cues.Length - 1; choice++)
                            ((name == "HighPassCutoff" ? -1 : 1) * (cues[choice + 1] - cues[choice]))
                                .Should().BeGreaterThan(0.6, $"{context}: neighboring tonal choices");
                    }
                    if (name is "LowShelf" or "HighShelf")
                    {
                        var original = GuessFrequencyTests.Spectrum(input, fullClip: true);
                        var before = name == "LowShelf" ? BandBalance(original, 40, 100, 1000, 2500)
                            : BandBalance(original, 6000, 12000, 300, 1000);
                        for (var choice = 0; choice < output.Length; choice++)
                        {
                            var spectrum = GuessFrequencyTests.Spectrum(output[choice], fullClip: true);
                            var after = name == "LowShelf" ? BandBalance(spectrum, 40, 100, 1000, 2500)
                                : BandBalance(spectrum, 6000, 12000, 300, 1000);
                            ((choice == 0 ? 1 : -1) * (after - before)).Should().BeGreaterThan(1, context);
                        }
                    }
                }
            }
    }

    [PrivateTrainingSourcesTests.PrivateAudioFact]
    public async Task NextThresholdAndNoiseCues_MeetTheirDirectionAndSnrOnEveryPermittedChunk()
    {
        var (library, _, _) = PrivateTrainingSourcesTests.Library();
        var generator = new TechnicalListeningRoundGenerator(library, new NewAudioExerciseTests.SequenceRandom());
        foreach (var level in ExpandedAudioExerciseTests.Levels)
        {
            var eligible = generator.ExpansionSourcesFor("CompressorThreshold", level);
            for (var index = 0; index < eligible.Count; index++)
            {
                var input = await library.ReadAsync(eligible[index]);
                var plan = ExpandedAudioExerciseTests.Plan(library, "CompressorThreshold", level, index, 0);
                var output = plan.Clips.Select(clip => AudioProcessing.Process(input, [clip.Plan.Processors[0]])).ToArray();
                var lowerPower = output[0].Samples.Sum(sample => (double)sample * sample);
                var higherPower = output[1].Samples.Sum(sample => (double)sample * sample);
                (10 * Math.Log10(higherPower / lowerPower)).Should().BeGreaterThan(1, $"{level}/{eligible[index].Key}");
                var noisePlan = ExpandedAudioExerciseTests.Plan(library, "NoiseDetection", level, index, 0);
                var noise = (WhiteNoiseProcessor)noisePlan.Clips[1].Plan.Processors[0];
                var noisy = AudioProcessing.Process(input, [noise]);
                var signalPower = input.Samples.Sum(sample => (double)sample * sample);
                var noisePower = noisy.Samples.Zip(input.Samples, (sample, reference) => Math.Pow(sample - reference, 2)).Sum();
                (10 * Math.Log10(signalPower / noisePower)).Should().BeApproximately(noise.SignalToNoiseDb, 0.05);
            }
        }
    }

    private static double BandBalance(double[] spectrum, double low, double high, double referenceLow, double referenceHigh)
    {
        var binHz = 44100.0 / ((spectrum.Length - 1) * 2);
        double Power(double start, double end) => spectrum.Skip((int)Math.Ceiling(start / binHz))
            .Take((int)Math.Floor(end / binHz) - (int)Math.Ceiling(start / binHz) + 1).Sum();
        return 10 * Math.Log10(Power(low, high) / Power(referenceLow, referenceHigh));
    }
}
