using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Tests for <see cref="ExerciseFilterPresets"/>, which reads, validates and
/// merges the filter presets stored on routine items, student overrides and answers.
/// Older presets were typed by hand, so bad input must be ignored, never thrown.
/// </summary>
public class ExerciseFilterPresetsTests
{
    private static readonly IReadOnlyList<FilterOptionGroup> KeyAndScale = new List<FilterOptionGroup>
    {
        new() { Name = "keySelect", Label = "Exercise.Key", Options = new() { new("C", "C"), new("D", "D") } },
        new() { Name = "scaleTypeSelect", Label = "Exercise.Scale", Options = new() { new("major", "Exercise.ScaleMajor"), new("minor", "Exercise.ScaleMinor") } },
    };

    [Fact]
    public void Groups_ReadsTheExerciseFilters_AndSkipsUnusableGroups()
    {
        const string json = """
            [
              {"Label":"Exercise.Key","Name":"keySelect","Options":[{"Value":"C","Text":"C"}]},
              {"Label":"Unnamed","Name":"","Options":[{"Value":"C","Text":"C"}]},
              {"Label":"Empty","Name":"empty","Options":[]},
              {"Label":"Missing","Name":"missing"},
              null
            ]
            """;

        var groups = ExerciseFilterPresets.Groups(json);

        groups.Select(g => g.Name).Should().Equal("keySelect");
        groups[0].Options.Should().ContainSingle(o => o.Value == "C");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    public void Groups_ReturnsEmpty_ForMissingOrInvalidJson(string? json)
    {
        ExerciseFilterPresets.Groups(json).Should().BeEmpty();
    }

    [Fact]
    public void Defaults_AreTheFirstOptionOfEachGroup()
    {
        ExerciseFilterPresets.Defaults(KeyAndScale).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "C",
            ["scaleTypeSelect"] = "major",
        });
    }

    [Fact]
    public void Defaults_KeepAnEmptyFirstOption_AndTheFirstGroupOfARepeatedName()
    {
        var groups = new List<FilterOptionGroup>
        {
            new() { Name = "keySelect", Label = "Exercise.Key", Options = new() { new("", "Exercise.Any"), new("C", "C") } },
            new() { Name = "keySelect", Label = "Exercise.Key", Options = new() { new("D", "D") } },
        };

        ExerciseFilterPresets.Defaults(groups).Should().BeEquivalentTo(new Dictionary<string, string> { ["keySelect"] = "" });
    }

    [Fact]
    public void Defaults_SkipGroupsWithoutANameOrOptions()
    {
        var groups = new List<FilterOptionGroup>
        {
            new() { Name = "keySelect", Label = "Exercise.Key", Options = new() },
            new() { Name = "scaleTypeSelect", Label = "Exercise.ScaleType", Options = null! },
            new() { Name = null!, Label = "Exercise.Key", Options = new() { new("C", "C") } },
            new() { Name = "octaveSelect", Label = "Exercise.Octave", Options = new() { new(null!, "Exercise.Any"), new("3", "3") } },
        };

        ExerciseFilterPresets.Defaults(groups).Should().BeEmpty();
    }

    [Fact]
    public void Parse_KeepsStringAndIntegerValues_Only()
    {
        const string json = """
            {"keySelect":"D","melodyLength":5,"flag":true,"none":null,"nested":{"a":"b"},"list":["x"],"ratio":1.5,"blank":""}
            """;

        ExerciseFilterPresets.Parse(json).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "D",
            ["melodyLength"] = "5",
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("{} trailing")]
    [InlineData("[]")]
    [InlineData("[{\"keySelect\":\"D\"}]")]
    [InlineData("\"D\"")]
    [InlineData("42")]
    public void Parse_ReturnsEmpty_ForAnythingButAJsonObject(string? json)
    {
        ExerciseFilterPresets.Parse(json).Should().BeEmpty();
    }

    [Fact]
    public void Serialize_SortsKeys_AndRoundTrips()
    {
        var preset = new Dictionary<string, string> { ["scaleTypeSelect"] = "minor", ["keySelect"] = "D" };

        var json = ExerciseFilterPresets.Serialize(preset);

        json.Should().Be("""{"keySelect":"D","scaleTypeSelect":"minor"}""");
        ExerciseFilterPresets.Parse(json).Should().BeEquivalentTo(preset);
    }

    [Fact]
    public void Serialize_ReturnsNull_ForAnEmptyPreset()
    {
        ExerciseFilterPresets.Serialize(null).Should().BeNull();
        ExerciseFilterPresets.Serialize(new Dictionary<string, string>()).Should().BeNull();
    }

    [Fact]
    public void Sanitize_KeepsOnlyKnownGroupsAndOptions()
    {
        var values = new Dictionary<string, string?>
        {
            ["keySelect"] = "D",
            ["scaleTypeSelect"] = "Minor", // option values are case-sensitive
            ["KeySelect"] = "C",           // so are group names
            ["melodyLength"] = "5",        // not a filter of this exercise
        };

        ExerciseFilterPresets.Sanitize(values, KeyAndScale).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "D",
        });
    }

    [Fact]
    public void Sanitize_DropsEmptyValues_WhichLetTheStudentChoose()
    {
        var values = new Dictionary<string, string?> { ["keySelect"] = "", ["scaleTypeSelect"] = null };

        ExerciseFilterPresets.Sanitize(values, KeyAndScale).Should().BeEmpty();
        ExerciseFilterPresets.Sanitize(null, KeyAndScale).Should().BeEmpty();
        ExerciseFilterPresets.Sanitize(new Dictionary<string, string?> { ["keySelect"] = "D" }, Array.Empty<FilterOptionGroup>())
            .Should().BeEmpty();
    }

    [Fact]
    public void FromQuery_ReadsKnownFilters_AndIgnoresTheRest()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["keySelect"] = "D",
            ["scaleTypeSelect"] = new StringValues(new[] { "major", "minor" }), // repeated parameter
            ["returnUrl"] = "/MyTraining",
        });

        ExerciseFilterPresets.FromQuery(query, KeyAndScale).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "D",
        });
    }

    [Fact]
    public void Merge_OverrideKeysWin_AndTheOthersAreInherited()
    {
        var merged = ExerciseFilterPresets.Merge(
            new Dictionary<string, string> { ["keySelect"] = "C", ["scaleTypeSelect"] = "major" },
            new Dictionary<string, string> { ["scaleTypeSelect"] = "minor", ["melodyLength"] = "5" });

        merged.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["keySelect"] = "C",
            ["scaleTypeSelect"] = "minor",
            ["melodyLength"] = "5",
        });
    }

    [Fact]
    public void Merge_WithoutOverrides_ReturnsACopy()
    {
        var baseFilters = new Dictionary<string, string> { ["keySelect"] = "C" };

        var merged = ExerciseFilterPresets.Merge(baseFilters, null);

        merged.Should().BeEquivalentTo(baseFilters);
        merged.Should().NotBeSameAs(baseFilters);
    }

    [Fact]
    public void Describe_FollowsTheExerciseGroupOrder()
    {
        var applied = ExerciseFilterPresets.Describe(KeyAndScale, new Dictionary<string, string>
        {
            ["scaleTypeSelect"] = "minor",
            ["keySelect"] = "D",
        });

        applied.Select(a => a.Group.Name).Should().Equal("keySelect", "scaleTypeSelect");
        applied.Select(a => a.Option.Text).Should().Equal("D", "Exercise.ScaleMinor");
    }

    [Fact]
    public void Describe_SkipsValuesThatAreNoLongerOptions()
    {
        var applied = ExerciseFilterPresets.Describe(KeyAndScale, new Dictionary<string, string>
        {
            ["keySelect"] = "E",
            ["scaleTypeSelect"] = "minor",
            ["melodyLength"] = "5",
        });

        applied.Should().ContainSingle().Which.Option.Value.Should().Be("minor");
    }

    [Fact]
    public void ForAnswer_KeepsOnlyTheExercisesOwnFilters_InKeyOrder()
    {
        var played = new Dictionary<string, string>
        {
            ["scaleTypeSelect"] = "minor",
            ["keySelect"] = "D",
            ["instrument"] = "Guitar",
            ["guitarPosition"] = "Barre",
            ["noteRange"] = "C3-C5",
        };

        ExerciseFilterPresets.ForAnswer(played, JsonConvert.SerializeObject(KeyAndScale))
            .Should().Be("""{"keySelect":"D","scaleTypeSelect":"minor"}""");
    }

    [Fact]
    public void ForAnswer_DropsValuesThatAreNotOptions()
    {
        var played = new Dictionary<string, string> { ["keySelect"] = "E", ["scaleTypeSelect"] = "minor" };

        ExerciseFilterPresets.ForAnswer(played, JsonConvert.SerializeObject(KeyAndScale))
            .Should().Be("""{"scaleTypeSelect":"minor"}""");
    }

    [Fact]
    public void ForAnswer_IsNull_WhenNoExerciseFilterWasPlayed()
    {
        var keyAndScale = JsonConvert.SerializeObject(KeyAndScale);
        var played = new Dictionary<string, string> { ["instrument"] = "Piano", ["keySelect"] = "C" };

        ExerciseFilterPresets.ForAnswer(played, null).Should().BeNull("the exercise has no filters");
        ExerciseFilterPresets.ForAnswer(played, "[]").Should().BeNull("the exercise has no filters");
        ExerciseFilterPresets.ForAnswer(null, keyAndScale).Should().BeNull();
        ExerciseFilterPresets.ForAnswer(new Dictionary<string, string> { ["keySelect"] = "" }, keyAndScale).Should().BeNull();
    }

    [Theory]
    // {"long":"…"} is 11 characters plus the value.
    [InlineData(ScoreSnapshot.FilterJsonMaxLength - 11, true)]
    [InlineData(ScoreSnapshot.FilterJsonMaxLength - 10, false)]
    public void ForAnswer_IsNull_WhenThePresetWouldNotFitTheColumn(int valueLength, bool fits)
    {
        var value = new string('x', valueLength);
        var groups = JsonConvert.SerializeObject(new[]
        {
            new FilterOptionGroup { Name = "long", Label = "Long", Options = new() { new(value, value) } },
        });

        var json = ExerciseFilterPresets.ForAnswer(new Dictionary<string, string> { ["long"] = value }, groups);

        if (fits) json.Should().HaveLength(ScoreSnapshot.FilterJsonMaxLength);
        else json.Should().BeNull();
    }
}
