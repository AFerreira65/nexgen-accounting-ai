using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EFaturaPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFaturaPurchase",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Atcud = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Sector = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TaxBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DocumentKey = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFaturaPurchase", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EFaturaPurchase_CompanyId_DocumentKey",
                schema: "accounting",
                table: "EFaturaPurchase",
                columns: new[] { "CompanyId", "DocumentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFaturaPurchase_CompanyId_InvoiceDate",
                schema: "accounting",
                table: "EFaturaPurchase",
                columns: new[] { "CompanyId", "InvoiceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFaturaPurchase",
                schema: "accounting");
        }
    }
}
