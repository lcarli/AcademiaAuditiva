using System.Text.Json;
using AcademiaAuditiva.Controllers;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.UnitTests;

public class ExercisePlayResponseTests
{
    [Fact]
    public void NamedRound_ReturnsOnlyItsIdAndOpaqueControlClips()
    {
        var round = new AudioRound(
            "round-1",
            """{"louder":"B","differenceDb":6}""",
            ["opaque-a", "opaque-b"],
            new Dictionary<string, string>
            {
                ["opaque-a"] = "private/reference.wav",
                ["opaque-b"] = "private/processed-plus-6db.wav"
            },
            ClipKeys: ["A", "B"]);

        var payload = ExerciseController.PlayTokens(new Exercise { Name = "LevelMatch" }, round);
        var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.EnumerateObject().Select(p => p.Name).Should().Equal("roundId", "clips");
        json.GetProperty("roundId").GetString().Should().Be("round-1");
        json.GetProperty("clips").EnumerateArray().Select(clip => (
            clip.GetProperty("key").GetString(),
            clip.GetProperty("token").GetString())).Should().Equal(
                ("A", "opaque-a"),
                ("B", "opaque-b"));
        json.ToString().Should().NotContainAny(
            "louder", "differenceDb", "reference", "processed", "gain", ".wav");
    }

    [Fact]
    public void UnnamedRound_KeepsTheExistingSingleClipContract()
    {
        var round = new AudioRound(
            "round-1", "{}", ["opaque-token"], new Dictionary<string, string> { ["opaque-token"] = "private.wav" });

        var payload = ExerciseController.PlayTokens(new Exercise { Name = "GuessNote" }, round);

        payload.Keys.Should().Equal("roundId", "playToken");
        payload["playToken"].Should().Be("opaque-token");
    }
}
