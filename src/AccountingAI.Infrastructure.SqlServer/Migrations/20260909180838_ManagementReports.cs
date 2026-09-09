using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ManagementReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyAiSettings",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    AutoMonthly = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyAiSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagementReport",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DossierJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagementReport", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAiSettings_CompanyId",
                schema: "reporting",
                table: "CompanyAiSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagementReport_CompanyId_PeriodFrom_PeriodTo",
                schema: "reporting",
                table: "ManagementReport",
                columns: new[] { "CompanyId", "PeriodFrom", "PeriodTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyAiSettings",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "ManagementReport",
                schema: "reporting");
        }
    }
}
