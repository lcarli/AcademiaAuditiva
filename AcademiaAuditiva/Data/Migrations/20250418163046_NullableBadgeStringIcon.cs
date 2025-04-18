using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class NullableBadgeStringIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "4fc7f651-f679-443a-9a6f-ed0ebf743d62");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "Badges",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Discriminator", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "789ea4a4-dd57-45fe-9ee3-4363e9d6632c", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "ApplicationUser", "lucas.decarli.ca@gmail.com", true, "Lucas", "De Carli", true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEBc4R8ouLUbWE6zLZ7nX0Wi26BI1YwBREwap3/6QX3lXm8MHoA7UODbZANd6vnoQaw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "789ea4a4-dd57-45fe-9ee3-4363e9d6632c");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "Badges",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Discriminator", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "4fc7f651-f679-443a-9a6f-ed0ebf743d62", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "ApplicationUser", "lucas.decarli.ca@gmail.com", true, "Lucas", "De Carli", true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAENmw+JZydY6DWsRY2EqX4Tv3c1A73IE72z/a+AZkEgJZ/Ub1YuHFltQArRWrcBeapw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }
    }
}
