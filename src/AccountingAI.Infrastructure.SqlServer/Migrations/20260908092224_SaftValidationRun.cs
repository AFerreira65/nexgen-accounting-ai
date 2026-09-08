using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class SaftValidationRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaftValidationRun",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    AuditFileVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SchemaAvailable = table.Column<bool>(type: "bit", nullable: false),
                    SchemaValid = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    InformationCount = table.Column<int>(type: "int", nullable: false),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaftValidationRun", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaftValidationRun_CompanyId_CreatedAtUtc",
                schema: "accounting",
                table: "SaftValidationRun",
                columns: new[] { "CompanyId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaftValidationRun",
                schema: "accounting");
        }
    }
}
