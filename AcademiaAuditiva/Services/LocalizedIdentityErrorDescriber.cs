using AcademiaAuditiva.Resources;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Services;

public sealed class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    private readonly IStringLocalizer<SharedResources> _localizer;

    public LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResources> localizer)
    {
        _localizer = localizer;
    }

    private IdentityError Error(string code, string key, params object[] args)
        => new() { Code = code, Description = _localizer[key, args] };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Identity.Error.Default");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Identity.Error.ConcurrencyFailure");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Identity.Error.PasswordMismatch");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Identity.Error.InvalidToken");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "Identity.Error.LoginAlreadyAssociated");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Identity.Error.InvalidUserName", userName ?? string.Empty);
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Identity.Error.InvalidEmail", email ?? string.Empty);
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Identity.Error.DuplicateUserName", userName);
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Identity.Error.DuplicateEmail", email);
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), "Identity.Error.InvalidRoleName", role ?? string.Empty);
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), "Identity.Error.DuplicateRoleName", role);
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Identity.Error.UserAlreadyHasPassword");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Identity.Error.UserLockoutNotEnabled");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "Identity.Error.UserAlreadyInRole", role);
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "Identity.Error.UserNotInRole", role);
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), "Identity.Error.PasswordTooShort", length);
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), "Identity.Error.PasswordRequiresUniqueChars", uniqueChars);
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Identity.Error.PasswordRequiresNonAlphanumeric");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Identity.Error.PasswordRequiresDigit");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Identity.Error.PasswordRequiresLower");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Identity.Error.PasswordRequiresUpper");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "Identity.Error.RecoveryCodeRedemptionFailed");
}
