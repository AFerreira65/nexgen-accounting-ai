using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesDocuments",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Atcud = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    InvoiceType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerTaxId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    NetTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    JournalEntryReference = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    Taxes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesEntryRules",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EntryTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesEntryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesEntryRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesEntryRules_EntryTemplates_EntryTemplateId",
                        column: x => x.EntryTemplateId,
                        principalSchema: "accounting",
                        principalTable: "EntryTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDocuments_CompanyId_InvoiceDate",
                schema: "accounting",
                table: "SalesDocuments",
                columns: new[] { "CompanyId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDocuments_CompanyId_InvoiceNo",
                schema: "accounting",
                table: "SalesDocuments",
                columns: new[] { "CompanyId", "InvoiceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesEntryRules_CompanyId_TaxPercentage",
                schema: "accounting",
                table: "SalesEntryRules",
                columns: new[] { "CompanyId", "TaxPercentage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesEntryRules_EntryTemplateId",
                schema: "accounting",
                table: "SalesEntryRules",
                column: "EntryTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesDocuments",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "SalesEntryRules",
                schema: "accounting");
        }
    }
}
