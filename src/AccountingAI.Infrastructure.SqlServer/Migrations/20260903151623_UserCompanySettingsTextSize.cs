using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class UserCompanySettingsTextSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TextSize",
                schema: "identity",
                table: "UserCompanySettings",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TextSize",
                schema: "identity",
                table: "UserCompanySettings");
        }
    }
}
