using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AccountingStandardsAndProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceTemplateVersion",
                schema: "accounting",
                table: "Journals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AccountingSetupCompletedAtUtc",
                schema: "organization",
                table: "Companies",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingSetupVersion",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccountingStandard",
                schema: "organization",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AllowsPosting",
                schema: "accounting",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentCode",
                schema: "accounting",
                table: "Accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTemplateVersion",
                schema: "accounting",
                table: "Accounts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountingDocumentTypes",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SourceTemplateVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingDocumentTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingDocumentTypes_Journals_CompanyId_DefaultJournalId",
                        columns: x => new { x.CompanyId, x.DefaultJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingDocumentTypes_CompanyId_Code",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingDocumentTypes_CompanyId_DefaultJournalId",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                columns: new[] { "CompanyId", "DefaultJournalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingDocumentTypes",
                schema: "accounting");

            migrationBuilder.DropColumn(
                name: "SourceTemplateVersion",
                schema: "accounting",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "AccountingSetupCompletedAtUtc",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AccountingSetupVersion",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AccountingStandard",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AllowsPosting",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ParentCode",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SourceTemplateVersion",
                schema: "accounting",
                table: "Accounts");
        }
    }
}
