using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientPortalUploads",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientPortalUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientPortalUploads_ClientPortalAccounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "organization",
                        principalTable: "ClientPortalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientPortalUploads_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientPortalUploads_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalSchema: "accounting",
                        principalTable: "DocumentAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalUploads_AccountId",
                schema: "organization",
                table: "ClientPortalUploads",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalUploads_AttachmentId",
                schema: "organization",
                table: "ClientPortalUploads",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalUploads_CompanyId_AccountId_UploadedAtUtc",
                schema: "organization",
                table: "ClientPortalUploads",
                columns: new[] { "CompanyId", "AccountId", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientPortalUploads",
                schema: "organization");
        }
    }
}
