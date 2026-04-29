using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAdminSeedHardcodedPassword : Migration
    {
        // Historical Ids that previous migrations inserted with a hardcoded
        // password hash for the bootstrap admin user. Each migration in the
        // history of this project re-inserted (or overwrote) the same logical
        // user under a different Id, so we delete every row that was ever
        // seeded by a migration. Real users registered through Identity have
        // different (Guid-shaped, lower/mixed-case) Ids and are not affected.
        private static readonly string[] LegacySeedIds = new[]
        {
            "7f42424d-b5d9-48f9-97fb-e26b84811b0f",
            "1a63ee96-89d7-45e3-90ba-292da0d62711",
            "dcf21186-3e87-47f7-8d03-d4a3885c80ad",
            "aa14a2fe-8f0c-46e7-bc72-e687440a4ee4",
            "ae1f9507-e9fe-4b7c-b328-963389843d57",
            "8df19a87-f300-4bd5-a4fb-e87346b63b80",
            "caa93a42-f721-409d-989c-5e26b15429b2",
            "392fc301-1f60-456a-8659-59544f044f43",
            "5217d5e9-d774-4adb-8b3f-c8f7355de0a7",
            "4fc7f651-f679-443a-9a6f-ed0ebf743d62",
            "789ea4a4-dd57-45fe-9ee3-4363e9d6632c",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var id in LegacySeedIds)
            {
                migrationBuilder.DeleteData(
                    table: "AspNetUsers",
                    keyColumn: "Id",
                    keyValue: id);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally non-reversible: re-introducing the hardcoded
            // password hash would defeat the purpose of this migration.
        }
    }
}
