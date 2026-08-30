using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JournalEntryDocumentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AccountingDocumentTypes_CompanyId_Id",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "DocumentTypeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_AccountingDocumentTypes_CompanyId_DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "DocumentTypeId" },
                principalSchema: "accounting",
                principalTable: "AccountingDocumentTypes",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_AccountingDocumentTypes_CompanyId_DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_CompanyId_DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AccountingDocumentTypes_CompanyId_Id",
                schema: "accounting",
                table: "AccountingDocumentTypes");

            migrationBuilder.DropColumn(
                name: "DocumentTypeId",
                schema: "accounting",
                table: "JournalEntries");
        }
    }
}
