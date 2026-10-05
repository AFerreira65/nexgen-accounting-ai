using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetTaxData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DisposalPrice",
                schema: "accounting",
                table: "FixedAssets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonetaryCoefficient",
                schema: "accounting",
                table: "FixedAssets",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "accounting",
                table: "FixedAssets",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxTableCode",
                schema: "accounting",
                table: "FixedAssets",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisposalPrice",
                schema: "accounting",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "MonetaryCoefficient",
                schema: "accounting",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "accounting",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "TaxTableCode",
                schema: "accounting",
                table: "FixedAssets");
        }
    }
}
