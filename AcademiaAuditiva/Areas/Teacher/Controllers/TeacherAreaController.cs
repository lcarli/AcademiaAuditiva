using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Policy = RoleNames.Teacher)]
public abstract class TeacherAreaController : Controller
{
}
