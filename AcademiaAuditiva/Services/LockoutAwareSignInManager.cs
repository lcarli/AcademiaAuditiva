using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Services;

/// <summary>
/// Keeps accounts an admin locked (<see cref="AdminLock"/>) out. Identity refuses
/// locked accounts a password, authenticator or external sign-in, but its cookie
/// check compares the security stamp alone, and refreshing a sign-in (saving the
/// profile, accepting an invite) issues a cookie with the current stamp: a locked
/// user who did that within the check interval kept their session for good. Here
/// an admin-locked account fails the cookie check, and refreshing its own sign-in
/// signs it out. A lockout after wrong passwords or codes leaves open sessions
/// alone, because anyone who knows the address can cause one. A recovery code gets
/// the checks an authenticator code gets, so it can't finish a sign-in begun before
/// either kind of lock.
/// </summary>
public sealed class LockoutAwareSignInManager : SignInManager<ApplicationUser>
{
    public LockoutAwareSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> ValidateSecurityStampAsync(ApplicationUser? user, string? securityStamp)
        => await base.ValidateSecurityStampAsync(user, securityStamp) && !await AdminLock.AppliesAsync(UserManager, user!);

    public override async Task RefreshSignInAsync(ApplicationUser user)
    {
        // Only the user's own session: like the base refresh, which leaves anyone
        // else's alone (an email change link may be opened in another session).
        if (await AdminLock.AppliesAsync(UserManager, user) && await IsSignedInAsAsync(user))
        {
            await SignOutAsync();
            return;
        }

        await base.RefreshSignInAsync(user);
    }

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var refused = user is null ? null : await PreSignInCheck(user);
        return refused ?? await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
    }

    private async Task<bool> IsSignedInAsAsync(ApplicationUser user)
    {
        var auth = await Context.AuthenticateAsync(AuthenticationScheme);
        return auth.Succeeded && UserManager.GetUserId(auth.Principal) == await UserManager.GetUserIdAsync(user);
    }
}
