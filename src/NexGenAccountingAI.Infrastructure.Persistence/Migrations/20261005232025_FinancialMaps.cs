using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialMap",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "case_insensitive"),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialMap", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMap_CompanyId_Name",
                schema: "reporting",
                table: "FinancialMap",
                columns: new[] { "CompanyId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialMap",
                schema: "reporting");
        }
    }
}
