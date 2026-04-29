namespace AcademiaAuditiva.Models
{
    /// <summary>
    /// Centralized role name constants. Use these instead of magic strings
    /// when adding [Authorize(Roles = ...)] or calling UserManager APIs.
    /// </summary>
    public static class RoleNames
    {
        public const string Admin = "Admin";
        public const string Teacher = "Teacher";
        public const string Student = "Student";

        public static readonly string[] All = { Admin, Teacher, Student };
    }
}
