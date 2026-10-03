using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JournalEntryLineVatCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "VatCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_VatCodes_VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "VatCodeId",
                principalSchema: "accounting",
                principalTable: "VatCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_VatCodes_VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "VatCodeId",
                schema: "accounting",
                table: "JournalEntryLines");
        }
    }
}
