using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameRunId",
                table: "ScoreSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: true),
                    FilterJson = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Answered = table.Column<int>(type: "int", nullable: false),
                    PlacementUnit = table.Column<int>(type: "int", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameRuns_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameRuns_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSnapshots_GameRunId",
                table: "ScoreSnapshots",
                column: "GameRunId",
                filter: "[GameRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GameRuns_ExerciseId",
                table: "GameRuns",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_GameRuns_UserId_Mode_ExerciseId",
                table: "GameRuns",
                columns: new[] { "UserId", "Mode", "ExerciseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameRuns");

            migrationBuilder.DropIndex(
                name: "IX_ScoreSnapshots_GameRunId",
                table: "ScoreSnapshots");

            migrationBuilder.DropColumn(
                name: "GameRunId",
                table: "ScoreSnapshots");
        }
    }
}
