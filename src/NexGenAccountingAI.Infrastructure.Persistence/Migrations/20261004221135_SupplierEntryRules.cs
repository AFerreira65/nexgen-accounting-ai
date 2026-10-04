using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierEntryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JournalEntryReference",
                schema: "accounting",
                table: "EFaturaPurchase",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierEntryRules",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntryTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierEntryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierEntryRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierEntryRules_EntryTemplates_EntryTemplateId",
                        column: x => x.EntryTemplateId,
                        principalSchema: "accounting",
                        principalTable: "EntryTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierEntryRules_CompanyId_SupplierTaxId",
                schema: "accounting",
                table: "SupplierEntryRules",
                columns: new[] { "CompanyId", "SupplierTaxId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierEntryRules_EntryTemplateId",
                schema: "accounting",
                table: "SupplierEntryRules",
                column: "EntryTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierEntryRules",
                schema: "accounting");

            migrationBuilder.DropColumn(
                name: "JournalEntryReference",
                schema: "accounting",
                table: "EFaturaPurchase");
        }
    }
}
