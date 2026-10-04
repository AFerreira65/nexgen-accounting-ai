using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EFaturaDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DismissedAtUtc",
                schema: "accounting",
                table: "EFaturaPurchase",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DismissedReason",
                schema: "accounting",
                table: "EFaturaPurchase",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DismissedAtUtc",
                schema: "accounting",
                table: "EFaturaPurchase");

            migrationBuilder.DropColumn(
                name: "DismissedReason",
                schema: "accounting",
                table: "EFaturaPurchase");
        }
    }
}
