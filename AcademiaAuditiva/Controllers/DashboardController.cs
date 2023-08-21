using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
