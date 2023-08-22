using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExercisePlans");

            migrationBuilder.DropTable(
                name: "ExerciseResponses");

            migrationBuilder.DropTable(
                name: "TeachingPlanEnrollments");

            migrationBuilder.DropTable(
                name: "TeachingPlans");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "1a63ee96-89d7-45e3-90ba-292da0d62711");

            migrationBuilder.DropColumn(
                name: "AudioUrl",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "SuggestedDuration",
                table: "Exercises");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "Exercises",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<int>(
                name: "Difficulty",
                table: "Exercises",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateTable(
                name: "Scores",
                columns: table => new
                {
                    ScoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scores", x => x.ScoreId);
                    table.ForeignKey(
                        name: "FK_Scores_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scores_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "dcf21186-3e87-47f7-8d03-d4a3885c80ad", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEEQVh3QEEav7BvcUR6DaPzvInXDQEE5FHeO5eJ0bxo19BSRivigNLuS0Ix4CXUemig==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });

            migrationBuilder.CreateIndex(
                name: "IX_Scores_ExerciseId",
                table: "Scores",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_UserId",
                table: "Scores",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Scores");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "dcf21186-3e87-47f7-8d03-d4a3885c80ad");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Exercises",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Difficulty",
                table: "Exercises",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AudioUrl",
                table: "Exercises",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SuggestedDuration",
                table: "Exercises",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExerciseResponses",
                columns: table => new
                {
                    ExerciseResponseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttemptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    UserResponse = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseResponses", x => x.ExerciseResponseId);
                    table.ForeignKey(
                        name: "FK_ExerciseResponses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseResponses_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeachingPlans",
                columns: table => new
                {
                    TeachingPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedTotalDuration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingPlans", x => x.TeachingPlanId);
                });

            migrationBuilder.CreateTable(
                name: "ExercisePlans",
                columns: table => new
                {
                    ExercisePlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    TeachingPlanId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExercisePlans", x => x.ExercisePlanId);
                    table.ForeignKey(
                        name: "FK_ExercisePlans_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExercisePlans_TeachingPlans_TeachingPlanId",
                        column: x => x.TeachingPlanId,
                        principalTable: "TeachingPlans",
                        principalColumn: "TeachingPlanId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeachingPlanEnrollments",
                columns: table => new
                {
                    TeachingPlanEnrollmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeachingPlanId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingPlanEnrollments", x => x.TeachingPlanEnrollmentId);
                    table.ForeignKey(
                        name: "FK_TeachingPlanEnrollments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeachingPlanEnrollments_TeachingPlans_TeachingPlanId",
                        column: x => x.TeachingPlanId,
                        principalTable: "TeachingPlans",
                        principalColumn: "TeachingPlanId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "1a63ee96-89d7-45e3-90ba-292da0d62711", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEHjj1RsaS5AjSOAKNsbrGZhepJDaK1xNMFywibDOP2xvSnRV2D7TJtybCSK+7KkJEA==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });

            migrationBuilder.CreateIndex(
                name: "IX_ExercisePlans_ExerciseId",
                table: "ExercisePlans",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExercisePlans_TeachingPlanId",
                table: "ExercisePlans",
                column: "TeachingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseResponses_ExerciseId",
                table: "ExerciseResponses",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseResponses_UserId",
                table: "ExerciseResponses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingPlanEnrollments_TeachingPlanId",
                table: "TeachingPlanEnrollments",
                column: "TeachingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingPlanEnrollments_UserId",
                table: "TeachingPlanEnrollments",
                column: "UserId");
        }
    }
}
