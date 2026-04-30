using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreSnapshotAndAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScoreAggregates",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    BestScore = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreAggregates", x => new { x.UserId, x.ExerciseId });
                    table.ForeignKey(
                        name: "FK_ScoreAggregates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreAggregates_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScoreSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoreSnapshots_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScoreSnapshots_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreAggregates_ExerciseId",
                table: "ScoreAggregates",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSnapshots_ExerciseId",
                table: "ScoreSnapshots",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSnapshots_UserId_ExerciseId_Timestamp",
                table: "ScoreSnapshots",
                columns: new[] { "UserId", "ExerciseId", "Timestamp" });

            // Backfill the aggregate from the latest legacy Score row per
            // (UserId, ExerciseId). Idempotent: re-running the migration
            // does nothing because both target tables are empty when this
            // INSERT runs (CreateTable just dropped+created them).
            migrationBuilder.Sql(@"
                INSERT INTO ScoreAggregates (UserId, ExerciseId, CorrectCount, ErrorCount, BestScore, LastAttemptAt)
                SELECT s.UserId, s.ExerciseId, s.CorrectCount, s.ErrorCount, s.BestScore, s.Timestamp
                FROM Scores s
                INNER JOIN (
                    SELECT UserId, ExerciseId, MAX(Timestamp) AS MaxTs
                    FROM Scores
                    GROUP BY UserId, ExerciseId
                ) latest
                  ON s.UserId = latest.UserId
                 AND s.ExerciseId = latest.ExerciseId
                 AND s.Timestamp  = latest.MaxTs;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoreAggregates");

            migrationBuilder.DropTable(
                name: "ScoreSnapshots");
        }
    }
}
