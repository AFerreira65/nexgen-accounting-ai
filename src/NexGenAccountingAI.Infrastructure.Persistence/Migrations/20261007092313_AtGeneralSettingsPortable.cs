using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtGeneralSettingsPortable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateDirectory",
                schema: "organization",
                table: "AtGeneralSettings");

            migrationBuilder.DropColumn(
                name: "PublicKeyDirectory",
                schema: "organization",
                table: "AtGeneralSettings");

            migrationBuilder.AddColumn<bool>(
                name: "IsProducer",
                schema: "identity",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "CertificateData",
                schema: "organization",
                table: "AtGeneralSettings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PublicKeyData",
                schema: "organization",
                table: "AtGeneralSettings",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProducer",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "CertificateData",
                schema: "organization",
                table: "AtGeneralSettings");

            migrationBuilder.DropColumn(
                name: "PublicKeyData",
                schema: "organization",
                table: "AtGeneralSettings");

            migrationBuilder.AddColumn<string>(
                name: "CertificateDirectory",
                schema: "organization",
                table: "AtGeneralSettings",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicKeyDirectory",
                schema: "organization",
                table: "AtGeneralSettings",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);
        }
    }
}
