using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class ClassroomsController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<ClassroomsController> _logger;

    public ClassroomsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ILogger<ClassroomsController> logger)
    {
        _db = db;
        _users = users;
        _logger = logger;
    }

    private string TeacherId => _users.GetUserId(User)!;

    public async Task<IActionResult> Index(bool includeArchived = false)
    {
        var query = _db.Classrooms.Where(c => c.OwnerId == TeacherId);
        if (!includeArchived) query = query.Where(c => !c.IsArchived);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClassroomListItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                MemberCount = c.Members.Count,
                PendingInvites = c.Invites.Count(i => i.AcceptedAt == null && i.ExpiresAt > DateTime.UtcNow),
                IsArchived = c.IsArchived,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        ViewBag.IncludeArchived = includeArchived;
        return View(items);
    }

    public async Task<IActionResult> Details(int id)
    {
        var classroom = await _db.Classrooms
            .Include(c => c.Members).ThenInclude(m => m.Student)
            .Include(c => c.Invites)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);

        if (classroom == null) return NotFound();
        return View(classroom);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new ClassroomFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClassroomFormViewModel model)
    {
        if (!ModelState.IsValid) return View("Form", model);

        var classroom = new Classroom
        {
            Name = model.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            OwnerId = TeacherId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Classrooms.Add(classroom);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Classroom {Id} created by {Teacher}", classroom.Id, TeacherId);
        TempData["Success"] = "Classroom created.";
        return RedirectToAction(nameof(Details), new { id = classroom.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var classroom = await _db.Classrooms
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);
        if (classroom == null) return NotFound();

        return View("Form", new ClassroomFormViewModel
        {
            Id = classroom.Id,
            Name = classroom.Name,
            Description = classroom.Description
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ClassroomFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View("Form", model);

        var classroom = await _db.Classrooms
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);
        if (classroom == null) return NotFound();

        classroom.Name = model.Name.Trim();
        classroom.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = "Classroom updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        var classroom = await _db.Classrooms
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);
        if (classroom == null) return NotFound();

        classroom.IsArchived = true;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Classroom archived.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var classroom = await _db.Classrooms
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == TeacherId);
        if (classroom == null) return NotFound();

        classroom.IsArchived = false;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Classroom restored.";
        return RedirectToAction(nameof(Index), new { includeArchived = true });
    }
}
