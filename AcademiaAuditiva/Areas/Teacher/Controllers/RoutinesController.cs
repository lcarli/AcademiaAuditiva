using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class RoutinesController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public RoutinesController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private string TeacherId => _users.GetUserId(User)!;

    private Task<Routine?> LoadOwnedAsync(int id)
        => _db.Routines.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == TeacherId);

    public async Task<IActionResult> Index()
    {
        var rows = await _db.Routines
            .Where(r => r.OwnerId == TeacherId)
            .OrderBy(r => r.Name)
            .Select(r => new RoutineListItem
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ItemCount = r.Items.Count,
                AssignmentCount = _db.RoutineAssignments.Count(a => a.RoutineId == r.Id)
            })
            .ToListAsync();
        return View(rows);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new RoutineFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoutineFormViewModel model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var routine = new Routine
        {
            Name = model.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            OwnerId = TeacherId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Routines.Add(routine);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Routine created.";
        return RedirectToAction(nameof(Details), new { id = routine.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var r = await LoadOwnedAsync(id);
        if (r == null) return NotFound();
        return View("Form", new RoutineFormViewModel
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RoutineFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var r = await LoadOwnedAsync(id);
        if (r == null) return NotFound();
        if (!ModelState.IsValid) return View("Form", model);
        r.Name = model.Name.Trim();
        r.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        await _db.SaveChangesAsync();
        TempData["Success"] = "Routine updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var r = await _db.Routines
            .Include(x => x.Items).ThenInclude(i => i.Exercise)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == TeacherId);
        if (r == null) return NotFound();
        ViewBag.Assignments = await _db.RoutineAssignments
            .Where(a => a.RoutineId == id)
            .Include(a => a.Classroom)
            .Include(a => a.Student)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync();
        return View(r);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _db.Routines
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == TeacherId);
        if (r == null) return NotFound();

        var hasAssignments = await _db.RoutineAssignments.AnyAsync(a => a.RoutineId == id);
        if (hasAssignments)
        {
            TempData["Error"] = "Routine has active assignments. Remove them first.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.Routines.Remove(r);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Routine deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ----- Items -----

    private async Task<RoutineItemFormViewModel> NewItemFormAsync(int routineId, RoutineItem? existing = null)
    {
        var options = await _db.Exercises
            .OrderBy(e => e.Name)
            .Select(e => new ExerciseOption(e.ExerciseId, e.Name))
            .ToListAsync();
        return new RoutineItemFormViewModel
        {
            Id = existing?.Id ?? 0,
            RoutineId = routineId,
            ExerciseId = existing?.ExerciseId ?? 0,
            TargetCount = existing?.TargetCount ?? 10,
            MinScore = existing?.MinScore,
            FilterJson = existing?.FilterJson,
            ExerciseOptions = options
        };
    }

    [HttpGet]
    public async Task<IActionResult> AddItem(int routineId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        return View("ItemForm", await NewItemFormAsync(routineId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(RoutineItemFormViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();
        if (!ModelState.IsValid)
        {
            model.ExerciseOptions = await _db.Exercises.OrderBy(e => e.Name)
                .Select(e => new ExerciseOption(e.ExerciseId, e.Name)).ToListAsync();
            return View("ItemForm", model);
        }

        var nextOrder = (await _db.RoutineItems.Where(i => i.RoutineId == r.Id)
            .Select(i => (int?)i.Order).MaxAsync() ?? 0) + 1;

        _db.RoutineItems.Add(new RoutineItem
        {
            RoutineId = r.Id,
            ExerciseId = model.ExerciseId,
            TargetCount = model.TargetCount,
            MinScore = model.MinScore,
            FilterJson = string.IsNullOrWhiteSpace(model.FilterJson) ? null : model.FilterJson,
            Order = nextOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Exercise added to routine.";
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }

    [HttpGet]
    public async Task<IActionResult> EditItem(int routineId, int itemId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoutineId == routineId);
        if (item == null) return NotFound();
        return View("ItemForm", await NewItemFormAsync(routineId, item));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(RoutineItemFormViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == model.Id && i.RoutineId == r.Id);
        if (item == null) return NotFound();
        if (!ModelState.IsValid)
        {
            model.ExerciseOptions = await _db.Exercises.OrderBy(e => e.Name)
                .Select(e => new ExerciseOption(e.ExerciseId, e.Name)).ToListAsync();
            return View("ItemForm", model);
        }
        item.ExerciseId = model.ExerciseId;
        item.TargetCount = model.TargetCount;
        item.MinScore = model.MinScore;
        item.FilterJson = string.IsNullOrWhiteSpace(model.FilterJson) ? null : model.FilterJson;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Item updated.";
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int routineId, int itemId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoutineId == routineId);
        if (item == null) return NotFound();
        _db.RoutineItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Item removed.";
        return RedirectToAction(nameof(Details), new { id = routineId });
    }

    // ----- Assignments -----

    private async Task PopulateAssignChoicesAsync(AssignRoutineViewModel vm)
    {
        vm.Classrooms = await _db.Classrooms
            .Where(c => c.OwnerId == TeacherId && !c.IsArchived)
            .OrderBy(c => c.Name)
            .Select(c => new ClassroomOption(c.Id, c.Name))
            .ToListAsync();
        vm.Students = await _db.ClassroomMembers
            .Where(m => m.Classroom!.OwnerId == TeacherId && !m.Classroom.IsArchived)
            .Select(m => new { m.StudentId, m.Student!.UserName, m.Student.Email, Classroom = m.Classroom!.Name })
            .Distinct()
            .OrderBy(x => x.UserName)
            .Select(x => new StudentOption(x.StudentId, x.UserName + " (" + x.Email + ") — " + x.Classroom))
            .ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> Assign(int routineId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        var vm = new AssignRoutineViewModel { RoutineId = r.Id, RoutineName = r.Name };
        await PopulateAssignChoicesAsync(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(AssignRoutineViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();

        if (model.Target == "classroom")
        {
            if (model.ClassroomId is null)
                ModelState.AddModelError(nameof(model.ClassroomId), "Select a classroom.");
            else
            {
                var ownsClass = await _db.Classrooms.AnyAsync(c =>
                    c.Id == model.ClassroomId && c.OwnerId == TeacherId);
                if (!ownsClass) return Forbid();
            }
        }
        else if (model.Target == "student")
        {
            if (string.IsNullOrEmpty(model.StudentId))
                ModelState.AddModelError(nameof(model.StudentId), "Select a student.");
            else
            {
                var ownsStudent = await _db.ClassroomMembers.AnyAsync(m =>
                    m.StudentId == model.StudentId && m.Classroom!.OwnerId == TeacherId);
                if (!ownsStudent) return Forbid();
            }
        }
        else
        {
            ModelState.AddModelError(nameof(model.Target), "Invalid target.");
        }

        if (!ModelState.IsValid)
        {
            model.RoutineName = r.Name;
            await PopulateAssignChoicesAsync(model);
            return View(model);
        }

        _db.RoutineAssignments.Add(new RoutineAssignment
        {
            RoutineId = r.Id,
            ClassroomId = model.Target == "classroom" ? model.ClassroomId : null,
            StudentId = model.Target == "student" ? model.StudentId : null,
            AssignedAt = DateTime.UtcNow,
            DueAt = model.DueAt
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "Routine assigned.";
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int routineId, int assignmentId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        var a = await _db.RoutineAssignments
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.RoutineId == routineId);
        if (a == null) return NotFound();
        _db.RoutineAssignments.Remove(a);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Assignment removed.";
        return RedirectToAction(nameof(Details), new { id = routineId });
    }
}
