using AcademiaAuditiva.Services.Audio.Processing;
using AcademiaAuditiva.Services.Audio.Sources;
using Newtonsoft.Json;

namespace AcademiaAuditiva.Services.Audio;

public sealed partial class TechnicalListeningRoundGenerator
{
    private TechnicalListeningRoundPlan PlanExpansion(
        AudioComparisonDefinition definition, IReadOnlyDictionary<string, string> filters)
    {
        var level = Level(filters, definition.LevelFilter);
        var difficulty = level == "beginner" ? 0 : level == "intermediate" ? 1 : 2;
        var source = Pick(ExpansionSourcesFor(definition.Name, level), definition.Name);
        var target = new LoudnessMatchProcessor(-30 - _random.Next(9) / 2.0);
        IReadOnlyList<AudioProcessor> first = [];
        IReadOnlyList<AudioProcessor> second = [];
        string answer;
        double[] parameters;
        var comparisonSide = _random.Next(2);

        switch (definition.Name)
        {
            case "CompressorAttack":
                var fastAttack = new[] { 0.0, 1, 3 }[difficulty];
                var slowAttack = new[] { 60.0, 30, 15 }[difficulty];
                first = [new CompressorProcessor(-28, 6, fastAttack, 80)];
                second = [new CompressorProcessor(-28, 6, slowAttack, 80)];
                answer = "A";
                parameters = [fastAttack, slowAttack];
                break;
            case "CompressorRelease":
                var fastRelease = new[] { 20.0, 40, 70 }[difficulty];
                var slowRelease = new[] { 350.0, 200, 130 }[difficulty];
                first = [new CompressorProcessor(-28, 6, 0, fastRelease)];
                second = [new CompressorProcessor(-28, 6, 0, slowRelease)];
                answer = "A";
                parameters = [fastRelease, slowRelease];
                break;
            case "CompressionRatio":
                var ratioIndex = _random.Next(3);
                var ratios = difficulty == 0 ? new[] { 2.0, 4, 8 }
                    : difficulty == 1 ? [2.0, 3, 6] : [2.0, 3, 4];
                var ratio = ratios[ratioIndex];
                second = [new CompressorProcessor(-28, ratio, 0, 20)];
                answer = definition.Answers[ratioIndex];
                parameters = [ratio, -28];
                break;
            case "StereoWidth":
                var widthIndex = _random.Next(3);
                var difference = new[] { 0.55, 0.35, 0.2 }[difficulty];
                var width = 1 + (widthIndex - 1) * difference;
                second = [new StereoWidthProcessor(width)];
                answer = definition.Answers[widthIndex];
                parameters = [width * 100];
                break;
            case "ChannelPolarity":
                var invert = _random.Next(2) == 1;
                second = [new ChannelPolarityProcessor(invert)];
                answer = invert ? "inverted" : "normal";
                parameters = [];
                break;
            case "ReverbPresence":
                var wetMix = new[] { 0.55, 0.4, 0.25 }[difficulty];
                second = [new ReverbProcessor(1.1, 15, wetMix)];
                answer = "B";
                parameters = [wetMix * 100, 1.1];
                break;
            case "ReverbDecay":
                var shortDecay = new[] { 0.4, 0.7, 1.0 }[difficulty];
                var longDecay = new[] { 3.0, 2.0, 1.5 }[difficulty];
                first = [new ReverbProcessor(shortDecay, 15, 0.6)];
                second = [new ReverbProcessor(longDecay, 15, 0.6)];
                answer = "B";
                parameters = [shortDecay, longDecay];
                break;
            case "ReverbPreDelay":
                var shortPreDelay = new[] { 0.0, 10, 25 }[difficulty];
                var longPreDelay = new[] { 100.0, 70, 50 }[difficulty];
                first = [new ReverbProcessor(0.7, shortPreDelay, 0.6)];
                second = [new ReverbProcessor(0.7, longPreDelay, 0.6)];
                answer = "B";
                parameters = [shortPreDelay, longPreDelay];
                break;
            case "EchoTime":
                var delayIndex = _random.Next(3);
                var delays = difficulty == 0 ? new[] { 75.0, 225, 675 }
                    : difficulty == 1 ? [100.0, 200, 400] : [150.0, 225, 300];
                var delay = delays[delayIndex];
                second = [new EchoProcessor(delay, 0.3, 0.35)];
                answer = definition.Answers[delayIndex];
                parameters = [delay];
                break;
            case "Distortion":
                var overdrive = new[] { 12.0, 9, 6 }[difficulty];
                second = [new SaturationProcessor(source.Audio.PeakDbfs - overdrive)];
                answer = "B";
                parameters = [overdrive];
                break;
            case "HighPassCutoff":
            case "LowPassCutoff":
                var cutoffIndex = _random.Next(3);
                var highPass = definition.Name == "HighPassCutoff";
                var cutoffs = highPass
                    ? difficulty switch
                    {
                        0 => new[] { 80.0, 250, 800 },
                        1 => [120.0, 250, 500],
                        _ => [180.0, 250, 350],
                    }
                    : difficulty switch
                    {
                        0 => new[] { 1500.0, 4500, 12000 },
                        1 => [2500.0, 4500, 8000],
                        _ => [3500.0, 4500, 6000],
                    };
                var cutoff = cutoffs[cutoffIndex];
                second = [new CutFilterProcessor(cutoff, highPass)];
                answer = definition.Answers[cutoffIndex];
                parameters = [cutoff];
                break;
            case "LowShelf":
            case "HighShelf":
                var boost = _random.Next(2) == 0;
                var shelfGain = new[] { 9.0, 6, 3 }[difficulty] * (boost ? 1 : -1);
                var shelfFrequency = definition.Name == "HighShelf" ? 4000 : 150;
                second = [new ShelfEqProcessor(shelfFrequency, shelfGain, definition.Name == "HighShelf")];
                answer = boost ? "boost" : "cut";
                parameters = [shelfGain, shelfFrequency];
                break;
            case "EqGain":
                var gainIndex = _random.Next(3);
                var gains = difficulty switch
                {
                    0 => new[] { 3.0, 7.5, 12 },
                    1 => [3.0, 6, 9],
                    _ => [3.0, 4.5, 6],
                };
                var eqGain = gains[gainIndex];
                second = [new PeakingEqProcessor(1000, eqGain, 1)];
                answer = definition.Answers[gainIndex];
                parameters = [eqGain, 1000];
                break;
            case "CompressorThreshold":
                var lowerThreshold = source.Audio.PeakDbfs - new[] { 15.0, 12, 10 }[difficulty];
                var higherThreshold = source.Audio.PeakDbfs - new[] { 3.0, 4, 5 }[difficulty];
                first = [new CompressorProcessor(lowerThreshold, 4, 3, 50)];
                second = [new CompressorProcessor(higherThreshold, 4, 3, 50)];
                answer = "A";
                parameters = [lowerThreshold, higherThreshold];
                break;
            case "EchoFeedback":
                var lowFeedback = new[] { 0.1, 0.2, 0.3 }[difficulty];
                var highFeedback = new[] { 0.65, 0.5, 0.45 }[difficulty];
                first = [new EchoProcessor(250, lowFeedback, 0.5)];
                second = [new EchoProcessor(250, highFeedback, 0.5)];
                answer = "B";
                parameters = [lowFeedback * 100, highFeedback * 100, 250];
                break;
            case "ReverbMix":
                var mixIndex = _random.Next(4);
                var mixes = difficulty switch
                {
                    0 => new[] { 0.0, 0.15, 0.4, 0.7 },
                    1 => [0.0, 0.2, 0.4, 0.6],
                    _ => [0.0, 0.3, 0.4, 0.5],
                };
                var mix = mixes[mixIndex];
                second = [new ReverbProcessor(1.2, 20, mix)];
                answer = definition.Answers[mixIndex];
                parameters = [mix * 100, 1.2];
                break;
            case "NoiseDetection":
                var snr = NoiseSnr(difficulty);
                second = [new WhiteNoiseProcessor(snr, 1 + _random.Next(int.MaxValue - 1))];
                answer = "B";
                parameters = [snr];
                break;
            case "MixProblem":
                var problemIndex = _random.Next(definition.Answers.Count);
                answer = definition.Answers[problemIndex];
                var change = new[] { 6.0, 4, 2 }[difficulty];
                second = answer switch
                {
                    "muddy" => [new PeakingEqProcessor(250, change, 1)],
                    "harsh" => [new PeakingEqProcessor(4000, change, 1)],
                    "boomy" => [new PeakingEqProcessor(125, change, 1)],
                    "thin" => [new PeakingEqProcessor(150, -change, 1)],
                    "narrow" => [new StereoWidthProcessor(new[] { 0.5, 0.65, 0.8 }[difficulty])],
                    _ => throw new InvalidOperationException("Unknown mix diagnostic answer."),
                };
                parameters = [];
                break;
            default:
                throw new ArgumentException("Unknown expanded Audio comparison.", nameof(definition));
        }

        if (!definition.ReferenceIsA && comparisonSide == 1)
        {
            (first, second) = (second, first);
            answer = answer == "A" ? "B" : "A";
        }
        var clips = new List<NamedAudioProcessingPlan>
        {
            new NamedAudioProcessingPlan("A", new(source.Key, [.. first, new FadeOutProcessor(15), target])),
            new NamedAudioProcessingPlan("B", new(source.Key, [.. second, new FadeOutProcessor(15), target])),
        };
        if (definition.Name == "MixProblem")
        {
            for (var index = 0; index < definition.Answers.Count; index++)
            {
                var key = definition.Answers[index];
                var repair = MixProblemCorrection(key, difficulty);
                clips.Add(new NamedAudioProcessingPlan(
                    ((char)('C' + index)).ToString(),
                    new(source.Key, [.. second, repair, new FadeOutProcessor(15), target])));
            }
        }
        foreach (var clip in clips) AudioProcessing.Validate(clip.Plan, source);
        return new TechnicalListeningRoundPlan(
            JsonConvert.SerializeObject(new { answer, parameters, level, sourceKind = source.Kind }),
            clips, new Dictionary<string, string>
            {
                ["audioAnswer"] = answer,
                ["audioParameters"] = JsonConvert.SerializeObject(parameters),
                ["audioSourceKind"] = source.Kind,
            });
    }

    private static AudioProcessor MixProblemCorrection(string answer, int difficulty) => answer switch
    {
        "muddy" => new PeakingEqProcessor(250, new[] { -6.0, -4, -2 }[difficulty], 1),
        "harsh" => new PeakingEqProcessor(4000, new[] { -6.0, -4, -2 }[difficulty], 1),
        "boomy" => new PeakingEqProcessor(125, new[] { -6.0, -4, -2 }[difficulty], 1),
        "thin" => new PeakingEqProcessor(150, new[] { 6.0, 4, 2 }[difficulty], 1),
        "narrow" => new StereoWidthProcessor(1 / new[] { 0.5, 0.65, 0.8 }[difficulty]),
        _ => throw new InvalidOperationException("Unknown mix correction."),
    };

    private static double NoiseSnr(int difficulty) => new[] { 12.0, 21, 30 }[difficulty];

    internal IReadOnlyList<AudioSource> ExpansionSourcesFor(string name, string level) =>
        PreferMusic(_sources.Sources.Where(source => source.Audio.Channels == 2 && (source.IsRemote
            ? name switch
            {
                "StereoWidth" => AudioSourceLibrary.SupportsStorageWidth(source.Key),
                "ChannelPolarity" => AudioSourceLibrary.SupportsStoragePolarity(source.Key, level),
                _ => AudioSourceLibrary.SupportsStorageCompression(source.Key),
            }
            : source.Key == "full-mix")));
}
