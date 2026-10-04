using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AccountingDbContext))]
[Migration("202608300001_MultiCompanyUserSettings")]
public partial class MultiCompanyUserSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "identity");

        migrationBuilder.CreateTable(
            name: "UserAccounts",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_UserAccounts", item => item.Id));

        migrationBuilder.CreateTable(
            name: "CompanyUsers",
            schema: "identity",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyUsers", item => new { item.CompanyId, item.UserId });
                table.ForeignKey(
                    name: "FK_CompanyUsers_Companies_CompanyId",
                    column: item => item.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyUsers_UserAccounts_UserId",
                    column: item => item.UserId,
                    principalSchema: "identity",
                    principalTable: "UserAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserCompanySettings",
            schema: "identity",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Culture = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DefaultFiscalYear = table.Column<int>(type: "int", nullable: true),
                Theme = table.Column<int>(type: "int", nullable: false),
                CompactMode = table.Column<bool>(type: "bit", nullable: false),
                PageSize = table.Column<int>(type: "int", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserCompanySettings", item => new { item.CompanyId, item.UserId });
                table.ForeignKey(
                    name: "FK_UserCompanySettings_CompanyUsers_CompanyId_UserId",
                    columns: item => new { item.CompanyId, item.UserId },
                    principalSchema: "identity",
                    principalTable: "CompanyUsers",
                    principalColumns: new[] { "CompanyId", "UserId" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CompanyUsers_UserId",
            schema: "identity",
            table: "CompanyUsers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserAccounts_NormalizedEmail",
            schema: "identity",
            table: "UserAccounts",
            column: "NormalizedEmail",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserCompanySettings", schema: "identity");
        migrationBuilder.DropTable(name: "CompanyUsers", schema: "identity");
        migrationBuilder.DropTable(name: "UserAccounts", schema: "identity");
    }
}
