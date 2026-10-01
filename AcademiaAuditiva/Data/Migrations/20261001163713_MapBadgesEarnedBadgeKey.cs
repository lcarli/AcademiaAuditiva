using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapBadgesEarnedBadgeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BadgesEarned_Badges_BadgeKey1",
                table: "BadgesEarned");

            migrationBuilder.DropIndex(
                name: "IX_BadgesEarned_BadgeKey1",
                table: "BadgesEarned");

            migrationBuilder.DropIndex(
                name: "IX_BadgesEarned_UserId",
                table: "BadgesEarned");

            migrationBuilder.DropColumn(
                name: "BadgeKey1",
                table: "BadgesEarned");

            // Nothing writes BadgesEarned yet. Rows the new key would reject (an unknown
            // badge, or a badge earned twice by the same user) are dropped rather than
            // failing the deploy; the first time a badge was earned is kept.
            migrationBuilder.Sql(@"
DELETE e FROM [BadgesEarned] AS e
WHERE NOT EXISTS (SELECT 1 FROM [Badges] AS b WHERE b.[BadgeKey] = e.[BadgeKey]);

WITH [Repeats] AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY [UserId], [BadgeKey] ORDER BY [EarnedDate], [Id]) AS [N]
    FROM [BadgesEarned]
)
DELETE FROM [Repeats] WHERE [N] > 1;");

            migrationBuilder.AlterColumn<string>(
                name: "BadgeKey",
                table: "BadgesEarned",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_BadgeKey",
                table: "BadgesEarned",
                column: "BadgeKey");

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_UserId_BadgeKey",
                table: "BadgesEarned",
                columns: new[] { "UserId", "BadgeKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BadgesEarned_Badges_BadgeKey",
                table: "BadgesEarned",
                column: "BadgeKey",
                principalTable: "Badges",
                principalColumn: "BadgeKey",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BadgesEarned_Badges_BadgeKey",
                table: "BadgesEarned");

            migrationBuilder.DropIndex(
                name: "IX_BadgesEarned_BadgeKey",
                table: "BadgesEarned");

            migrationBuilder.DropIndex(
                name: "IX_BadgesEarned_UserId_BadgeKey",
                table: "BadgesEarned");

            migrationBuilder.AlterColumn<string>(
                name: "BadgeKey",
                table: "BadgesEarned",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "BadgeKey1",
                table: "BadgesEarned",
                type: "nvarchar(50)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_BadgeKey1",
                table: "BadgesEarned",
                column: "BadgeKey1");

            migrationBuilder.CreateIndex(
                name: "IX_BadgesEarned_UserId",
                table: "BadgesEarned",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BadgesEarned_Badges_BadgeKey1",
                table: "BadgesEarned",
                column: "BadgeKey1",
                principalTable: "Badges",
                principalColumn: "BadgeKey");
        }
    }
}
