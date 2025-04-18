using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileAndBadgesSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "5217d5e9-d774-4adb-8b3f-c8f7355de0a7");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Exercises",
                newName: "ExerciseTypeId");

            migrationBuilder.RenameColumn(
                name: "Difficulty",
                table: "Exercises",
                newName: "ExerciseCategoryId");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "Exercises",
                newName: "DifficultyLevelId");

            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "AspNetUsers",
                type: "nvarchar(21)",
                maxLength: 21,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Badges",
                columns: table => new
                {
                    BadgeKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badges", x => x.BadgeKey);
                });

            migrationBuilder.CreateTable(
                name: "DifficultyLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DifficultyLevels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Gateway = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BadgesEarned",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BadgeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EarnedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsNew = table.Column<bool>(type: "bit", nullable: false),
                    BadgeKey1 = table.Column<string>(type: "nvarchar(50)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgesEarned", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgesEarned_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BadgesEarned_Badges_BadgeKey1",
                        column: x => x.BadgeKey1,
                        principalTable: "Badges",
                        principalColumn: "BadgeKey");
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Discriminator", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "4fc7f651-f679-443a-9a6f-ed0ebf743d62", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "ApplicationUser", "lucas.decarli.ca@gmail.com", true, "Lucas", "De Carli", true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAENmw+JZydY6DWsRY2EqX4Tv3c1A73IE72z/a+AZkEgJZ/Ub1YuHFltQArRWrcBeapw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_DifficultyLevelId",
                table: "Exercises",
                column: "DifficultyLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_ExerciseCategoryId",
                table: "Exercises",
                column: "ExerciseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_ExerciseTypeId",
                table: "Exercises",
                column: "ExerciseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_BadgeKey1",
                table: "BadgesEarned",
                column: "BadgeKey1");

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_UserId",
                table: "BadgesEarned",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_DifficultyLevels_DifficultyLevelId",
                table: "Exercises",
                column: "DifficultyLevelId",
                principalTable: "DifficultyLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_ExerciseCategories_ExerciseCategoryId",
                table: "Exercises",
                column: "ExerciseCategoryId",
                principalTable: "ExerciseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_ExerciseTypes_ExerciseTypeId",
                table: "Exercises",
                column: "ExerciseTypeId",
                principalTable: "ExerciseTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_DifficultyLevels_DifficultyLevelId",
                table: "Exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_ExerciseCategories_ExerciseCategoryId",
                table: "Exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_ExerciseTypes_ExerciseTypeId",
                table: "Exercises");

            migrationBuilder.DropTable(
                name: "BadgesEarned");

            migrationBuilder.DropTable(
                name: "DifficultyLevels");

            migrationBuilder.DropTable(
                name: "ExerciseCategories");

            migrationBuilder.DropTable(
                name: "ExerciseTypes");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Badges");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_DifficultyLevelId",
                table: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_ExerciseCategoryId",
                table: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_ExerciseTypeId",
                table: "Exercises");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "4fc7f651-f679-443a-9a6f-ed0ebf743d62");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "ExerciseTypeId",
                table: "Exercises",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "ExerciseCategoryId",
                table: "Exercises",
                newName: "Difficulty");

            migrationBuilder.RenameColumn(
                name: "DifficultyLevelId",
                table: "Exercises",
                newName: "Category");

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "5217d5e9-d774-4adb-8b3f-c8f7355de0a7", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEDJ3LGyHgnwNJhhAAHe1MbDvKSvIU8FvAhkeXw/Oc61+U41cwFTBKZvQYLJLxr+ffg==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }
    }
}
