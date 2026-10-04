using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AccountingDbContext))]
[Migration("202608290002_LocalOrganization")]
public partial class LocalOrganization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "organization");

        migrationBuilder.CreateTable(
            name: "AccountingFirms",
            schema: "organization",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountingFirms", item => item.Id);
            });

        migrationBuilder.CreateTable(
            name: "Companies",
            schema: "organization",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountingFirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TaxRegistrationNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TaxRegion = table.Column<int>(type: "int", nullable: false),
                BaseCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Companies", item => item.Id);
                table.ForeignKey(
                    name: "FK_Companies_AccountingFirms_AccountingFirmId",
                    column: item => item.AccountingFirmId,
                    principalSchema: "organization",
                    principalTable: "AccountingFirms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountingFirms_Slug",
            schema: "organization",
            table: "AccountingFirms",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Companies_AccountingFirmId_TaxRegistrationNumber",
            schema: "organization",
            table: "Companies",
            columns: ["AccountingFirmId", "TaxRegistrationNumber"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Companies", schema: "organization");
        migrationBuilder.DropTable(name: "AccountingFirms", schema: "organization");
    }
}
