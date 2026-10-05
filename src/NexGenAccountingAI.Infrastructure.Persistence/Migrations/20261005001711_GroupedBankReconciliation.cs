using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GroupedBankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryReferences = table.Column<List<string>>(type: "text[]", nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_AccountId",
                schema: "accounting",
                table: "BankReconciliations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_CompanyId_AccountId",
                schema: "accounting",
                table: "BankReconciliations",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatementLines_BankReconciliations_ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines",
                column: "ReconciliationId",
                principalSchema: "accounting",
                principalTable: "BankReconciliations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Cada movimento já conciliado passa a ter a sua conciliação, com o mesmo Id, antes de sair a coluna antiga.
            migrationBuilder.Sql("""
                INSERT INTO accounting."BankReconciliations" ("Id", "CompanyId", "AccountId", "EntryReferences", "Automatic", "CreatedAtUtc")
                SELECT "Id", "CompanyId", "AccountId", ARRAY["ReconciledEntryReference"], "ReconciledAutomatically", COALESCE("ReconciledAtUtc", now())
                FROM accounting."BankStatementLines"
                WHERE "ReconciledEntryReference" IS NOT NULL;
                UPDATE accounting."BankStatementLines" SET "ReconciliationId" = "Id" WHERE "ReconciledEntryReference" IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_BankStatementLines_CompanyId_ReconciledEntryReference",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReconciledAtUtc",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReconciledAutomatically",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReconciledEntryReference",
                schema: "accounting",
                table: "BankStatementLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankStatementLines_BankReconciliations_ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "BankReconciliations",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementLines_ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciledAtUtc",
                schema: "accounting",
                table: "BankStatementLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReconciledAutomatically",
                schema: "accounting",
                table: "BankStatementLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReconciledEntryReference",
                schema: "accounting",
                table: "BankStatementLines",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_CompanyId_ReconciledEntryReference",
                schema: "accounting",
                table: "BankStatementLines",
                columns: new[] { "CompanyId", "ReconciledEntryReference" });
        }
    }
}
