using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcademiaAuditiva.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlServerDistributedCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[AppCache]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AppCache](
        [Id] [nvarchar](449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
        [Value] [varbinary](max) NOT NULL,
        [ExpiresAtTime] [datetimeoffset](7) NOT NULL,
        [SlidingExpirationInSeconds] [bigint] NULL,
        [AbsoluteExpiration] [datetimeoffset](7) NULL,
        CONSTRAINT [pk_AppCache_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [Index_ExpiresAtTime]
        ON [dbo].[AppCache]([ExpiresAtTime] ASC);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[AppCache]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[AppCache];
END
");
        }
    }
}
