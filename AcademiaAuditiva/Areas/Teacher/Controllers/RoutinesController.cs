using AcademiaAuditiva.Areas.Teacher.Models;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace AcademiaAuditiva.Areas.Teacher.Controllers;

public class RoutinesController : TeacherAreaController
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IStringLocalizer<SharedResources> _l;
    private readonly TimeProvider _clock;

    public RoutinesController(ApplicationDbContext db, UserManager<ApplicationUser> users, IStringLocalizer<SharedResources> localizer, TimeProvider clock)
    {
        _db = db;
        _users = users;
        _l = localizer;
        _clock = clock;
    }

    private string TeacherId => _users.GetUserId(User)!;

    private Task<Routine?> LoadOwnedAsync(int id)
        => _db.Routines.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == TeacherId);

    /// <summary>
    /// An assigned routine is a test under way: its items stay as they are, so every answer
    /// keeps counting towards what the students were given. Removing every assignment unlocks it.
    /// </summary>
    private Task<bool> IsAssignedAsync(int routineId)
        => _db.RoutineAssignments.AnyAsync(a => a.RoutineId == routineId);

    private IActionResult RoutineLocked(int routineId) => Refused(routineId, "Toast.RoutineLocked");

    /// <summary>Back to the routine's page, with the reason in an error toast.</summary>
    private IActionResult Refused(int routineId, string toastKey)
    {
        TempData["Error"] = _l[toastKey].Value;
        return RedirectToAction(nameof(Details), new { id = routineId });
    }

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
        TempData["Success"] = _l["Toast.RoutineCreated"].Value;
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
        TempData["Success"] = _l["Toast.RoutineUpdated"].Value;
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
            .Include(a => a.Overrides)
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
            TempData["Error"] = _l["Toast.RoutineActiveAssignments"].Value;
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.Routines.Remove(r);
        await _db.SaveChangesAsync();
        TempData["Success"] = _l["Toast.RoutineDeleted"].Value;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Copies the routine and its items, without assignments, so the copy can change.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int id)
    {
        var r = await _db.Routines
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == TeacherId);
        if (r == null) return NotFound();

        var copy = new Routine
        {
            Name = CopyName(r.Name),
            Description = r.Description,
            OwnerId = TeacherId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            Items = r.Items.OrderBy(i => i.Order).Select(i => new RoutineItem
            {
                ExerciseId = i.ExerciseId,
                Order = i.Order,
                FilterJson = i.FilterJson,
                TargetCount = i.TargetCount,
                MinScore = i.MinScore
            }).ToList()
        };
        _db.Routines.Add(copy);
        await _db.SaveChangesAsync();
        TempData["Success"] = _l["Toast.RoutineDuplicated"].Value;
        return RedirectToAction(nameof(Details), new { id = copy.Id });
    }

    /// <summary>"Name (copy)" in the page's language, shortening the name to fit.</summary>
    private string CopyName(string name)
    {
        var copy = _l["Teacher.Routines.CopyName", name].Value;
        var excess = copy.Length - Routine.NameMaxLength;
        if (excess <= 0) return copy;
        var shortened = name[..^excess];
        if (shortened.Length > 0 && char.IsHighSurrogate(shortened[^1])) shortened = shortened[..^1];
        return _l["Teacher.Routines.CopyName", shortened.TrimEnd()].Value;
    }

    // ----- Items -----

    private string LocalizedExerciseName(string name)
    {
        var localized = _l[name];
        return localized.ResourceNotFound ? name : localized.Value;
    }

    private async Task<IReadOnlyList<ExerciseOption>> ExerciseOptionsAsync()
    {
        var exercises = await _db.Exercises
            .Select(e => new { e.ExerciseId, e.Name, e.FiltersJson })
            .ToListAsync();
        return exercises
            .Select(e => new ExerciseOption(e.ExerciseId, LocalizedExerciseName(e.Name), ExerciseFilterPresets.Groups(e.FiltersJson)))
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<RoutineItemFormViewModel> NewItemFormAsync(int routineId, RoutineItem? existing = null)
    {
        return new RoutineItemFormViewModel
        {
            Id = existing?.Id ?? 0,
            RoutineId = routineId,
            ExerciseId = existing?.ExerciseId ?? 0,
            TargetCount = existing?.TargetCount ?? 10,
            MinScore = existing?.MinScore,
            Filters = ExerciseFilterPresets.Parse(existing?.FilterJson)
                .ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
            ExerciseOptions = await ExerciseOptionsAsync()
        };
    }

    /// <summary>
    /// Validates the posted exercise and returns the sanitized filter preset
    /// as JSON. Re-populates the form's options when validation fails.
    /// </summary>
    private async Task<(bool Ok, string? FilterJson)> ValidateItemFormAsync(RoutineItemFormViewModel model)
    {
        var options = await ExerciseOptionsAsync();
        var exercise = options.FirstOrDefault(o => o.Id == model.ExerciseId);
        if (exercise is null)
        {
            ModelState.Remove(nameof(model.ExerciseId));
            ModelState.AddModelError(nameof(model.ExerciseId), _l["Teacher.Routines.SelectExercise"]);
        }
        if (!ModelState.IsValid)
        {
            model.ExerciseOptions = options;
            return (false, null);
        }
        var filters = ExerciseFilterPresets.Sanitize(model.Filters, exercise!.FilterGroups);
        return (true, ExerciseFilterPresets.Serialize(filters));
    }

    [HttpGet]
    public async Task<IActionResult> AddItem(int routineId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        if (await IsAssignedAsync(r.Id)) return RoutineLocked(r.Id);
        return View("ItemForm", await NewItemFormAsync(routineId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(RoutineItemFormViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();
        if (await IsAssignedAsync(r.Id)) return RoutineLocked(r.Id);
        var (ok, filterJson) = await ValidateItemFormAsync(model);
        if (!ok) return View("ItemForm", model);

        var nextOrder = (await _db.RoutineItems.Where(i => i.RoutineId == r.Id)
            .Select(i => (int?)i.Order).MaxAsync() ?? 0) + 1;

        _db.RoutineItems.Add(new RoutineItem
        {
            RoutineId = r.Id,
            ExerciseId = model.ExerciseId,
            TargetCount = model.TargetCount,
            MinScore = model.MinScore,
            FilterJson = filterJson,
            Order = nextOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = _l["Toast.ItemAdded"].Value;
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }

    [HttpGet]
    public async Task<IActionResult> EditItem(int routineId, int itemId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        if (await IsAssignedAsync(r.Id)) return RoutineLocked(r.Id);
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoutineId == routineId);
        if (item == null) return NotFound();
        return View("ItemForm", await NewItemFormAsync(routineId, item));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditItem(RoutineItemFormViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();
        if (await IsAssignedAsync(r.Id)) return RoutineLocked(r.Id);
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == model.Id && i.RoutineId == r.Id);
        if (item == null) return NotFound();
        var (ok, filterJson) = await ValidateItemFormAsync(model);
        if (!ok) return View("ItemForm", model);

        item.ExerciseId = model.ExerciseId;
        item.TargetCount = model.TargetCount;
        item.MinScore = model.MinScore;
        item.FilterJson = filterJson;
        await _db.SaveChangesAsync();
        TempData["Success"] = _l["Toast.ItemUpdated"].Value;
        return RedirectToAction(nameof(Details), new { id = r.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int routineId, int itemId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        if (await IsAssignedAsync(r.Id)) return RoutineLocked(r.Id);
        var item = await _db.RoutineItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoutineId == routineId);
        if (item == null) return NotFound();
        // Overrides reference items with a restrict FK, so they go first.
        var overrides = await _db.RoutineAssignmentOverrides.Where(o => o.RoutineItemId == item.Id).ToListAsync();
        _db.RoutineAssignmentOverrides.RemoveRange(overrides);
        _db.RoutineItems.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = _l["Toast.ItemRemoved"].Value;
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

    // An empty routine would lock as soon as it is assigned, with nothing in it to answer.
    private Task<bool> HasItemsAsync(int routineId)
        => _db.RoutineItems.AnyAsync(i => i.RoutineId == routineId);

    [HttpGet]
    public async Task<IActionResult> Assign(int routineId)
    {
        var r = await LoadOwnedAsync(routineId);
        if (r == null) return NotFound();
        if (!await HasItemsAsync(r.Id)) return Refused(r.Id, "Toast.RoutineEmpty");
        var vm = new AssignRoutineViewModel { RoutineId = r.Id, RoutineName = r.Name };
        await PopulateAssignChoicesAsync(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(AssignRoutineViewModel model)
    {
        var r = await LoadOwnedAsync(model.RoutineId);
        if (r == null) return NotFound();
        if (!await HasItemsAsync(r.Id)) return Refused(r.Id, "Toast.RoutineEmpty");

        if (model.Target == "classroom")
        {
            if (model.ClassroomId is null)
                ModelState.AddModelError(nameof(model.ClassroomId), _l["Teacher.Routines.SelectClassroom"]);
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
                ModelState.AddModelError(nameof(model.StudentId), _l["Teacher.Routines.SelectStudent"]);
            else
            {
                var ownsStudent = await _db.ClassroomMembers.AnyAsync(m =>
                    m.StudentId == model.StudentId && m.Classroom!.OwnerId == TeacherId);
                if (!ownsStudent) return Forbid();
            }
        }
        else
        {
            ModelState.AddModelError(nameof(model.Target), _l["Teacher.Routines.InvalidTarget"]);
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
            DueAt = model.DueAt,
            AllowLate = model.DueAt.HasValue && model.AllowLate
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = _l["Toast.RoutineAssigned"].Value;
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
        TempData["Success"] = _l["Toast.AssignmentRemoved"].Value;
        return RedirectToAction(nameof(Details), new { id = routineId });
    }

    // ----- Per-student overrides (classroom assignments only) -----

    /// <summary>
    /// Loads a classroom assignment of a routine the current teacher owns,
    /// in a classroom the teacher owns. Anything else is treated as not found.
    /// </summary>
    private async Task<(Routine Routine, RoutineAssignment Assignment)?> LoadOwnedClassroomAssignmentAsync(int routineId, int assignmentId)
    {
        var routine = await _db.Routines
            .Include(r => r.Items).ThenInclude(i => i.Exercise)
            .FirstOrDefaultAsync(r => r.Id == routineId && r.OwnerId == TeacherId);
        if (routine == null) return null;
        var assignment = await _db.RoutineAssignments
            .Include(a => a.Classroom)
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.RoutineId == routineId);
        if (assignment?.ClassroomId == null || assignment.Classroom?.OwnerId != TeacherId) return null;
        return (routine, assignment);
    }

    private Task<bool> IsClassroomMemberAsync(int classroomId, string studentId)
        => _db.ClassroomMembers.AnyAsync(m => m.ClassroomId == classroomId && m.StudentId == studentId);

    /// <summary>
    /// The items the student has answered in this assignment. Their adjustments stay as they
    /// are, since the student is answering with them.
    /// </summary>
    private async Task<HashSet<int>> StartedItemsAsync(int assignmentId, string studentId)
    {
        var started = await _db.ScoreSnapshots
            .Where(s => s.UserId == studentId && s.RoutineAssignmentId == assignmentId && s.RoutineItemId != null)
            .Select(s => s.RoutineItemId!.Value)
            .Distinct()
            .ToListAsync();
        return started.ToHashSet();
    }

    private async Task PopulateOverridesAsync(RoutineOverridesViewModel vm, Routine routine, RoutineAssignment assignment, bool fromDb)
    {
        vm.RoutineName = routine.Name;
        vm.ClassroomName = assignment.Classroom?.Name ?? string.Empty;

        var counts = await _db.RoutineAssignmentOverrides
            .Where(o => o.RoutineAssignmentId == assignment.Id)
            .GroupBy(o => o.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudentId, x => x.Count);
        var members = await _db.ClassroomMembers
            .Where(m => m.ClassroomId == assignment.ClassroomId)
            .Select(m => new { m.StudentId, m.Student!.UserName, m.Student.Email })
            .ToListAsync();
        vm.Students = members
            .Select(m => new OverrideStudentOption(
                m.StudentId,
                string.IsNullOrEmpty(m.Email) || m.Email == m.UserName ? (m.UserName ?? m.StudentId) : $"{m.UserName} ({m.Email})",
                counts.GetValueOrDefault(m.StudentId)))
            .OrderBy(s => s.Display, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (vm.StudentId == null) return;
        vm.StudentDisplay = vm.Students.FirstOrDefault(s => s.Id == vm.StudentId)?.Display;

        var saved = await _db.RoutineAssignmentOverrides
            .Where(o => o.RoutineAssignmentId == assignment.Id && o.StudentId == vm.StudentId)
            .ToListAsync();
        var started = await StartedItemsAsync(assignment.Id, vm.StudentId);
        vm.CanClear = saved.Any(o => !started.Contains(o.RoutineItemId));
        vm.KeepsStarted = saved.Any(o => started.Contains(o.RoutineItemId));
        var posted = fromDb
            ? new Dictionary<int, RoutineItemOverrideInput>()
            : vm.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First());

        vm.Items = routine.Items.OrderBy(i => i.Order).Select(item =>
        {
            var isStarted = started.Contains(item.Id);
            // Started items, which the page doesn't post, and any item missing from the post show what is saved.
            if (isStarted || !posted.TryGetValue(item.Id, out var input))
            {
                var o = saved.FirstOrDefault(x => x.RoutineItemId == item.Id);
                input = new RoutineItemOverrideInput
                {
                    ItemId = item.Id,
                    Exclude = o?.ExcludeItem ?? false,
                    TargetCount = o?.OverrideTargetCount,
                    Filters = ExerciseFilterPresets.Parse(o?.OverrideFilterJson)
                        .ToDictionary(kv => kv.Key, kv => (string?)kv.Value)
                };
            }
            var groups = ExerciseFilterPresets.Groups(item.Exercise?.FiltersJson);
            input.ExerciseName = LocalizedExerciseName(item.Exercise?.Name ?? string.Empty);
            input.Started = isStarted;
            input.DefaultTarget = item.TargetCount;
            input.FilterGroups = groups;
            input.DefaultFilters = ExerciseFilterPresets.Sanitize(
                ExerciseFilterPresets.Parse(item.FilterJson).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)),
                groups);
            return input;
        }).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Overrides(int routineId, int assignmentId, string? studentId)
    {
        var owned = await LoadOwnedClassroomAssignmentAsync(routineId, assignmentId);
        if (owned is null) return NotFound();
        var (routine, assignment) = owned.Value;
        if (studentId != null && !await IsClassroomMemberAsync(assignment.ClassroomId!.Value, studentId)) return NotFound();

        var vm = new RoutineOverridesViewModel { RoutineId = routineId, AssignmentId = assignmentId, StudentId = studentId };
        await PopulateOverridesAsync(vm, routine, assignment, fromDb: true);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Overrides(RoutineOverridesViewModel model)
    {
        var owned = await LoadOwnedClassroomAssignmentAsync(model.RoutineId, model.AssignmentId);
        if (owned is null) return NotFound();
        var (routine, assignment) = owned.Value;
        if (string.IsNullOrEmpty(model.StudentId) || !await IsClassroomMemberAsync(assignment.ClassroomId!.Value, model.StudentId))
            return NotFound();

        if (!ModelState.IsValid)
        {
            await PopulateOverridesAsync(model, routine, assignment, fromDb: false);
            return View(model);
        }

        var existing = await _db.RoutineAssignmentOverrides
            .Where(o => o.RoutineAssignmentId == assignment.Id && o.StudentId == model.StudentId)
            .ToListAsync();
        var started = await StartedItemsAsync(assignment.Id, model.StudentId);
        var keptStarted = false;
        var items = routine.Items.ToDictionary(i => i.Id);
        var seen = new HashSet<int>();

        foreach (var input in model.Items)
        {
            // Only items of this routine, once each; anything else is ignored.
            if (!items.TryGetValue(input.ItemId, out var item) || !seen.Add(item.Id)) continue;
            // The page doesn't post started items, unless it was opened before the student started.
            if (started.Contains(item.Id))
            {
                keptStarted = true;
                continue;
            }

            var filters = ExerciseFilterPresets.Sanitize(input.Filters, ExerciseFilterPresets.Groups(item.Exercise?.FiltersJson));
            var row = existing.FirstOrDefault(o => o.RoutineItemId == item.Id);
            var isDefault = !input.Exclude && input.TargetCount == null && filters.Count == 0;

            if (isDefault)
            {
                if (row != null) _db.RoutineAssignmentOverrides.Remove(row);
                continue;
            }
            if (row == null)
            {
                row = new RoutineAssignmentOverride
                {
                    RoutineAssignmentId = assignment.Id,
                    StudentId = model.StudentId,
                    RoutineItemId = item.Id
                };
                _db.RoutineAssignmentOverrides.Add(row);
            }
            row.ExcludeItem = input.Exclude;
            row.OverrideTargetCount = input.TargetCount;
            row.OverrideFilterJson = ExerciseFilterPresets.Serialize(filters);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = WithStartedKept(_l["Toast.OverridesSaved"].Value, keptStarted);
        return RedirectToAction(nameof(Overrides), new { routineId = routine.Id, assignmentId = assignment.Id, studentId = model.StudentId });
    }

    private string WithStartedKept(string toast, bool keptStarted)
        => keptStarted ? $"{toast} {_l["Toast.OverridesStartedKept"].Value}" : toast;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearOverrides(int routineId, int assignmentId, string studentId)
    {
        var owned = await LoadOwnedClassroomAssignmentAsync(routineId, assignmentId);
        if (owned is null) return NotFound();
        var (routine, assignment) = owned.Value;
        if (string.IsNullOrEmpty(studentId) || !await IsClassroomMemberAsync(assignment.ClassroomId!.Value, studentId))
            return NotFound();

        var rows = await _db.RoutineAssignmentOverrides
            .Where(o => o.RoutineAssignmentId == assignment.Id && o.StudentId == studentId)
            .ToListAsync();
        var started = await StartedItemsAsync(assignment.Id, studentId);
        _db.RoutineAssignmentOverrides.RemoveRange(rows.Where(o => !started.Contains(o.RoutineItemId)));
        await _db.SaveChangesAsync();
        TempData["Success"] = WithStartedKept(_l["Toast.OverridesCleared"].Value, rows.Any(o => started.Contains(o.RoutineItemId)));
        return RedirectToAction(nameof(Overrides), new { routineId = routine.Id, assignmentId = assignment.Id, studentId });
    }
}
