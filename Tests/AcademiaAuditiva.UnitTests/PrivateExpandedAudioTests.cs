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
}
