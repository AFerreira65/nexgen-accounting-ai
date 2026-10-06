using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "reporting",
                table: "SavedListing",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "reporting",
                table: "SavedListing",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "reporting",
                table: "SavedListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "reporting",
                table: "SavedListing",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FirmTasks",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmTasks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmTasks_AssigneeUserId_Status",
                schema: "accounting",
                table: "FirmTasks",
                columns: new[] { "AssigneeUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FirmTasks_CompanyId_Origin",
                schema: "accounting",
                table: "FirmTasks",
                columns: new[] { "CompanyId", "Origin" },
                unique: true,
                filter: "\"Origin\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FirmTasks_CompanyId_Status",
                schema: "accounting",
                table: "FirmTasks",
                columns: new[] { "CompanyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirmTasks",
                schema: "accounting");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "reporting",
                table: "SavedListing");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "reporting",
                table: "SavedListing");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "reporting",
                table: "SavedListing");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "reporting",
                table: "SavedListing");
        }
    }
}
