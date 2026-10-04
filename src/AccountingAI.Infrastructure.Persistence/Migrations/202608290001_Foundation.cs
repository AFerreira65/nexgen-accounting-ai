using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AccountingDbContext))]
[Migration("202608290001_Foundation")]
public partial class Foundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "infrastructure");

        migrationBuilder.CreateTable(
            name: "FoundationState",
            schema: "infrastructure",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FoundationState", item => item.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FoundationState",
            schema: "infrastructure");
    }
}
