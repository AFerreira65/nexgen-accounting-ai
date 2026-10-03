using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JournalEntryLineParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartyId",
                schema: "accounting",
                table: "JournalEntryLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_PartyId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Parties_PartyId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "PartyId",
                principalSchema: "accounting",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Parties_PartyId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_PartyId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "PartyId",
                schema: "accounting",
                table: "JournalEntryLines");
        }
    }
}
