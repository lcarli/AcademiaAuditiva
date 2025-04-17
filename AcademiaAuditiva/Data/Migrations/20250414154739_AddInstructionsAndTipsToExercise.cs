using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstructionsAndTipsToExercise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "caa93a42-f721-409d-989c-5e26b15429b2");

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "Exercises",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipsJson",
                table: "Exercises",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "392fc301-1f60-456a-8659-59544f044f43", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEBAC/eEiyictKRSr+IIGQGJrrhqUd28fl0VPsZ+0UgqF1iP5a2dDjmDgRx9B84lMFw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "392fc301-1f60-456a-8659-59544f044f43");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "TipsJson",
                table: "Exercises");

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "caa93a42-f721-409d-989c-5e26b15429b2", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEMArqPuzBiBgToipjz90GXg8+BwhgQTu7OxiT8OAduudoCUCQEenPNiKqovxVs8dUA==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }
    }
}
