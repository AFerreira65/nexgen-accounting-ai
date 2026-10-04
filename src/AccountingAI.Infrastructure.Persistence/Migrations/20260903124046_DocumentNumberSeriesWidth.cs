using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentNumberSeriesWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Width",
                schema: "accounting",
                table: "DocumentNumberSeries",
                type: "int",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Width",
                schema: "accounting",
                table: "DocumentNumberSeries");
        }
    }
}
