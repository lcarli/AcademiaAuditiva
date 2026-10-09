using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio;
using AcademiaAuditiva.Services.Routines;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Audio tokens are the only way to fetch a clip. An Explore token points straight at
/// the clip the learner picked; a round token points at an exercise round, whose answer
/// stays on the server.
/// </summary>
public class AudioTokenServiceTests
{
    private const string Clip = "piano-audio-mixed/mix-abc.wav";

    private readonly AudioTokenService _service =
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), TimeProvider.System);

    [Fact]
    public async Task IssuedToken_ResolvesToItsClip_OnlyForItsUser()
    {
        var token = await _service.IssueTokenAsync("alice", Clip);

        token.Should().MatchRegex("^[0-9a-f]{32}$");
        (await _service.ResolveTokenAsync("alice", token)).Should().Be(Clip);
        (await _service.ResolveTokenAsync("bob", token)).Should().BeNull();
    }

    [Fact]
    public async Task EveryPlay_GetsItsOwnToken()
    {
        var first = await _service.IssueTokenAsync("alice", Clip);
        var second = await _service.IssueTokenAsync("alice", Clip);

        second.Should().NotBe(first);
        (await _service.ResolveTokenAsync("alice", first)).Should().Be(Clip);
        (await _service.ResolveTokenAsync("alice", second)).Should().Be(Clip);
    }

    [Fact]
    public async Task IssuedToken_ExpiresFifteenMinutesAfterItIsIssued_EvenIfUsed()
    {
        var cache = new Mock<IDistributedCache>();
        var token = await new AudioTokenService(cache.Object, TimeProvider.System).IssueTokenAsync("alice", Clip);

        cache.Verify(c => c.SetAsync(
            $"AudioToken:alice:{token}",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(15)
                && o.AbsoluteExpiration == null
                && o.SlidingExpiration == null),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IssuedToken_CannotStandInForARound()
    {
        var token = await _service.IssueTokenAsync("alice", Clip);

        (await _service.GetRoundAsync("alice", 0, token)).Should().BeNull();
    }

    [Fact]
    public async Task RoundTokens_ResolveToTheirClips_UntilTheRoundIsRemoved()
    {
        var round = await _service.CreateRoundAsync("alice", 7, """{"note":"C4"}""", ["C4.mp3", "G4.mp3"]);

        round.Tokens.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        (await _service.ResolveTokenAsync("alice", round.Tokens[0])).Should().Be("C4.mp3");
        (await _service.ResolveTokenAsync("alice", round.Tokens[1])).Should().Be("G4.mp3");
        (await _service.ResolveTokenAsync("bob", round.Tokens[0])).Should().BeNull();
        (await _service.GetRoundAsync("alice", 7, round.RoundId))!.ExpectedAnswerJson.Should().Be("""{"note":"C4"}""");

        await _service.RemoveRoundAsync("alice", 7, round.RoundId);

        (await _service.ResolveTokenAsync("alice", round.Tokens[0])).Should().BeNull();
        (await _service.ResolveTokenAsync("alice", round.Tokens[1])).Should().BeNull();
        (await _service.GetRoundAsync("alice", 7, round.RoundId)).Should().BeNull();
    }

    [Fact]
    public async Task NamedRoundClips_KeepTheirKeysAndOpaqueTokensInPlaybackOrder()
    {
        var keys = new[] { "A", "B" };

        var created = await _service.CreateRoundAsync(
            "alice", 7, """{"louder":"B"}""", ["reference.wav", "processed.wav"], clipKeys: keys);
        keys[0] = "changed-after-creation";
        var restored = await _service.GetRoundAsync("alice", 7, created.RoundId);

        created.ClipKeys.Should().Equal("A", "B");
        created.Clips.Should().Equal(
            new AudioRoundClip("A", created.Tokens[0]),
            new AudioRoundClip("B", created.Tokens[1]));
        restored!.Clips.Should().Equal(
            new AudioRoundClip("A", created.Tokens[0]),
            new AudioRoundClip("B", created.Tokens[1]));
        restored.Clips.Should().OnlyContain(c => !c.Token.Contains("wav", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NamedRoundAndItsTokens_ExpireTogetherAfterFifteenMinutes()
    {
        var cache = new Mock<IDistributedCache>();
        var service = new AudioTokenService(cache.Object, TimeProvider.System);

        await service.CreateRoundAsync(
            "alice", 7, "{}", ["a.wav", "b.wav"], clipKeys: ["A", "B"]);

        cache.Verify(c => c.SetAsync(
            It.Is<string>(key => key.StartsWith("ExerciseRound:alice:7:", StringComparison.Ordinal)
                || key.StartsWith("AudioToken:alice:", StringComparison.Ordinal)),
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(15)
                && o.AbsoluteExpiration == null
                && o.SlidingExpiration == null),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        cache.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(InvalidClipKeys))]
    public async Task NamedRoundClips_RequireParallelDistinctSafeKeys(string[] keys)
    {
        await FluentActions.Awaiting(() => _service.CreateRoundAsync(
                "alice", 7, "{}", ["a.wav", "b.wav"], clipKeys: keys))
            .Should().ThrowAsync<ArgumentException>();
    }

    public static TheoryData<string[]> InvalidClipKeys => new()
    {
        new[] { "A" },
        new[] { "A", "A" },
        new[] { "A", "a" },
        new[] { "A", "" },
        new[] { "A", "../B" },
        new[] { "A", "0123456789abcdefg" }
    };

    [Fact]
    public async Task Rounds_RememberWhetherTheyAreFreePractice()
    {
        var scored = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"]);
        var free = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"], free: true);

        scored.Free.Should().BeFalse();
        free.Free.Should().BeTrue();
        (await _service.GetRoundAsync("alice", 7, scored.RoundId))!.Free.Should().BeFalse();
        (await _service.GetRoundAsync("alice", 7, free.RoundId))!.Free.Should().BeTrue();
    }

    [Fact]
    public async Task RoundsCachedBeforeFreePractice_StayScored()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await cache.SetStringAsync("ExerciseRound:alice:7:abc",
            """{"RoundId":"abc","ExpectedAnswerJson":"{\"note\":\"C4\"}","TokenToBlob":{"t1":"C4.mp3"}}""");

        var round = await new AudioTokenService(cache, TimeProvider.System).GetRoundAsync("alice", 7, "abc");

        round.Should().NotBeNull();
        round!.Free.Should().BeFalse();
        round.FilterJson.Should().BeNull("rounds cached before filters were saved have none");
        round.IssuedAt.Should().BeNull("rounds cached before answer times were measured have none");
        round.ExpectedAnswerJson.Should().Be("""{"note":"C4"}""");
        round.Tokens.Should().Equal("t1");
        round.ClipKeys.Should().BeNull("rounds cached before named clips use the legacy response contract");
        round.Clips.Should().BeNull();
    }

    [Fact]
    public async Task Rounds_RememberWhenTheyWereIssued()
    {
        var issuedAt = new DateTimeOffset(2026, 10, 5, 14, 30, 15, 250, TimeSpan.Zero);
        var service = new AudioTokenService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), new FixedClock(issuedAt));

        var round = await service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"]);

        round.IssuedAt.Should().Be(issuedAt);
        (await service.GetRoundAsync("alice", 7, round.RoundId))!.IssuedAt.Should().Be(issuedAt);
    }

    [Fact]
    public async Task Rounds_RememberTheFiltersTheyWerePlayedWith()
    {
        const string filters = """{"keySelect":"D4","scaleTypeSelect":"minor"}""";
        var withFilters = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"], filterJson: filters);
        var without = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"]);

        withFilters.FilterJson.Should().Be(filters);
        without.FilterJson.Should().BeNull();
        (await _service.GetRoundAsync("alice", 7, withFilters.RoundId))!.FilterJson.Should().Be(filters);
        (await _service.GetRoundAsync("alice", 7, without.RoundId))!.FilterJson.Should().BeNull();
    }

    [Fact]
    public async Task Rounds_RememberTheRoutineQuestionTheyAsk()
    {
        var question = new RoutineQuestion(new RoutineLink(12, 34), 3);
        var routine = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"], routine: question);
        var practice = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"]);

        routine.Routine.Should().Be(question);
        practice.Routine.Should().BeNull();
        (await _service.GetRoundAsync("alice", 7, routine.RoundId))!.Routine.Should().Be(question);
        (await _service.GetRoundAsync("alice", 7, practice.RoundId))!.Routine.Should().BeNull();
    }

    [Fact]
    public async Task Rounds_RememberTheGameRunTheyBelongTo()
    {
        var game = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"], gameRunId: 42);
        var practice = await _service.CreateRoundAsync("alice", 7, "{}", ["C4.mp3"]);

        game.GameRunId.Should().Be(42);
        practice.GameRunId.Should().BeNull();
        (await _service.GetRoundAsync("alice", 7, game.RoundId))!.GameRunId.Should().Be(42);
        (await _service.GetRoundAsync("alice", 7, practice.RoundId))!.GameRunId.Should().BeNull();
    }

    [Fact]
    public async Task RoundsCachedBeforeRoutineQuestions_AreOrdinaryPractice()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await cache.SetStringAsync("ExerciseRound:alice:7:abc",
            """{"RoundId":"abc","ExpectedAnswerJson":"{}","TokenToBlob":{"t1":"C4.mp3"},"RoutineAssignmentId":12,"RoutineItemId":34}""");

        var round = await new AudioTokenService(cache, TimeProvider.System).GetRoundAsync("alice", 7, "abc");

        round!.Routine.Should().BeNull("a routine round must say which question it asks");
    }

    [Theory]
    [InlineData(null, Clip)]
    [InlineData("", Clip)]
    [InlineData("alice", null)]
    [InlineData("alice", "")]
    public async Task IssueToken_NeedsAUserAndAClip(string? userId, string? address)
    {
        await FluentActions.Awaiting(() => _service.IssueTokenAsync(userId!, address!))
            .Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
