using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentAttachments",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    JournalEntryReference = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    QrText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QrIssuerTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    QrDocumentNo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    QrDocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    QrGrossTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    QrTaxTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAttachments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAttachmentContents",
                schema: "accounting",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachmentContents", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_DocumentAttachmentContents_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalSchema: "accounting",
                        principalTable: "DocumentAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_CompanyId_JournalEntryReference",
                schema: "accounting",
                table: "DocumentAttachments",
                columns: new[] { "CompanyId", "JournalEntryReference" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_CompanyId_Sha256",
                schema: "accounting",
                table: "DocumentAttachments",
                columns: new[] { "CompanyId", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAttachmentContents",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "DocumentAttachments",
                schema: "accounting");
        }
    }
}
