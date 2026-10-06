using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// An assigned routine is a test under way (#106): its exercises stay as they are, and a
/// duplicate is what changes instead. A student's adjustments to an exercise stay too once
/// they have started it, and an empty routine can't be assigned, since it would lock empty.
/// </summary>
public class RoutineLockTests : IClassFixture<RoutineLockTests.Factory>
{
    private const string Password = "Routine-Lock!Pass1";
    private const string Locked = "This routine is assigned, so its exercises can't change. Duplicate it to change a copy.";
    private const string LockedNote = "This routine is assigned, so its exercises can't change. To change them, duplicate the routine and assign the copy.";
    private const string Duplicated = "Routine duplicated. The copy isn't assigned, so you can change its exercises.";
    private const string Empty = "Add at least one exercise before assigning this routine.";
    private const string StartedNote = "This student has started this exercise, so its adjustments can't change.";
    private const string StartedKept = "Exercises this student has started keep their adjustments.";
    private const string ClearConfirm = "Remove all adjustments for this student? They will follow the routine again.";
    private const string ClearStartedConfirm = "Remove this student's adjustments? Exercises they have started keep theirs; the others will follow the routine again.";
    private const string SaveButton = "<button type=\"submit\" class=\"btn btn-primary\">Save</button>";
    private const string ClearForm = "action=\"/Teacher/Routines/ClearOverrides\"";

    private readonly Factory _factory;

    public RoutineLockTests(Factory factory) => _factory = factory;

    // ----- Exercises of an assigned routine -----

    [Theory]
    [InlineData("GET AddItem")]
    [InlineData("POST AddItem")]
    [InlineData("GET EditItem")]
    [InlineData("POST EditItem")]
    [InlineData("POST RemoveItem")]
    public async Task AssignedRoutine_KeepsItsExercises(string change)
    {
        var seeded = await SeedAsync();
        await AssignAsync(seeded);
        var client = await SignedInClientAsync(seeded.Teacher);
        var before = await ItemsAsync(seeded.RoutineId);
        var routineId = Id(seeded.RoutineId);
        var itemId = Id(seeded.ItemIds[0]);
        var exerciseId = Id(seeded.ExerciseId);

        var response = change switch
        {
            "GET AddItem" => await client.GetAsync($"/Teacher/Routines/AddItem?routineId={routineId}"),
            "POST AddItem" => await PostAsync(client, "/Teacher/Routines/AddItem", new()
            {
                ["RoutineId"] = routineId, ["ExerciseId"] = exerciseId, ["TargetCount"] = "3",
            }),
            "GET EditItem" => await client.GetAsync($"/Teacher/Routines/EditItem?routineId={routineId}&itemId={itemId}"),
            "POST EditItem" => await PostAsync(client, "/Teacher/Routines/EditItem", new()
            {
                ["Id"] = itemId, ["RoutineId"] = routineId, ["ExerciseId"] = exerciseId, ["TargetCount"] = "3",
            }),
            _ => await PostAsync(client, "/Teacher/Routines/RemoveItem", new()
            {
                ["routineId"] = routineId, ["itemId"] = itemId,
            }),
        };

        ShowsError(await FollowAsync(client, response, DetailsUrl(seeded)), Locked);
        (await ItemsAsync(seeded.RoutineId)).Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutinePage_OffersToChangeTheExercisesUntilTheRoutineIsAssigned(bool assigned)
    {
        var seeded = await SeedAsync();
        if (assigned) await AssignAsync(seeded);
        var client = await SignedInClientAsync(seeded.Teacher);

        var page = await PageTextAsync(client, DetailsUrl(seeded));

        string[] changes =
        [
            LockedNote,
            $"href=\"/Teacher/Routines/AddItem?routineId={seeded.RoutineId}\"",
            $"href=\"/Teacher/Routines/EditItem?routineId={seeded.RoutineId}&itemId={seeded.ItemIds[0]}\"",
            "action=\"/Teacher/Routines/RemoveItem\"",
        ];
        if (assigned)
        {
            page.Should().Contain(changes[0]);
            foreach (var change in changes[1..]) page.Should().NotContain(change);
        }
        else
        {
            page.Should().NotContain(changes[0]);
            foreach (var change in changes[1..]) page.Should().Contain(change);
        }
        page.Should().Contain("action=\"/Teacher/Routines/Duplicate\"")
            .And.Contain($"href=\"/Teacher/Routines/Edit/{seeded.RoutineId}\"", "the name and description can still change");
    }

    [Fact]
    public async Task AssignedRoutine_CanStillBeRenamed()
    {
        var seeded = await SeedAsync();
        await AssignAsync(seeded);
        var client = await SignedInClientAsync(seeded.Teacher);

        var response = await PostAsync(client, $"/Teacher/Routines/Edit/{seeded.RoutineId}", new()
        {
            ["Id"] = Id(seeded.RoutineId), ["Name"] = "Notes, week 2", ["Description"] = "Treble clef",
        });

        ShowsSuccess(await FollowAsync(client, response, DetailsUrl(seeded)), "Routine updated.");
        var routine = await RoutineAsync(seeded.RoutineId);
        routine.Name.Should().Be("Notes, week 2");
        routine.Description.Should().Be("Treble clef");
    }

    [Fact]
    public async Task RemovingTheAssignment_UnlocksTheExercises()
    {
        var seeded = await SeedAsync();
        var assignmentId = await AssignAsync(seeded);
        var client = await SignedInClientAsync(seeded.Teacher);

        var response = await PostAsync(client, "/Teacher/Routines/Unassign", new()
        {
            ["routineId"] = Id(seeded.RoutineId), ["assignmentId"] = Id(assignmentId),
        });

        (await FollowAsync(client, response, DetailsUrl(seeded))).Should().NotContain(LockedNote)
            .And.Contain($"href=\"/Teacher/Routines/AddItem?routineId={seeded.RoutineId}\"");
        (await client.GetAsync($"/Teacher/Routines/EditItem?routineId={seeded.RoutineId}&itemId={seeded.ItemIds[0]}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- Duplicate -----

    [Fact]
    public async Task Duplicate_CopiesTheExercisesButNotTheAssignments()
    {
        var seeded = await SeedAsync();
        var preset = ExerciseFilterPresets.Serialize(new Dictionary<string, string> { ["csRoot"] = "D" });
        await DbAsync(async db =>
        {
            var routine = await db.Routines.Include(r => r.Items).SingleAsync(r => r.Id == seeded.RoutineId);
            routine.Description = "Treble clef";
            var second = routine.Items.Single(i => i.Order == 2);
            second.TargetCount = 8;
            second.MinScore = 80;
            second.FilterJson = preset;
            return await db.SaveChangesAsync();
        });
        await AssignAsync(seeded);
        var client = await SignedInClientAsync(seeded.Teacher);

        var response = await PostAsync(client, "/Teacher/Routines/Duplicate", new() { ["id"] = Id(seeded.RoutineId) });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var copy = await CopyAsync(seeded);
        var page = await FollowAsync(client, response, $"/Teacher/Routines/Details/{copy.Id}");
        ShowsSuccess(page, Duplicated);
        page.Should().NotContain(LockedNote).And.Contain($"href=\"/Teacher/Routines/AddItem?routineId={copy.Id}\"");
        copy.Name.Should().Be("Notes (copy)");
        copy.Description.Should().Be("Treble clef");
        copy.CreatedAt.Should().Be(_factory.Clock.GetUtcNow().UtcDateTime);
        copy.Items.OrderBy(i => i.Order).Select(i => (i.ExerciseId, i.Order, i.TargetCount, i.MinScore, i.FilterJson))
            .Should().Equal((seeded.ExerciseId, 1, 5, null, null), (seeded.ExerciseId, 2, 8, 80, preset));
        copy.Items.Select(i => i.Id).Should().NotIntersectWith(seeded.ItemIds);
        (await DbAsync(db => db.RoutineAssignments.CountAsync(a => a.RoutineId == copy.Id))).Should().Be(0);
        (await DbAsync(db => db.RoutineAssignments.CountAsync(a => a.RoutineId == seeded.RoutineId))).Should().Be(1);
        (await client.GetAsync($"/Teacher/Routines/EditItem?routineId={copy.Id}&itemId={copy.Items.First().Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the copy's exercises can change");
    }

    [Theory]
    [InlineData("en-US", "Notes (copy)")]
    [InlineData("pt-BR", "Notes (cópia)")]
    [InlineData("fr-CA", "Notes (copie)")]
    public async Task Duplicate_NamesTheCopyInThePageLanguage(string culture, string expected)
    {
        var seeded = await SeedAsync();
        var client = await SignedInClientAsync(seeded.Teacher);

        var response = await PostAsync(client, $"/Teacher/Routines/Duplicate?culture={culture}", new() { ["id"] = Id(seeded.RoutineId) });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await CopyAsync(seeded)).Name.Should().Be(expected);
    }

    public static TheoryData<string, string> LongNames => new()
    {
        { new string('a', 120), new string('a', 113) + " (copy)" },
        // Cutting the name where it fits would split the emoji, so the emoji goes whole...
        { new string('a', 112) + "🎵" + new string('b', 6), new string('a', 112) + " (copy)" },
        // ...and so does a space the cut would end on.
        { new string('a', 112) + " " + new string('b', 7), new string('a', 112) + " (copy)" },
    };

    [Theory]
    [MemberData(nameof(LongNames))]
    public async Task Duplicate_ShortensTheNameToFit(string name, string expected)
    {
        var seeded = await SeedAsync(name);
        var client = await SignedInClientAsync(seeded.Teacher);

        var response = await PostAsync(client, "/Teacher/Routines/Duplicate", new() { ["id"] = Id(seeded.RoutineId) });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var copy = await CopyAsync(seeded);
        copy.Name.Should().Be(expected);
        copy.Name.Length.Should().BeLessThanOrEqualTo(Routine.NameMaxLength);
    }

    [Fact]
    public async Task Duplicate_OnlyCopiesTheTeachersOwnRoutines()
    {
        var seeded = await SeedAsync();
        var other = await CreateUserAsync(RoleNames.Teacher);
        var client = await SignedInClientAsync(other);

        var response = await PostAsync(client, "/Teacher/Routines/Duplicate", new() { ["id"] = Id(seeded.RoutineId) });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DbAsync(db => db.Routines.CountAsync(r => r.OwnerId == other.Id))).Should().Be(0);
    }

    // ----- Assigning an empty routine -----

    [Fact]
    public async Task EmptyRoutine_CannotBeAssigned()
    {
        var seeded = await SeedAsync(items: 0);
        var client = await SignedInClientAsync(seeded.Teacher);

        var form = await client.GetAsync($"/Teacher/Routines/Assign?routineId={seeded.RoutineId}");
        ShowsError(await FollowAsync(client, form, DetailsUrl(seeded)), Empty);
        var response = await PostAsync(client, "/Teacher/Routines/Assign", new()
        {
            ["RoutineId"] = Id(seeded.RoutineId), ["Target"] = "classroom", ["ClassroomId"] = Id(seeded.ClassroomId),
        });

        ShowsError(await FollowAsync(client, response, DetailsUrl(seeded)), Empty);
        (await DbAsync(db => db.RoutineAssignments.AnyAsync(a => a.RoutineId == seeded.RoutineId))).Should().BeFalse();
    }

    // ----- A student's adjustments -----

    [Fact]
    public async Task Adjustments_OfExercisesTheStudentHasStarted_ShowLocked()
    {
        var seeded = await SeedAsync(items: 3);
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 0, targetCount: 3);
        await AdjustAsync(seeded, assignmentId, item: 1, targetCount: 4);
        await StartAsync(seeded, assignmentId, item: 0);
        var client = await SignedInClientAsync(seeded.Teacher);

        var page = await PageTextAsync(client, OverridesUrl(seeded, assignmentId));

        var items = Fieldsets(page);
        items.Select(i => i.Disabled).Should().Equal(true, false, false);
        items[0].Html.Should().Contain(StartedNote)
            .And.Contain("name=\"Items[0].TargetCount\" value=\"3\"", "it shows what the student answers with")
            .And.NotContain("name=\"Items.index\"");
        items[1].Html.Should().Contain("name=\"Items.index\" value=\"1\"").And.NotContain(StartedNote);
        items[2].Html.Should().Contain("name=\"Items.index\" value=\"2\"");
        page.Should().Contain(SaveButton).And.Contain($"data-confirm=\"{ClearStartedConfirm}\"");
    }

    [Fact]
    public async Task Adjustments_StayOpenUntilTheStudentAnswersThatExerciseInThisAssignment()
    {
        var seeded = await SeedAsync();
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 0, targetCount: 3);
        // A classmate's answers, the student's in another assignment of the routine, or practice outside it.
        var classmate = await CreateUserAsync(RoleNames.Student);
        await StartAsync(seeded, assignmentId, item: 0, classmate.Id);
        await StartAsync(seeded, await AssignAsync(seeded), item: 0);
        await DbAsync(db =>
        {
            db.ScoreSnapshots.Add(new ScoreSnapshot
            {
                UserId = seeded.StudentId, ExerciseId = seeded.ExerciseId, IsCorrect = true, Timestamp = Now,
            });
            return db.SaveChangesAsync();
        });
        var client = await SignedInClientAsync(seeded.Teacher);

        var page = await PageTextAsync(client, OverridesUrl(seeded, assignmentId));

        Fieldsets(page).Select(i => i.Disabled).Should().Equal(false, false);
        page.Should().NotContain(StartedNote)
            .And.Contain("name=\"Items.index\" value=\"0\"").And.Contain("name=\"Items.index\" value=\"1\"")
            .And.Contain(SaveButton).And.Contain($"data-confirm=\"{ClearConfirm}\"");
    }

    [Fact]
    public async Task Adjustments_WhenTheStudentHasStartedEveryExercise_HaveNothingToSaveOrClear()
    {
        var seeded = await SeedAsync();
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 1, targetCount: 4);
        await StartAsync(seeded, assignmentId, item: 0);
        await StartAsync(seeded, assignmentId, item: 1);
        var client = await SignedInClientAsync(seeded.Teacher);

        var page = await PageTextAsync(client, OverridesUrl(seeded, assignmentId));

        Fieldsets(page).Select(i => i.Disabled).Should().Equal(true, true);
        page.Should().NotContain("name=\"Items.index\"").And.NotContain(SaveButton).And.NotContain(ClearForm);
    }

    [Fact]
    public async Task SavingAdjustments_ChangesOnlyExercisesTheStudentHasNotStarted()
    {
        var seeded = await SeedAsync(items: 3);
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 0, targetCount: 3);
        await AdjustAsync(seeded, assignmentId, item: 1, targetCount: 4);
        await StartAsync(seeded, assignmentId, item: 0);
        var client = await SignedInClientAsync(seeded.Teacher);
        var url = OverridesUrl(seeded, assignmentId);
        var form = FormFields(await PageAsync(client, url), "/Teacher/Routines/Overrides");

        Set(form, "Items[1].TargetCount", "6");
        Set(form, "Items[2].TargetCount", "2");
        var response = await client.PostAsync("/Teacher/Routines/Overrides", new FormUrlEncodedContent(form));

        ShowsSuccess(await FollowAsync(client, response, url), "Adjustments saved.");
        (await AdjustmentsAsync(seeded, assignmentId)).Should().BeEquivalentTo(new Dictionary<int, int?> { [0] = 3, [1] = 6, [2] = 2 });
    }

    [Fact]
    public async Task SavingAPageOpenedBeforeTheStudentStarted_KeepsTheAdjustmentsTheyStartedWith()
    {
        var seeded = await SeedAsync();
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 0, targetCount: 3);
        var client = await SignedInClientAsync(seeded.Teacher);
        var url = OverridesUrl(seeded, assignmentId);
        var form = FormFields(await PageAsync(client, url), "/Teacher/Routines/Overrides");
        await StartAsync(seeded, assignmentId, item: 0);

        Set(form, "Items[0].TargetCount", "9");
        Set(form, "Items[1].TargetCount", "6");
        var response = await client.PostAsync("/Teacher/Routines/Overrides", new FormUrlEncodedContent(form));

        ShowsSuccess(await FollowAsync(client, response, url), $"Adjustments saved. {StartedKept}");
        (await AdjustmentsAsync(seeded, assignmentId)).Should().BeEquivalentTo(new Dictionary<int, int?> { [0] = 3, [1] = 6 });
    }

    [Fact]
    public async Task ClearingAdjustments_KeepsThoseOfExercisesTheStudentHasStarted()
    {
        var seeded = await SeedAsync();
        var assignmentId = await AssignAsync(seeded);
        await AdjustAsync(seeded, assignmentId, item: 0, targetCount: 3);
        await AdjustAsync(seeded, assignmentId, item: 1, targetCount: 4);
        await StartAsync(seeded, assignmentId, item: 0);
        var client = await SignedInClientAsync(seeded.Teacher);
        var url = OverridesUrl(seeded, assignmentId);

        var response = await PostAsync(client, "/Teacher/Routines/ClearOverrides", new()
        {
            ["routineId"] = Id(seeded.RoutineId), ["assignmentId"] = Id(assignmentId), ["studentId"] = seeded.StudentId,
        });

        var page = await FollowAsync(client, response, url);
        ShowsSuccess(page, $"Adjustments removed. {StartedKept}");
        page.Should().NotContain(ClearForm, "only adjustments that can't change are left");
        (await AdjustmentsAsync(seeded, assignmentId)).Should().BeEquivalentTo(new Dictionary<int, int?> { [0] = 3 });
    }

    // ----- Helpers -----

    private DateTime Now => _factory.Clock.GetUtcNow().UtcDateTime;

    /// <param name="ItemIds">The routine's items, in order.</param>
    private sealed record Seeded(ApplicationUser Teacher, string StudentId, int ClassroomId, int RoutineId, int ExerciseId, int[] ItemIds);

    // A teacher's routine of GuessNote items, five questions each, and a classroom with a student to assign it to.
    private async Task<Seeded> SeedAsync(string name = "Notes", int items = 2)
    {
        var teacher = await CreateUserAsync(RoleNames.Teacher);
        var student = await CreateUserAsync(RoleNames.Student);
        return await DbAsync(async db =>
        {
            if (!await db.Exercises.AnyAsync()) SeedData.SeedExercises(db);
            var exerciseId = await db.Exercises.Where(e => e.Name == "GuessNote").Select(e => e.ExerciseId).SingleAsync();
            var routine = new Routine { Name = name, OwnerId = teacher.Id };
            for (var order = 1; order <= items; order++)
            {
                routine.Items.Add(new RoutineItem { ExerciseId = exerciseId, Order = order, TargetCount = 5 });
            }
            var classroom = new Classroom
            {
                Name = "Choir",
                OwnerId = teacher.Id,
                Members = { new ClassroomMember { StudentId = student.Id } }
            };
            db.Routines.Add(routine);
            db.Classrooms.Add(classroom);
            await db.SaveChangesAsync();
            var itemIds = routine.Items.OrderBy(i => i.Order).Select(i => i.Id).ToArray();
            return new Seeded(teacher, student.Id, classroom.Id, routine.Id, exerciseId, itemIds);
        });
    }

    // Assigns the routine to the classroom.
    private Task<int> AssignAsync(Seeded seeded) => DbAsync(async db =>
    {
        var assignment = new RoutineAssignment { RoutineId = seeded.RoutineId, ClassroomId = seeded.ClassroomId, AssignedAt = Now };
        db.RoutineAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    });

    // Gives the student their own number of questions for an item of the assignment.
    private Task AdjustAsync(Seeded seeded, int assignmentId, int item, int targetCount) => DbAsync(db =>
    {
        db.RoutineAssignmentOverrides.Add(new RoutineAssignmentOverride
        {
            RoutineAssignmentId = assignmentId,
            StudentId = seeded.StudentId,
            RoutineItemId = seeded.ItemIds[item],
            OverrideTargetCount = targetCount
        });
        return db.SaveChangesAsync();
    });

    // Saves a first answer to an item of the assignment, by the student unless someone else is given.
    private Task StartAsync(Seeded seeded, int assignmentId, int item, string? studentId = null) => DbAsync(db =>
    {
        db.ScoreSnapshots.Add(new ScoreSnapshot
        {
            UserId = studentId ?? seeded.StudentId,
            ExerciseId = seeded.ExerciseId,
            IsCorrect = true,
            Timestamp = Now,
            RoutineAssignmentId = assignmentId,
            RoutineItemId = seeded.ItemIds[item],
            RoutineQuestion = 1
        });
        return db.SaveChangesAsync();
    });

    // The student's own numbers of questions in the assignment, by item position.
    private Task<Dictionary<int, int?>> AdjustmentsAsync(Seeded seeded, int assignmentId) => DbAsync(async db =>
        (await db.RoutineAssignmentOverrides.AsNoTracking()
            .Where(o => o.RoutineAssignmentId == assignmentId && o.StudentId == seeded.StudentId)
            .ToListAsync())
        .ToDictionary(o => Array.IndexOf(seeded.ItemIds, o.RoutineItemId), o => o.OverrideTargetCount));

    private Task<List<(int Id, int ExerciseId, int Order, int TargetCount)>> ItemsAsync(int routineId) => DbAsync(async db =>
        (await db.RoutineItems.AsNoTracking().Where(i => i.RoutineId == routineId).OrderBy(i => i.Order).ToListAsync())
        .Select(i => (i.Id, i.ExerciseId, i.Order, i.TargetCount))
        .ToList());

    private Task<Routine> RoutineAsync(int id) => DbAsync(db => db.Routines.AsNoTracking().SingleAsync(r => r.Id == id));

    // The teacher's other routine, which Duplicate made.
    private Task<Routine> CopyAsync(Seeded seeded) => DbAsync(db => db.Routines.AsNoTracking().Include(r => r.Items)
        .SingleAsync(r => r.OwnerId == seeded.Teacher.Id && r.Id != seeded.RoutineId));

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static string DetailsUrl(Seeded seeded) => $"/Teacher/Routines/Details/{seeded.RoutineId}";

    private static string OverridesUrl(Seeded seeded, int assignmentId)
        => $"/Teacher/Routines/Overrides?routineId={seeded.RoutineId}&assignmentId={assignmentId}&studentId={seeded.StudentId}";

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    // The exercises of the adjustments page, each with whether it is disabled.
    private static List<(bool Disabled, string Html)> Fieldsets(string page)
        => Regex.Matches(page, "<fieldset([^>]*)>(.*?)</fieldset>", RegexOptions.Singleline)
            .Select(m => (m.Groups[1].Value.Contains(" disabled"), m.Groups[2].Value))
            .ToList();

    /// <summary>
    /// What a browser posts for the page's form with this action: its named inputs (checkboxes
    /// and radios only when checked) and selects, except those in a disabled fieldset.
    /// </summary>
    private static List<KeyValuePair<string, string>> FormFields(string html, string action)
    {
        var form = Regex.Match(html, $"<form[^>]*action=\"{Regex.Escape(action)}\"[^>]*>(.*?)</form>", RegexOptions.Singleline);
        form.Success.Should().BeTrue($"the page has a form that posts to {action}");
        var controls = Regex.Replace(form.Groups[1].Value, "<fieldset[^>]*disabled[^>]*>.*?</fieldset>", string.Empty, RegexOptions.Singleline);
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match control in Regex.Matches(controls, "<input([^>]*)>|<select([^>]*)>(.*?)</select>", RegexOptions.Singleline))
        {
            var isInput = control.Groups[1].Success;
            var attributes = isInput ? control.Groups[1].Value : control.Groups[2].Value;
            var name = Attribute(attributes, "name");
            if (name is null) continue;
            if (isInput)
            {
                var type = Attribute(attributes, "type");
                if (type is "checkbox" or "radio" && Attribute(attributes, "checked") is null) continue;
                fields.Add(new(name, Attribute(attributes, "value") ?? string.Empty));
            }
            else
            {
                var options = Regex.Matches(control.Groups[3].Value, "<option([^>]*)>").Select(o => o.Groups[1].Value).ToList();
                var chosen = options.FirstOrDefault(o => Attribute(o, "selected") is not null) ?? options.FirstOrDefault();
                if (chosen is not null) fields.Add(new(name, Attribute(chosen, "value") ?? string.Empty));
            }
        }
        return fields;
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"\\s{name}=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static void Set(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        var at = fields.FindIndex(f => f.Key == name);
        at.Should().BeGreaterThanOrEqualTo(0, $"the page posts {name}");
        fields[at] = new(name, value);
    }

    private static void ShowsSuccess(string page, string message)
        => page.Should().MatchRegex($"class=\"alert alert-success[^\"]*\" role=\"status\"[^>]*>\\s*<i [^>]*></i>\\s*<div>{Regex.Escape(message)}</div>");

    private static void ShowsError(string page, string message)
        => page.Should().MatchRegex($"class=\"alert alert-danger[^\"]*\" role=\"alert\">\\s*<i [^>]*></i>\\s*<div>{Regex.Escape(message)}</div>");

    // Expects a redirect to path, and returns the page there.
    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(path);
        return await PageTextAsync(client, path);
    }

    // Posts a form, with an antiforgery token from another page.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await PageAsync(client, "/Home/Privacy"));
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> PageTextAsync(HttpClient client, string url)
        => WebUtility.HtmlDecode(await PageAsync(client, url));

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the page renders an antiforgery token");
        return match.Groups[1].Value;
    }

    private async Task<ApplicationUser> CreateUserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            (await roles.CreateAsync(new IdentityRole(role))).Succeeded.Should().BeTrue();
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = role,
            LastName = "Tester",
        };
        var created = await users.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task<HttpClient> SignedInClientAsync(ApplicationUser user)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary><see cref="TestWebApplicationFactory"/> whose time stands still, so a test knows when a copy was made.</summary>
    public sealed class Factory : TestWebApplicationFactory
    {
        public AnswerTimeTests.ManualClock Clock { get; } = new(TimeProvider.System.GetUtcNow());

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
    }
}
