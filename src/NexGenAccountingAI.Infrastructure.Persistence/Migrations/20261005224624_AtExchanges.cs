using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtExchanges",
                schema: "infrastructure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Production = table.Column<bool>(type: "boolean", nullable: false),
                    Period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Reached = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ResultCode = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DeclarationId = table.Column<long>(type: "bigint", nullable: true),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    AlertCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtExchanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtExchanges_CompanyId_RequestedAtUtc",
                schema: "infrastructure",
                table: "AtExchanges",
                columns: new[] { "CompanyId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtExchanges",
                schema: "infrastructure");
        }
    }
}
