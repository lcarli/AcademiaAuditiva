namespace AcademiaAuditiva.Areas.Admin.Models;

public class UserListRow
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsTeacher { get; set; }
    public bool IsStudent { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public bool EmailConfirmed { get; set; }
}

public class UserListViewModel
{
    public IReadOnlyList<UserListRow> Rows { get; set; } = Array.Empty<UserListRow>();
    public string? Query { get; set; }
    public string? Role { get; set; } // "Admin" | "Teacher" | "Student" | null
    public int Total { get; set; }
}
