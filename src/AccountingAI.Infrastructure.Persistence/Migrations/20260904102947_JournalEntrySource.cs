using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalEntrySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                schema: "accounting",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Retroação: os lançamentos de abertura e apuramento já existentes passam
            // a Opening (2) e Closing (3); os restantes ficam Manual (0). Os
            // lançamentos importados de SAF-T não têm marca e ficam Manual.
            migrationBuilder.Sql(
                """
                UPDATE je
                SET je.[Source] = CASE dt.[Code] WHEN 'AB' THEN 2 WHEN 'AP' THEN 3 END
                FROM [accounting].[JournalEntries] je
                INNER JOIN [accounting].[AccountingDocumentTypes] dt ON dt.[Id] = je.[DocumentTypeId]
                WHERE dt.[Code] IN ('AB', 'AP');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                schema: "accounting",
                table: "JournalEntries");
        }
    }
}
