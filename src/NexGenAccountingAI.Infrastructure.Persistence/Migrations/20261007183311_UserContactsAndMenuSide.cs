using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserContactsAndMenuSide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MenuOnRight",
                schema: "identity",
                table: "UserCompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Extension",
                schema: "identity",
                table: "UserAccounts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                schema: "identity",
                table: "UserAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "identity",
                table: "UserAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MenuOnRight",
                schema: "identity",
                table: "UserCompanySettings");

            migrationBuilder.DropColumn(
                name: "Extension",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Mobile",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "identity",
                table: "UserAccounts");
        }
    }
}
