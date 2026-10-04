using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShortAccountCodesAreIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // As contas de 1 e 2 dígitos passam a ser de integração (razão/agregadora).
            // Exceções: 56 e 81 (lançadas pelo apuramento/abertura) e as contas que já
            // têm movimentos, para não deixar lançamentos órfãos numa conta agregadora.
            migrationBuilder.Sql(
                "UPDATE a SET a.AllowsPosting = 0 " +
                "FROM accounting.Accounts a " +
                "WHERE a.AllowsPosting = 1 AND LEN(a.Code) <= 2 AND a.Code NOT IN ('56', '81') " +
                "AND NOT EXISTS (SELECT 1 FROM accounting.JournalEntryLines l WHERE l.AccountId = a.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As contas de 2 dígitos sem subcontas de movimento voltam a permitir movimento.
            migrationBuilder.Sql(
                "UPDATE a SET a.AllowsPosting = 1 " +
                "FROM accounting.Accounts a " +
                "WHERE a.AllowsPosting = 0 AND LEN(a.Code) = 2 " +
                "AND NOT EXISTS (SELECT 1 FROM accounting.Accounts c WHERE c.CompanyId = a.CompanyId AND c.ParentCode = a.Code);");
        }
    }
}
