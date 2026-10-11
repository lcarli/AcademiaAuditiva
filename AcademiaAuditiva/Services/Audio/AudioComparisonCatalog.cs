namespace AcademiaAuditiva.Services.Audio;

public sealed record AudioComparisonDefinition(
    string Name, string LevelFilter, int CategoryId, IReadOnlyList<string> Answers, bool ReferenceIsA);

public static class AudioComparisonCatalog
{
    public static IReadOnlyList<AudioComparisonDefinition> Expansion { get; } =
    [
        new("CompressorAttack", "caLevel", 10, ["A", "B"], false),
        new("CompressorRelease", "crLevel", 10, ["A", "B"], false),
        new("CompressionRatio", "raLevel", 10, ["light", "medium", "strong"], true),
        new("StereoWidth", "swLevel", 11, ["narrow", "same", "wide"], true),
        new("ChannelPolarity", "poLevel", 11, ["normal", "inverted"], true),
        new("ReverbPresence", "rvLevel", 12, ["A", "B"], false),
        new("ReverbDecay", "dcLevel", 12, ["A", "B"], false),
        new("ReverbPreDelay", "rpLevel", 12, ["A", "B"], false),
        new("EchoTime", "etLevel", 12, ["short", "medium", "long"], true),
        new("Distortion", "dsLevel", 13, ["A", "B"], false),
        new("HighPassCutoff", "hpfLevel", 9, ["low", "medium", "high"], true),
        new("LowPassCutoff", "lpfLevel", 9, ["low", "medium", "high"], true),
        new("LowShelf", "lsLevel", 9, ["boost", "cut"], true),
        new("HighShelf", "hsLevel", 9, ["boost", "cut"], true),
        new("EqGain", "egLevel", 9, ["light", "medium", "strong"], true),
        new("CompressorThreshold", "ctLevel", 10, ["A", "B"], false),
        new("EchoFeedback", "efLevel", 12, ["A", "B"], false),
        new("ReverbMix", "rmLevel", 12, ["light", "medium", "strong"], true),
        new("NoiseDetection", "ndLevel", 13, ["A", "B"], false),
        new("MixProblem", "mpLevel", 13, ["bass", "treble", "echo", "distortion", "noise"], true),
    ];

    public static IReadOnlyList<AudioComparisonDefinition> All { get; } =
    [
        new("BoostOrCut", "bcLevel", 9, ["boost|bass", "cut|bass", "boost|mids", "cut|mids", "boost|air", "cut|air"], true),
        new("GuessQ", "gqLevel", 9, ["broad", "medium", "narrow"], true),
        new("Compression", "cpLevel", 10, ["A", "B"], false),
        .. Expansion,
    ];

    public static AudioComparisonDefinition? Find(string name) =>
        All.FirstOrDefault(definition => definition.Name == name);
}
