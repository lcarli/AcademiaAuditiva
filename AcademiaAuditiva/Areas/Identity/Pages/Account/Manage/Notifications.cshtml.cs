#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AcademiaAuditiva.Models;

namespace AcademiaAuditiva.Areas.Identity.Pages.Account.Manage
{
    /// <summary>
    /// The e-mails a user can turn off: those about the routines teachers assign them, on until then.
    /// E-mails about the account itself, such as password resets, are always sent.
    /// </summary>
    public class NotificationsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        [Display(Name = "Manage.Notifications.RoutineEmails")]
        public bool RoutineEmails { get; set; }

        /// <summary>Unconfirmed addresses get none of these e-mails, so the page says so.</summary>
        public bool EmailConfirmed { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            RoutineEmails = !user.RoutineEmailsOff;
            EmailConfirmed = user.EmailConfirmed;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (user.RoutineEmailsOff == RoutineEmails)
            {
                user.RoutineEmailsOff = !RoutineEmails;
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    StatusMessage = "Error:Identity.Status.NotificationsError";
                    return RedirectToPage();
                }
            }

            StatusMessage = "Identity.Status.NotificationsSaved";
            return RedirectToPage();
        }
    }
}
