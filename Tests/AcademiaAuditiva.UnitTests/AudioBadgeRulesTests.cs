using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Gamification;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.UnitTests;

public class AudioBadgeRulesTests
{
    private static readonly Lazy<IReadOnlyList<ExerciseInfo>> SeededExercises = new(LoadSeededExercises);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void FirstContact_RequiresACompletedAudioSession()
    {
        var history = new History().Seeded();
        var levelMatch = IdOf("LevelMatch");

        history.Answer(levelMatch, "0000").Earns(BadgeKeys.AudioFirstContact).Should().BeFalse();
        history.Answer(levelMatch, "0").Earns(BadgeKeys.AudioFirstContact).Should().BeTrue();
    }

    [Theory]
    [InlineData("LevelMatch", 10, BadgeKeys.AudioLevelMatch)]
    [InlineData("StereoPosition", 20, BadgeKeys.AudioStereoPosition)]
    [InlineData("GuessFrequency", 20, BadgeKeys.AudioFrequencyRadar)]
    public void ExerciseBadges_RequireTheSpecifiedNumberOfCorrectAnswers(string exercise, int target, string badge)
    {
        var history = new History().Seeded().Answer(IdOf(exercise), new string('1', target - 1));
        history.Earns(badge).Should().BeFalse();

        history.Answer(IdOf(exercise), "1").Earns(badge).Should().BeTrue();
    }

    [Fact]
    public void AnalyticalEar_RequiresThreeAudioSessionsOfAtLeastTenAnswersAndEightyPercent()
    {
        var history = new History().Seeded();
        var levelMatch = IdOf("LevelMatch");

        history.Answer(levelMatch, "1111111100")
            .Pause(Hour).Answer(levelMatch, "1111111100")
            .Pause(Hour).Answer(levelMatch, "1111111000")
            .Earns(BadgeKeys.AudioAnalyticalEar).Should().BeFalse();

        history.Pause(Hour).Answer(levelMatch, "1111111100")
            .Earns(BadgeKeys.AudioAnalyticalEar).Should().BeTrue();
    }

    [Fact]
    public void AudioExplorer_RequiresAnAnswerInEveryAvailableAudioExercise()
    {
        var audioExercises = SeededExercises.Value
            .Where(e => TrainingTracks.OfCategory(e.Category) == TrainingTracks.Audio)
            .ToList();
        audioExercises.Should().NotBeEmpty();

        var complete = new History().Seeded();
        foreach (var exercise in audioExercises)
        {
            complete.Answer(exercise.ExerciseId, "0");
        }
        complete.Earns(BadgeKeys.AudioExplorer).Should().BeTrue();

        var incomplete = new History().Seeded();
        foreach (var exercise in audioExercises.SkipLast(1))
        {
            incomplete.Answer(exercise.ExerciseId, "1");
        }
        incomplete.Earns(BadgeKeys.AudioExplorer).Should().BeFalse();
    }

    [Fact]
    public void FoundationsMastered_RequiresAllThreeBasicAudioPathSteps()
    {
        var history = new History().Seeded()
            .Answer(IdOf("LevelMatch"), "1111111")
            .Answer(IdOf("StereoPosition"), "1111111");

        history.Earns(BadgeKeys.AudioFoundationsMastered).Should().BeFalse();
        history.Answer(IdOf("GuessFrequency"), "111111")
            .Earns(BadgeKeys.AudioFoundationsMastered).Should().BeFalse();
        history.Answer(IdOf("GuessFrequency"), "1")
            .Earns(BadgeKeys.AudioFoundationsMastered).Should().BeTrue();
    }

    private static int IdOf(string exerciseName) =>
        SeededExercises.Value.Where(e => e.Name == exerciseName).Min(e => e.ExerciseId);

    private static IReadOnlyList<ExerciseInfo> LoadSeededExercises()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audio-badge-rules-{Guid.NewGuid():N}")
            .Options);
        SeedData.SeedExercises(db);
        return db.Exercises
            .Select(e => new
            {
                e.ExerciseId,
                e.Name,
                Type = e.ExerciseType != null ? e.ExerciseType.Name : "",
                Category = e.ExerciseCategory != null ? e.ExerciseCategory.Name : "",
                Difficulty = e.DifficultyLevel != null ? e.DifficultyLevel.Name : "",
                e.FiltersJson,
            })
            .AsEnumerable()
            .Select(e => new ExerciseInfo(e.ExerciseId, e.Name, e.Type, e.Category, e.Difficulty,
                ExerciseFilterPresets.Defaults(ExerciseFilterPresets.Groups(e.FiltersJson))))
            .ToList();
    }

    private sealed class History
    {
        private readonly List<PracticeAnswer> _answers = [];
        private readonly Dictionary<int, ExerciseInfo> _exercises = [];
        private DateTime _clock = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        public History Seeded()
        {
            foreach (var exercise in SeededExercises.Value)
            {
                _exercises[exercise.ExerciseId] = exercise;
            }
            return this;
        }

        public History Answer(int exerciseId, string results)
        {
            foreach (var result in results)
            {
                _answers.Add(new PracticeAnswer(exerciseId, result == '1', _clock));
                _clock += TimeSpan.FromMinutes(1);
            }
            return this;
        }

        public History Pause(TimeSpan duration)
        {
            _clock = _answers[^1].Timestamp + duration;
            return this;
        }

        public bool Earns(string key) =>
            BadgeRules.IsEarned(key, _answers, _exercises, TimeZoneInfo.Utc,
                _answers.Count > 0 ? _answers[^1].Timestamp + Hour : _clock);
    }
}
