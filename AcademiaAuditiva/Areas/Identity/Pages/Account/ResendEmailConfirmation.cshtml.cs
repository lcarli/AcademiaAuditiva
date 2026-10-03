// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.Email;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [EnableRateLimiting(AccountFormsRateLimitPolicy.Name)]
    public class ResendEmailConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailMessageSender _emailSender;
        private readonly EmailComposer _emailComposer;
        private readonly IStringLocalizer<SharedResources> _localizer;

        public ResendEmailConfirmationModel(UserManager<ApplicationUser> userManager, IEmailMessageSender emailSender, EmailComposer emailComposer, IStringLocalizer<SharedResources> localizer)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _emailComposer = emailComposer;
            _localizer = localizer;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required(ErrorMessage = "Validation.Required")]
            [EmailAddress(ErrorMessage = "Validation.EmailAddress")]
            [Display(Name = "Account.Email")]
            public string Email { get; set; }
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, _localizer["Identity.Status.VerificationEmailSent"]);
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { userId = userId, code = code },
                protocol: Request.Scheme);
            // Answer the same way when sending fails, so a mail outage
            // doesn't reveal which addresses have accounts.
            await _emailSender.TrySendEmailAsync(Input.Email, await _emailComposer.ConfirmAccountAsync(callbackUrl));

            ModelState.AddModelError(string.Empty, _localizer["Identity.Status.VerificationEmailSent"]);
            return Page();
        }
    }
}
