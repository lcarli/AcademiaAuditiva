using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeachingDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Classrooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classrooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Classrooms_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Routines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routines_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassroomMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassroomId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassroomMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassroomMembers_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassroomMembers_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassroomInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassroomId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassroomInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassroomInvites_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoutineId = table.Column<int>(type: "int", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    FilterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetCount = table.Column<int>(type: "int", nullable: false),
                    MinScore = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineItems_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineItems_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "ExerciseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoutineAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoutineId = table.Column<int>(type: "int", nullable: false),
                    ClassroomId = table.Column<int>(type: "int", nullable: true),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineAssignments_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoutineAssignments_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoutineAssignments_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoutineAssignmentOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoutineAssignmentId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoutineItemId = table.Column<int>(type: "int", nullable: false),
                    OverrideFilterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverrideTargetCount = table.Column<int>(type: "int", nullable: true),
                    ExcludeItem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineAssignmentOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineAssignmentOverrides_RoutineAssignments_RoutineAssignmentId",
                        column: x => x.RoutineAssignmentId,
                        principalTable: "RoutineAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineAssignmentOverrides_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoutineAssignmentOverrides_RoutineItems_RoutineItemId",
                        column: x => x.RoutineItemId,
                        principalTable: "RoutineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Classrooms_OwnerId",
                table: "Classrooms",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomMembers_ClassroomId_StudentId",
                table: "ClassroomMembers",
                columns: new[] { "ClassroomId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomMembers_StudentId",
                table: "ClassroomMembers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomInvites_Token",
                table: "ClassroomInvites",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomInvites_ClassroomId_Email",
                table: "ClassroomInvites",
                columns: new[] { "ClassroomId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Routines_OwnerId",
                table: "Routines",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineItems_RoutineId_Order",
                table: "RoutineItems",
                columns: new[] { "RoutineId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutineItems_ExerciseId",
                table: "RoutineItems",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignments_RoutineId",
                table: "RoutineAssignments",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignments_ClassroomId",
                table: "RoutineAssignments",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignments_StudentId",
                table: "RoutineAssignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignmentOverrides_RoutineAssignmentId_StudentId_RoutineItemId",
                table: "RoutineAssignmentOverrides",
                columns: new[] { "RoutineAssignmentId", "StudentId", "RoutineItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignmentOverrides_StudentId",
                table: "RoutineAssignmentOverrides",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineAssignmentOverrides_RoutineItemId",
                table: "RoutineAssignmentOverrides",
                column: "RoutineItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RoutineAssignmentOverrides");
            migrationBuilder.DropTable(name: "RoutineAssignments");
            migrationBuilder.DropTable(name: "RoutineItems");
            migrationBuilder.DropTable(name: "Routines");
            migrationBuilder.DropTable(name: "ClassroomInvites");
            migrationBuilder.DropTable(name: "ClassroomMembers");
            migrationBuilder.DropTable(name: "Classrooms");
        }
    }
}
