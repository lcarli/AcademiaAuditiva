using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class First : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    ExerciseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedDuration = table.Column<int>(type: "int", nullable: false),
                    AudioUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.ExerciseId);
                });

            migrationBuilder.CreateTable(
                name: "TeachingPlans",
                columns: table => new
                {
                    TeachingPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedTotalDuration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingPlans", x => x.TeachingPlanId);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseResponses",
                columns: table => new
                {
                    ExerciseResponseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttemptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false)
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
                name: "ExercisePlans",
                columns: table => new
                {
                    ExercisePlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeachingPlanId = table.Column<int>(type: "int", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
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
                values: new object[] { "7f42424d-b5d9-48f9-97fb-e26b84811b0f", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCASDECARLI", "AQAAAAIAAYagAAAAECGXDRcs3Gi63++jGsKyxI9EPT2EpQcpqVOyXW3DvTTAxq9EHQoxA31Xg2WmPP6ctw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "LucasdeCarli" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExercisePlans");

            migrationBuilder.DropTable(
                name: "ExerciseResponses");

            migrationBuilder.DropTable(
                name: "TeachingPlanEnrollments");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "TeachingPlans");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "7f42424d-b5d9-48f9-97fb-e26b84811b0f");
        }
    }
}
