using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingLedgerTestData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.UniqueConstraint("AK_Accounts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Accounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Journals",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journals", x => x.Id);
                    table.UniqueConstraint("AK_Journals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Journals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DocumentReference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.UniqueConstraint("AK_JournalEntries_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_JournalEntries_Journals_CompanyId_JournalId",
                        columns: x => new { x.CompanyId, x.JournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_CompanyId_JournalEntryId",
                        columns: x => new { x.CompanyId, x.JournalEntryId },
                        principalSchema: "accounting",
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_Code",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_EntryDate",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_JournalId",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "JournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_Reference",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CompanyId_AccountId",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CompanyId_JournalEntryId_Sequence",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "CompanyId", "JournalEntryId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Journals_CompanyId_Code",
                schema: "accounting",
                table: "Journals",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalEntryLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "JournalEntries",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "Journals",
                schema: "accounting");
        }
    }
}
