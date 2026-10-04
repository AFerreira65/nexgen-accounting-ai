using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "identity",
                table: "UserCompanySettings",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "identity",
                table: "UserCompanySettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "UserCompanySettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "identity",
                table: "UserCompanySettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "identity",
                table: "UserAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "UserAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "identity",
                table: "UserAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "Journals",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "accounting",
                table: "Journals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "Journals",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "accounting",
                table: "Journals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "JournalEntries",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "accounting",
                table: "JournalEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "JournalEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "accounting",
                table: "JournalEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "DocumentNumberSeries",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "accounting",
                table: "DocumentNumberSeries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "DocumentNumberSeries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "accounting",
                table: "DocumentNumberSeries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "identity",
                table: "CompanyUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "CompanyUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "identity",
                table: "CompanyUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "organization",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "organization",
                table: "Companies",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "organization",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "Accounts",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "accounting",
                table: "Accounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "Accounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "accounting",
                table: "Accounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedBy",
                schema: "accounting",
                table: "AccountingDocumentTypes",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "identity",
                table: "UserCompanySettings");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "identity",
                table: "UserCompanySettings");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "UserCompanySettings");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "identity",
                table: "UserCompanySettings");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "identity",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "accounting",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "accounting",
                table: "Journals");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "accounting",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "DocumentNumberSeries");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "accounting",
                table: "DocumentNumberSeries");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "DocumentNumberSeries");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "accounting",
                table: "DocumentNumberSeries");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "identity",
                table: "CompanyUsers");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "identity",
                table: "CompanyUsers");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "identity",
                table: "CompanyUsers");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "accounting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "accounting",
                table: "AccountingDocumentTypes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "accounting",
                table: "AccountingDocumentTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                schema: "accounting",
                table: "AccountingDocumentTypes");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "accounting",
                table: "AccountingDocumentTypes");
        }
    }
}
