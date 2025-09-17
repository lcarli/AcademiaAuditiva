using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Security.Claims;

namespace AcademiaAuditiva.Controllers
{
    [Authorize]
    public class ProfessorPortalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IStringLocalizer<SharedResources> _localizer;
        private readonly UserReportService _userReportService;

        public ProfessorPortalController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResources> localizer, UserReportService userReportService)
        {
            _context = context;
            _userManager = userManager;
            _localizer = localizer;
            _userReportService = userReportService;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            ViewBag.FirstName = user.FirstName;


            return View();
        }

        [AllowAnonymous]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
            );

            return LocalRedirect(returnUrl);
        }

        

        [HttpGet]
        public IActionResult GetExerciseTranslations()
        {
            var exerciseNames = _context.Exercises
                .Select(e => e.Name)
                .Distinct()
                .ToList();

            var translations = exerciseNames.ToDictionary(
                name => name,
                name => _localizer[$"{name}"].Value
            );

            return Json(translations);
        }
    }
}