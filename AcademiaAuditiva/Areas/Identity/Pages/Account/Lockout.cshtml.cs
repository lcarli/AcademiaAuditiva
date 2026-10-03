// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Shown when a locked account tries to sign in. It doesn't say which lock applies:
    ///     a lockout after wrong passwords or codes, or an admin's lock.
    /// </summary>
    [AllowAnonymous]
    public class LockoutModel : PageModel
    {
        public LockoutModel(IOptions<IdentityOptions> identityOptions)
        {
            LockoutMinutes = (int)identityOptions.Value.Lockout.DefaultLockoutTimeSpan.TotalMinutes;
        }

        public int LockoutMinutes { get; }

        public void OnGet()
        {
        }
    }
}
