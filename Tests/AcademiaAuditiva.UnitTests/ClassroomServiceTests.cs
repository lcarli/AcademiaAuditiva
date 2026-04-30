using AcademiaAuditiva.Areas.Teacher.Services;
using AcademiaAuditiva.Models.Teaching;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage of the invite-related service helpers that the
/// <c>InvitesController</c> and <c>ClassroomsController</c> sit on top of:
/// <list type="bullet">
///   <item><see cref="InviteTokenGenerator"/> — entropy and URL safety.</item>
///   <item><see cref="InvitePolicy"/> — the four acceptance states.</item>
/// </list>
/// </summary>
public class ClassroomServiceTests
{
    [Fact]
    public void TokenGenerator_ProducesUrlSafe43CharToken()
    {
        var token = InviteTokenGenerator.Generate();
        token.Should().NotBeNullOrWhiteSpace();
        // 32 random bytes → 43 base64url chars (no padding).
        token.Length.Should().Be(43);
        token.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Fact]
    public void TokenGenerator_ConsecutiveTokensCollide_OnceIn2Pow256_NeverInPractice()
    {
        var a = InviteTokenGenerator.Generate();
        var b = InviteTokenGenerator.Generate();
        a.Should().NotBe(b);
    }

    [Fact]
    public void TokenGenerator_LargeBatch_AllUnique()
    {
        var set = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            set.Add(InviteTokenGenerator.Generate()).Should().BeTrue("each token must be unique");
        }
    }

    [Fact]
    public void InvitePolicy_NullInvite_NotFound()
    {
        InvitePolicy.Evaluate(null, DateTime.UtcNow).Should().Be(InvitePolicy.Result.NotFound);
        InvitePolicy.IsAcceptable(null, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void InvitePolicy_AcceptedInvite_AlreadyAccepted()
    {
        var inv = NewInvite(expiresAt: DateTime.UtcNow.AddDays(7), acceptedAt: DateTime.UtcNow.AddMinutes(-1));
        InvitePolicy.Evaluate(inv, DateTime.UtcNow).Should().Be(InvitePolicy.Result.AlreadyAccepted);
    }

    [Fact]
    public void InvitePolicy_ExpiredInvite_Expired()
    {
        var inv = NewInvite(expiresAt: DateTime.UtcNow.AddSeconds(-1));
        InvitePolicy.Evaluate(inv, DateTime.UtcNow).Should().Be(InvitePolicy.Result.Expired);
    }

    [Fact]
    public void InvitePolicy_FreshInvite_Acceptable()
    {
        var inv = NewInvite(expiresAt: DateTime.UtcNow.AddDays(1));
        InvitePolicy.Evaluate(inv, DateTime.UtcNow).Should().Be(InvitePolicy.Result.Acceptable);
        InvitePolicy.IsAcceptable(inv, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void InvitePolicy_AlreadyAccepted_TakesPrecedenceOverExpired()
    {
        // Edge case: an invite that was accepted on time but is now also past
        // its expiry should still report AlreadyAccepted (the more specific
        // signal) rather than Expired.
        var inv = NewInvite(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            acceptedAt: DateTime.UtcNow.AddDays(-2));
        InvitePolicy.Evaluate(inv, DateTime.UtcNow).Should().Be(InvitePolicy.Result.AlreadyAccepted);
    }

    [Fact]
    public void InvitePolicy_BoundaryAtExactExpiry_StillAcceptable()
    {
        // ExpiresAt == now → still acceptable (the strict-less-than rule).
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var inv = NewInvite(expiresAt: now);
        InvitePolicy.IsAcceptable(inv, now).Should().BeTrue();
    }

    private static ClassroomInvite NewInvite(DateTime expiresAt, DateTime? acceptedAt = null)
        => new()
        {
            Id = 1,
            ClassroomId = 1,
            Email = "student@example.com",
            Token = "fake-token",
            ExpiresAt = expiresAt,
            AcceptedAt = acceptedAt,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
}
