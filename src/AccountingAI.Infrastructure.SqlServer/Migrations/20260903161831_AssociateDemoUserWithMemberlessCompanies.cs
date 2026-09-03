using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AssociateDemoUserWithMemberlessCompanies : Migration
    {
        private const string DemoUserId = "33333333-3333-3333-3333-333333333333";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // As empresas criadas pelo ecrã de organização em desenvolvimento não ficam
            // associadas a nenhum utilizador, pelo que não aparecem no seletor de
            // empresa. Associa o utilizador de demonstração às empresas sem membros.
            migrationBuilder.Sql(
                "INSERT INTO [identity].[CompanyUsers] (CompanyId, UserId, Role, IsActive) " +
                "SELECT c.Id, '" + DemoUserId + "', 1, 1 " +
                "FROM [organization].[Companies] c " +
                "WHERE EXISTS (SELECT 1 FROM [identity].[UserAccounts] u WHERE u.Id = '" + DemoUserId + "') " +
                "AND NOT EXISTS (SELECT 1 FROM [identity].[CompanyUsers] cu WHERE cu.CompanyId = c.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem reposição: a associação a uma empresa não é reversível com segurança.
        }
    }
}
