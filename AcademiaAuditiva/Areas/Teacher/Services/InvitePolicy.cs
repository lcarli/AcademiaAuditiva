namespace AcademiaAuditiva.Areas.Teacher.Services;

using AcademiaAuditiva.Models.Teaching;

/// <summary>
/// Decides whether a <see cref="ClassroomInvite"/> is in
/// an acceptable state for a learner to redeem. Centralised here so the
/// controller does not duplicate the same null/expired/already-accepted
/// chain at every call site, and so the rules can be unit-tested without
/// spinning up Identity or EF.
/// </summary>
public static class InvitePolicy
{
    public enum Result
    {
        Acceptable,
        NotFound,
        Expired,
        AlreadyAccepted
    }

    public static Result Evaluate(ClassroomInvite? invite, DateTime utcNow)
    {
        if (invite is null) return Result.NotFound;
        if (invite.AcceptedAt is not null) return Result.AlreadyAccepted;
        if (invite.ExpiresAt < utcNow) return Result.Expired;
        return Result.Acceptable;
    }

    public static bool IsAcceptable(ClassroomInvite? invite, DateTime utcNow)
        => Evaluate(invite, utcNow) == Result.Acceptable;
}
