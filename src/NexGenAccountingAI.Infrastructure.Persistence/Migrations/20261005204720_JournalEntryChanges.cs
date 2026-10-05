using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalEntryChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalEntryChanges",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryChanges_CompanyId_ChangedAtUtc",
                schema: "accounting",
                table: "JournalEntryChanges",
                columns: new[] { "CompanyId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryChanges_CompanyId_Reference_ChangedAtUtc",
                schema: "accounting",
                table: "JournalEntryChanges",
                columns: new[] { "CompanyId", "Reference", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalEntryChanges",
                schema: "accounting");
        }
    }
}
