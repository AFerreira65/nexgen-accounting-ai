using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiCostControlAndPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnonymizeNames",
                schema: "reporting",
                table: "CompanyAiSettings",
                type: "boolean",
                nullable: false,
                // As empresas que já existem passam a esconder os nomes (o seguro por omissão).
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ConversationRetentionDays",
                schema: "reporting",
                table: "CompanyAiSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatModel",
                schema: "organization",
                table: "AiGeneralSettings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMonthlyAllowanceUsd",
                schema: "organization",
                table: "AiGeneralSettings",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricesJson",
                schema: "organization",
                table: "AiGeneralSettings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickModel",
                schema: "organization",
                table: "AiGeneralSettings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportModel",
                schema: "organization",
                table: "AiGeneralSettings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiCreditTopUps",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditTopUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCreditTopUps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Feature = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsageRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyAiBudgets",
                schema: "reporting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyAllowanceUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    ExtraCreditUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyAiBudgets", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyAiBudgets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditTopUps_CompanyId_CreatedAtUtc",
                schema: "reporting",
                table: "AiCreditTopUps",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CompanyId_CreatedAtUtc",
                schema: "reporting",
                table: "AiUsageRecords",
                columns: new[] { "CompanyId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCreditTopUps",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "AiUsageRecords",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "CompanyAiBudgets",
                schema: "reporting");

            migrationBuilder.DropColumn(
                name: "AnonymizeNames",
                schema: "reporting",
                table: "CompanyAiSettings");

            migrationBuilder.DropColumn(
                name: "ConversationRetentionDays",
                schema: "reporting",
                table: "CompanyAiSettings");

            migrationBuilder.DropColumn(
                name: "ChatModel",
                schema: "organization",
                table: "AiGeneralSettings");

            migrationBuilder.DropColumn(
                name: "DefaultMonthlyAllowanceUsd",
                schema: "organization",
                table: "AiGeneralSettings");

            migrationBuilder.DropColumn(
                name: "PricesJson",
                schema: "organization",
                table: "AiGeneralSettings");

            migrationBuilder.DropColumn(
                name: "QuickModel",
                schema: "organization",
                table: "AiGeneralSettings");

            migrationBuilder.DropColumn(
                name: "ReportModel",
                schema: "organization",
                table: "AiGeneralSettings");
        }
    }
}
