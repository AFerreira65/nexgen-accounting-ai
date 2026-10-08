using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexGenAccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAccountant",
                schema: "identity",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Quem já usava a aplicação continua a poder tudo, incluindo as senhas da AT (ADR-0018);
            // os utilizadores novos entram sem a qualidade de contabilista.
            migrationBuilder.Sql("UPDATE identity.\"UserAccounts\" SET \"IsAccountant\" = TRUE WHERE NOT \"IsProducer\";");

            migrationBuilder.AddColumn<Guid>(
                name: "PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermissionGroups",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SystemKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermissionGroups_AccountingFirms_FirmId",
                        column: x => x.FirmId,
                        principalSchema: "organization",
                        principalTable: "AccountingFirms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyUsers_PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers",
                column: "PermissionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGroups_FirmId_Name",
                schema: "identity",
                table: "PermissionGroups",
                columns: new[] { "FirmId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyUsers_PermissionGroups_PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers",
                column: "PermissionGroupId",
                principalSchema: "identity",
                principalTable: "PermissionGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyUsers_PermissionGroups_PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers");

            migrationBuilder.DropTable(
                name: "PermissionGroups",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_CompanyUsers_PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers");

            migrationBuilder.DropColumn(
                name: "IsAccountant",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PermissionGroupId",
                schema: "identity",
                table: "CompanyUsers");
        }
    }
}
