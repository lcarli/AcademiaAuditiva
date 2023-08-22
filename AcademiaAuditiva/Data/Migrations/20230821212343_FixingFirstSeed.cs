using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixingFirstSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "7f42424d-b5d9-48f9-97fb-e26b84811b0f");

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "1a63ee96-89d7-45e3-90ba-292da0d62711", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCAS.DECARLI.CA@GMAIL.COM", "AQAAAAIAAYagAAAAEHjj1RsaS5AjSOAKNsbrGZhepJDaK1xNMFywibDOP2xvSnRV2D7TJtybCSK+7KkJEA==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "lucas.decarli.ca@gmail.com" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "1a63ee96-89d7-45e3-90ba-292da0d62711");

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "7f42424d-b5d9-48f9-97fb-e26b84811b0f", 0, "37f979fc-85e9-42c0-bc5c-3321d0b9cad6", "lucas.decarli.ca@gmail.com", true, true, null, "LUCAS.DECARLI.CA@GMAIL.COM", "LUCASDECARLI", "AQAAAAIAAYagAAAAECGXDRcs3Gi63++jGsKyxI9EPT2EpQcpqVOyXW3DvTTAxq9EHQoxA31Xg2WmPP6ctw==", "+15817456586", true, "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO", false, "LucasdeCarli" });
        }
    }
}
