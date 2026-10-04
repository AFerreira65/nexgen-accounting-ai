using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyAndPartyAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressDetail",
                schema: "accounting",
                table: "Parties",
                type: "nvarchar(210)",
                maxLength: 210,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "accounting",
                table: "Parties",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "accounting",
                table: "Parties",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressDetail",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(210)",
                maxLength: 210,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressDetail",
                schema: "accounting",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "accounting",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                schema: "accounting",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "AddressDetail",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                schema: "organization",
                table: "Companies");
        }
    }
}
