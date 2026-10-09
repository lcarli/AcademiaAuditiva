using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using AcademiaAuditiva.Services.ExerciseValidators;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

public class StereoPositionTests
{
    private static readonly AudioSourceLibrary Sources = new(TempAudioSources.BundledRoot);
    private static readonly Exercise Exercise = new() { Name = "StereoPosition" };

    public static TheoryData<string, string, double> Positions()
    {
        var data = new TheoryData<string, string, double>();
        foreach (var level in new[] { "beginner", "intermediate", "advanced" })
        {
            foreach (var position in TechnicalListeningRoundGenerator.PositionsFor(level))
            {
                data.Add(level, position.Code, position.Pan);
            }
        }
        return data;
    }

    [Theory]
    [InlineData("beginner", new[] { "L", "C", "R" })]
    [InlineData("intermediate", new[] { "L75", "L25", "C", "R25", "R75" })]
    [InlineData("advanced", new[] { "L75", "L50", "L25", "C", "R25", "R50", "R75" })]
    public void Profile_OffersTheDocumentedPositions(string level, string[] codes)
    {
        TechnicalListeningRoundGenerator.PositionsFor(level).Select(p => p.Code).Should().Equal(codes);
    }

    [Theory]
    [InlineData("L", -1, 1, 0)]
    [InlineData("C", 0, 0.7071, 0.7071)]
    [InlineData("R", 1, 0, 1)]
    public void PanConvention_PutsTheSignalWhereTheCodeSays(string code, double pan, double left, double right)
    {
        var mono = new PcmAudio(44100, 1, [1f, 1f, 1f]);

        var stereo = AudioProcessing.Process(mono, [new PanProcessor(pan)]);

        stereo.Channels.Should().Be(2);
        stereo.Samples[0].Should().BeApproximately((float)left, 0.0001f, $"{code} left gain");
        stereo.Samples[1].Should().BeApproximately((float)right, 0.0001f, $"{code} right gain");
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void EveryConfiguredPosition_IsGeneratedValidatedAndKeepsItsLoudness(string level, string code, double pan)
    {
        var index = TechnicalListeningRoundGenerator.PositionsFor(level).ToList().FindIndex(p => p.Code == code);
        var plan = Generate(level, sourceIndex: 0, positionIndex: index);
        var expected = JObject.Parse(plan.ExpectedAnswerJson);

        plan.Clips.Should().ContainSingle().Which.Key.Should().Be("A");
        ((PanProcessor)plan.Clips[0].Plan.Processors.Single()).Position.Should().Be(pan);
        expected["answer"]!.Value<string>().Should().Be(code);
        expected["position"]!.Value<double>().Should().Be(pan);
        plan.AnswerMetadata["spPosition"].Should().Be(pan.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        // The constant-power law keeps the total power equal, centre and edges alike.
        var (left, right) = AudioProcessing.PanGains(pan);
        (20 * Math.Log10(Math.Sqrt(left * left + right * right))).Should().BeApproximately(0, 0.001);

        var validator = new StereoPositionValidator();
        validator.Validate(code, plan.ExpectedAnswerJson).IsCorrect.Should().BeTrue();
        validator.Validate(code.ToLowerInvariant(), plan.ExpectedAnswerJson).IsCorrect.Should().BeTrue();
        var wrong = TechnicalListeningRoundGenerator.PositionsFor(level).First(p => p.Code != code).Code;
        validator.Validate(wrong, plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
        validator.Validate("", plan.ExpectedAnswerJson).IsCorrect.Should().BeFalse();
    }

    [Theory]
    [InlineData("beginner", 1)]
    [InlineData("intermediate", 2)]
    [InlineData("advanced", 3)]
    public void Profile_UsesOnlyMonoPanSourcesForItsDifficulty(string level, int difficulty)
    {
        var eligible = Sources.Sources
            .Where(s => s.Uses.Contains("pan") && s.Difficulties.Contains(difficulty))
            .ToArray();
        eligible.Should().NotBeEmpty();

        for (var sourceIndex = 0; sourceIndex < eligible.Length; sourceIndex++)
        {
            var plan = Generate(level, sourceIndex, positionIndex: 0);
            plan.Clips.Should().OnlyContain(c => c.Plan.SourceKey == eligible[sourceIndex].Key);
            plan.AnswerMetadata["spSourceKind"].Should().Be(eligible[sourceIndex].Kind);
            AudioProcessing.Validate(plan.Clips[0].Plan, eligible[sourceIndex]).Should().Be(2);
        }
    }

    [Fact]
    public void UnknownLevel_UsesBeginnerWithoutEchoingTheInput()
    {
        var plan = Generate("anything", 0, 0);

        JObject.Parse(plan.ExpectedAnswerJson)["level"]!.Value<string>().Should().Be("beginner");
    }

    [Fact]
    public void ExpectedAnswerAndPanStayOutOfTheClipKey()
    {
        var plan = Generate("beginner", 0, 2);

        plan.Clips[0].Key.Should().Be("A", "the key names a control, never the position");
    }

    private static TechnicalListeningRoundPlan Generate(string level, int sourceIndex, int positionIndex) =>
        new TechnicalListeningRoundGenerator(Sources, new SequenceRandom(sourceIndex, positionIndex))
            .Plan(Exercise, new Dictionary<string, string> { ["spLevel"] = level });

    private sealed class SequenceRandom(params int[] values) : IAudioExerciseRandom
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int exclusiveMaximum) =>
            Math.Clamp(_values.Dequeue(), 0, exclusiveMaximum - 1);
    }
}
