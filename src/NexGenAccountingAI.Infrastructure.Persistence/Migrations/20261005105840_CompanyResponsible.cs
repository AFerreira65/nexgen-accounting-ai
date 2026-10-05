using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyResponsible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsResponsible",
                schema: "identity",
                table: "CompanyUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsResponsible",
                schema: "identity",
                table: "CompanyUsers");
        }
    }
}
