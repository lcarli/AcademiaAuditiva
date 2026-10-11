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
