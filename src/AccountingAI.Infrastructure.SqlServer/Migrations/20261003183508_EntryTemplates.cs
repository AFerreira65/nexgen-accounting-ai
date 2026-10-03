using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class EntryTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntryTemplates",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryTemplates_AccountingDocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "accounting",
                        principalTable: "AccountingDocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryTemplates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryTemplates_Journals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntryTemplateLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VatCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryTemplateLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntryTemplateLines_EntryTemplates_EntryTemplateId",
                        column: x => x.EntryTemplateId,
                        principalSchema: "accounting",
                        principalTable: "EntryTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntryTemplateLines_VatCodes_VatCodeId",
                        column: x => x.VatCodeId,
                        principalSchema: "accounting",
                        principalTable: "VatCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplateLines_AccountId",
                schema: "accounting",
                table: "EntryTemplateLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplateLines_EntryTemplateId_Sequence",
                schema: "accounting",
                table: "EntryTemplateLines",
                columns: new[] { "EntryTemplateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplateLines_VatCodeId",
                schema: "accounting",
                table: "EntryTemplateLines",
                column: "VatCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplates_CompanyId_Code",
                schema: "accounting",
                table: "EntryTemplates",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplates_DocumentTypeId",
                schema: "accounting",
                table: "EntryTemplates",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryTemplates_JournalId",
                schema: "accounting",
                table: "EntryTemplates",
                column: "JournalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryTemplateLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "EntryTemplates",
                schema: "accounting");
        }
    }
}
