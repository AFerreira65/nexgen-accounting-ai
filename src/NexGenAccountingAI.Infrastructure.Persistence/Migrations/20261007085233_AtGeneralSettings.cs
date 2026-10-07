using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtGeneralSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtGeneralSettings",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<int>(type: "integer", nullable: false),
                    CertificateDirectory = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    CertificateFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    PublicKeyDirectory = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    PublicKeyFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    EncryptedCertificatePassword = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtGeneralSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtGeneralSettings_Key",
                schema: "organization",
                table: "AtGeneralSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtGeneralSettings",
                schema: "organization");
        }
    }
}
