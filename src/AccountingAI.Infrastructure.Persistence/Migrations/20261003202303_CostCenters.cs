using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostCenters",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCenters_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_CompanyId_Code",
                schema: "accounting",
                table: "CostCenters",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_CostCenters_CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines",
                column: "CostCenterId",
                principalSchema: "accounting",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_CostCenters_CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "CostCenters",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines");
        }
    }
}
