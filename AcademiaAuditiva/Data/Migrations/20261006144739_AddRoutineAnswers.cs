using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutineAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoutineAssignmentId",
                table: "ScoreSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutineItemId",
                table: "ScoreSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutineQuestion",
                table: "ScoreSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowLate",
                table: "RoutineAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSnapshots_UserId_RoutineAssignmentId_RoutineItemId_RoutineQuestion",
                table: "ScoreSnapshots",
                columns: new[] { "UserId", "RoutineAssignmentId", "RoutineItemId", "RoutineQuestion" },
                unique: true,
                filter: "[RoutineAssignmentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScoreSnapshots_UserId_RoutineAssignmentId_RoutineItemId_RoutineQuestion",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "RoutineAssignmentId",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "RoutineItemId",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "RoutineQuestion",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "AllowLate",
                table: "RoutineAssignments");
        }
    }
}
