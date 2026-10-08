using AcademiaAuditiva.Data;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Audio;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// GuessTuning plays a note twice and asks whether the second time was in tune, sharp or flat.
/// The level is how many cents out of tune the second note is: 50 (a quarter tone) down to 5.
/// </summary>
public class GuessTuningExerciseTests
{
    private static readonly Exercise Exercise = new() { ExerciseId = 997, Name = "GuessTuning" };

    [Fact]
    public void EveryAnswer_IsAsked_AndEachHasAButton()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-tuning-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var buttons = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
            db.Exercises.Single(e => e.Name == "GuessTuning").AnswerButtonsJson!)!["guessAnswer"];

        var asked = Enumerable.Range(0, 200).Select(_ => Round([]).Value<string>("answer")).Distinct();

        buttons.Values.Should().BeEquivalentTo("inTune", "sharp", "flat");
        asked.Should().BeEquivalentTo(buttons.Values, "the student can give every answer, and every button can be right");
    }

    [Fact]
    public void TheLevels_AreTheFilterOptions()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"guess-tuning-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        var groups = ExerciseFilterPresets.Groups(db.Exercises.Single(e => e.Name == "GuessTuning").FiltersJson);

        groups.Should().ContainSingle(g => g.Name == "gtLevel").Which.Options.Select(o => o.Value)
            .Should().Equal(MusicTheoryService.TuningLevels.Select(level => level.ToString()));
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("25", 25)]
    [InlineData("10", 10)]
    [InlineData("5", 5)]
    [InlineData(null, 50)]
    [InlineData("", 50)]
    [InlineData("7", 50)]
    [InlineData("loud", 50)]
    public void TheSecondNote_IsTheLevelsCentsSharpOrFlat_OrInTune(string? level, int cents)
    {
        var filters = new Dictionary<string, string>();
        if (level is not null)
            filters["gtLevel"] = level;

        for (var i = 0; i < 100; i++)
        {
            var round = Round(filters);

            round.Value<int>("cents").Should().Be(round.Value<string>("answer") switch
            {
                "sharp" => cents,
                "flat" => -cents,
                "inTune" => 0,
                var other => throw new InvalidOperationException($"GuessTuning asks no such answer: {other}"),
            }, "{0} is {1}", round.ToString(Formatting.None), round["answer"]);
        }
    }

    [Theory]
    [InlineData("Piano", "C4-C4", 4)]
    [InlineData("Guitar", "C2-C2", 2)]
    [InlineData("Violin", "C5-C5", 5)]
    public void TheNote_IsANoteOfTheRange_OnTheInstrument(string instrumentName, string noteRange, int octave)
    {
        var instrument = Instrument.FromName(instrumentName);
        var lowest = Math.Max((octave + 1) * 12, instrument.LowestMidi);

        var notes = Enumerable.Range(0, 200)
            .Select(_ => Round(new() { ["noteRange"] = noteRange }, instrument).Value<string>("note")!)
            .ToList();

        notes.Should().AllSatisfy(note => (MusicTheoryService.NoteToMidi(note) ?? -1)
            .Should().BeInRange(lowest, (octave + 1) * 12 + 11, "the {0} plays {1} in octave {2}", instrumentName, note, octave));
        notes.Distinct().Should().HaveCountGreaterThan(5, "the note changes from round to round");
    }

    [Theory]
    [InlineData("Piano")]
    [InlineData("Guitar")]
    [InlineData("Violin")]
    public void TheSameNote_IsPlayedTwice_TheSecondTimeOutOfTuneByTheRoundsCents(string instrumentName)
    {
        var planner = new ExercisePlaybackPlanner();
        var instrument = Instrument.FromName(instrumentName);

        for (var i = 0; i < 30; i++)
        {
            var plan = planner.Plan(Exercise, new() { ["instrument"] = instrumentName, ["gtLevel"] = "25" });

            var round = JObject.Parse(plan.ExpectedAnswerJson);
            var sample = instrument.SampleFor(round.Value<string>("note")!);
            plan.PlaybackPlans.Should().ContainSingle().Which.Should().Equal(
                new MixInput(sample, 0.0, 1.5),
                new MixInput(sample, 2.0, 1.5, Cents: round.Value<int>("cents")));
            round.Value<int>("cents").Should().BeOneOf(-25, 0, 25);
        }
    }

    private static JObject Round(Dictionary<string, string> filters, Instrument? instrument = null) =>
        JObject.FromObject(MusicTheoryService.GenerateNoteForExercise(Exercise, filters, instrument));
}
