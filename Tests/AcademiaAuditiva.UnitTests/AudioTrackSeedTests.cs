using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

public class AudioTrackSeedTests
{
    [Fact]
    public void AudioCategories_AreAppendedWithoutMovingMusicCategories()
    {
        SeedData.ExerciseCategories().Select(c => c.Name).Should().Equal(
            "Harmony", "Melody", "Rhythm", "EarTraining", "Scales", "Games", "Misc",
            "Level", "FrequencyEq", "Dynamics", "StereoPhase", "SpaceTime", "CriticalListening");
        SeedData.Exercises().Single(e => e.Name == "LevelMatch")
            .Should().BeEquivalentTo(new
            {
                ExerciseTypeId = 9,
                ExerciseCategoryId = 8,
                DifficultyLevelId = 1,
            });
    }

    [Fact]
    public void ExistingDatabase_GetsMissingLookupsAndResolvesExerciseForeignKeysByName()
    {
        using var db = Database();
        db.ExerciseTypes.AddRange(SeedData.ExerciseTypes().Take(8).Select((row, i) =>
            new ExerciseType { Id = 201 + i, Name = row.Name, DisplayName = row.DisplayName }));
        db.ExerciseCategories.AddRange(SeedData.ExerciseCategories().Take(7).Select((row, i) =>
            new ExerciseCategory { Id = 101 + i, Name = row.Name, DisplayName = row.DisplayName }));
        db.DifficultyLevels.AddRange(SeedData.DifficultyLevels().Select((row, i) =>
            new DifficultyLevel { Id = 301 + i, Name = row.Name, DisplayName = row.DisplayName }));
        db.SaveChanges();

        SeedData.SeedExercises(db);
        SeedData.SeedExercises(db);

        db.ExerciseTypes.Select(t => t.Name).Should().OnlyHaveUniqueItems().And.Contain("AudioComparison");
        db.ExerciseCategories.Select(c => c.Name).Should().OnlyHaveUniqueItems()
            .And.Contain(["Level", "FrequencyEq", "Dynamics", "StereoPhase", "SpaceTime", "CriticalListening"]);
        var levelMatch = db.Exercises.Single(e => e.Name == "LevelMatch");
        levelMatch.ExerciseTypeId.Should().Be(db.ExerciseTypes.Single(t => t.Name == "AudioComparison").Id);
        levelMatch.ExerciseCategoryId.Should().Be(db.ExerciseCategories.Single(c => c.Name == "Level").Id);
        levelMatch.DifficultyLevelId.Should().Be(db.DifficultyLevels.Single(d => d.Name == "Beginner").Id);

        var guessNote = db.Exercises.Single(e => e.Name == "GuessNote");
        guessNote.ExerciseTypeId.Should().Be(201);
        guessNote.ExerciseCategoryId.Should().Be(104);
        guessNote.DifficultyLevelId.Should().Be(301);
    }

    private static ApplicationDbContext Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audio-track-seed-{Guid.NewGuid():N}")
            .Options);
}
