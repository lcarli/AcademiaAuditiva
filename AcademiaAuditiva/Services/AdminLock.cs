using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;

namespace AcademiaAuditiva.Services;

/// <summary>
/// Accounts get locked in two ways. Admin › Users locks one until <see cref="End"/>,
/// that is until an admin unlocks it. Identity locks one for a while after too many
/// wrong passwords or codes (<c>IdentityOptions.Lockout</c> in Program.cs). Only the
/// admin lock ends sessions that are already open, because anyone who knows an
/// account's address can cause the other one.
/// </summary>
public static class AdminLock
{
    public static readonly DateTimeOffset End = DateTimeOffset.MaxValue;

    public static async Task<bool> AppliesAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        await users.GetLockoutEnabledAsync(user) && await users.GetLockoutEndDateAsync(user) == End;
}
