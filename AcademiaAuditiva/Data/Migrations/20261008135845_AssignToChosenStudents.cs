using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssignToChosenStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChosenStudentsOnly",
                table: "RoutineAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AspNetUsers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoutineEmailsOff",
                table: "AspNetUsers",
                type: "bit",
                nullable: true,
                defaultValue: false);

            // The column is nullable (AspNetUsers holds every IdentityUser), and its default only
            // fills new rows: existing users get e-mails too, and must load as an ApplicationUser.
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [RoutineEmailsOff] = CAST(0 AS bit) WHERE [RoutineEmailsOff] IS NULL;");

            migrationBuilder.CreateTable(
                name: "EmailDailyCounts",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Sent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDailyCounts", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "RoutineAssignmentStudents",
                columns: table => new
                {
                    RoutineAssignmentId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineAssignmentStudents", x => new { x.RoutineAssignmentId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_RoutineAssignmentStudents_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoutineAssignmentStudents_RoutineAssignments_RoutineAssignmentId",
                        column: x => x.RoutineAssignmentId,
                        principalTable: "RoutineAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignmentStudents_StudentId",
                table: "RoutineAssignmentStudents",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDailyCounts");

            migrationBuilder.DropTable(
                name: "RoutineAssignmentStudents");

            migrationBuilder.DropColumn(
                name: "ChosenStudentsOnly",
                table: "RoutineAssignments");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RoutineEmailsOff",
                table: "AspNetUsers");
        }
    }
}
