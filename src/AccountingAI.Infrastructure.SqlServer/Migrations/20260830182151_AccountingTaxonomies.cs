using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AccountingTaxonomies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxonomyCode",
                schema: "accounting",
                table: "Accounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxonomyReference",
                schema: "accounting",
                table: "Accounts",
                type: "nchar(1)",
                fixedLength: true,
                maxLength: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxonomyCode",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TaxonomyReference",
                schema: "accounting",
                table: "Accounts");
        }
    }
}
