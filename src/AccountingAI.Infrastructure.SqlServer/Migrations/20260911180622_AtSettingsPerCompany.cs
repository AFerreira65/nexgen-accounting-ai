using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AtSettingsPerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Environment",
                schema: "organization",
                table: "CompanyAtCredentials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Periodicity",
                schema: "organization",
                table: "CompanyAtCredentials",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "organization",
                table: "CompanyAtCredentials");

            migrationBuilder.DropColumn(
                name: "Periodicity",
                schema: "organization",
                table: "CompanyAtCredentials");
        }
    }
}
