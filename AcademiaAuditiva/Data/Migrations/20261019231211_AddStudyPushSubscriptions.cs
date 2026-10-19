using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudyPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EndpointHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MinuteOfDay = table.Column<int>(type: "int", nullable: false),
                    LastReminderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPushSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPushSubscriptions_EndpointHash",
                table: "StudyPushSubscriptions",
                column: "EndpointHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyPushSubscriptions_UserId",
                table: "StudyPushSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudyPushSubscriptions");
        }
    }
}
