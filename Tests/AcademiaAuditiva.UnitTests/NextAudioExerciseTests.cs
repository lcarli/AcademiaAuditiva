using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class NextAudioExerciseTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    internal static readonly string[] Names =
    [
        "HighPassCutoff", "LowPassCutoff", "LowShelf", "HighShelf", "EqGain",
        "CompressorThreshold", "EchoFeedback", "ReverbMix", "NoiseDetection", "MixProblem",
    ];

    public static IEnumerable<object[]> Exercises => Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Exercises))]
    public void Profiles_ProgressivelyNarrowTheDifferenceWithoutChangingTheQuestion(string name)
    {
        var contrasts = ExpandedAudioExerciseTests.Levels.Select(level =>
        {
            var definition = AudioComparisonCatalog.Find(name)!;
            var plans = Enumerable.Range(0, definition.ReferenceIsA ? definition.Answers.Count : 1)
                .Select(choice => ExpandedAudioExerciseTests.Plan(Sources, name, level, 0, choice)).ToArray();
            return name switch
            {
                "HighPassCutoff" or "LowPassCutoff" => Math.Log(
                    ((CutFilterProcessor)plans[^1].Clips[1].Plan.Processors[0]).FrequencyHz
                    / ((CutFilterProcessor)plans[0].Clips[1].Plan.Processors[0]).FrequencyHz),
                "LowShelf" or "HighShelf" => Math.Abs(((ShelfEqProcessor)plans[0].Clips[1].Plan.Processors[0]).GainDb),
                "EqGain" => ((PeakingEqProcessor)plans[^1].Clips[1].Plan.Processors[0]).GainDb
                    - ((PeakingEqProcessor)plans[0].Clips[1].Plan.Processors[0]).GainDb,
                "CompressorThreshold" => ((CompressorProcessor)plans[0].Clips[1].Plan.Processors[0]).ThresholdDbfs
                    - ((CompressorProcessor)plans[0].Clips[0].Plan.Processors[0]).ThresholdDbfs,
                "EchoFeedback" => ((EchoProcessor)plans[0].Clips[1].Plan.Processors[0]).Feedback
                    - ((EchoProcessor)plans[0].Clips[0].Plan.Processors[0]).Feedback,
                "ReverbMix" => ((ReverbProcessor)plans[^1].Clips[1].Plan.Processors[0]).WetMix
                    - ((ReverbProcessor)plans[0].Clips[1].Plan.Processors[0]).WetMix,
                "NoiseDetection" => AudioProcessing.LinearGain(
                    -((WhiteNoiseProcessor)plans[0].Clips[1].Plan.Processors[0]).SignalToNoiseDb),
                "MixProblem" => Math.Abs(((PeakingEqProcessor)plans[0].Clips[1].Plan.Processors[0]).GainDb),
                _ => throw new ArgumentException("Unknown next-batch exercise.", nameof(name)),
            };
        }).ToArray();
        contrasts.Should().BeInDescendingOrder().And.OnlyHaveUniqueItems().And.OnlyContain(value => value > 0);
    }

    [Theory]
    [MemberData(nameof(Exercises))]
    public void ReferenceAndRandomSideAnswers_AgreeWithTheirProcessors(string name)
    {
        foreach (var level in ExpandedAudioExerciseTests.Levels)
        {
            var definition = AudioComparisonCatalog.Find(name)!;
            for (var choice = 0; choice < (definition.ReferenceIsA ? definition.Answers.Count : 1); choice++)
            {
                var first = ExpandedAudioExerciseTests.Plan(Sources, name, level, 0, choice, 0);
                var second = ExpandedAudioExerciseTests.Plan(Sources, name, level, 0, choice, 1);
                var answer = JObject.Parse(first.ExpectedAnswerJson)["answer"]!.Value<string>();
                var swappedAnswer = JObject.Parse(second.ExpectedAnswerJson)["answer"]!.Value<string>();
                if (definition.ReferenceIsA)
                {
                    answer.Should().Be(definition.Answers[choice]);
                    swappedAnswer.Should().Be(answer);
                    first.Clips[0].Plan.Processors.Should().Equal(new FadeOutProcessor(15), new LoudnessMatchProcessor(-30));
                    second.Clips[1].Plan.Processors.Should().Equal(first.Clips[1].Plan.Processors);
                }
                else
                {
                    swappedAnswer.Should().Be(answer == "A" ? "B" : "A");
                    second.Clips[0].Plan.Processors.Should().Equal(first.Clips[1].Plan.Processors);
                    second.Clips[1].Plan.Processors.Should().Equal(first.Clips[0].Plan.Processors);
                }
                first.Clips.Should().OnlyContain(clip => clip.Plan.SourceKey == second.Clips[0].Plan.SourceKey);
            }
        }
    }

    [Theory]
    [InlineData("beginner")]
    [InlineData("intermediate")]
    [InlineData("advanced")]
    public void FeedbackChangesLaterRepeats_WhileThresholdControlsMoreOfTheSignal(string level)
    {
        var echo = ExpandedAudioExerciseTests.Plan(Sources, "EchoFeedback", level, 0, 0);
        var impulseSamples = new float[44100];
        impulseSamples[0] = 0.4f;
        var impulse = new PcmAudio(44100, 1, impulseSamples);
        var echoes = echo.Clips.Select(clip => AudioProcessing.Process(impulse, [clip.Plan.Processors[0]])).ToArray();
        echoes[0].Samples[11025].Should().Be(echoes[1].Samples[11025]);
        echoes[1].Samples[22050].Should().BeGreaterThan(echoes[0].Samples[22050]);
        foreach (var output in echoes)
            output.Samples.Where((_, index) => index % 11025 != 0).Should().OnlyContain(sample => sample == 0);

        var threshold = ExpandedAudioExerciseTests.Plan(Sources, "CompressorThreshold", level, 0, 0);
        var lower = (CompressorProcessor)threshold.Clips[0].Plan.Processors[0];
        var higher = (CompressorProcessor)threshold.Clips[1].Plan.Processors[0];
        (lower with { ThresholdDbfs = higher.ThresholdDbfs }).Should().Be(higher);
        var amplitude = (float)AudioProcessing.LinearGain(higher.ThresholdDbfs + 2);
        var steady = new PcmAudio(44100, 1, Enumerable.Repeat(amplitude, 44100).ToArray());
        var lowerOutput = AudioProcessing.Process(steady, [lower]);
        var higherOutput = AudioProcessing.Process(steady, [higher]);
        (20 * Math.Log10(higherOutput.Samples[^1] / lowerOutput.Samples[^1])).Should().BeGreaterThan(3);
    }
}
