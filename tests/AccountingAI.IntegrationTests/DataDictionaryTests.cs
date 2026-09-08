using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.IntegrationTests;

[TestClass]
public sealed class DataDictionaryTests
{
    // O dicionário lê só o modelo do EF — não precisa de ligação à base.
    private static DataDictionaryService Service()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=(localdb)\\unused;Database=unused;Trusted_Connection=True")
            .Options;
        return new DataDictionaryService(new AccountingDbContext(options));
    }

    [TestMethod]
    public void ListsEveryMappedTableWithAPurpose()
    {
        var tables = Service().ListTables();

        Assert.IsTrue(tables.Count >= 12, $"Esperava pelo menos 12 tabelas, obtive {tables.Count}.");
        Assert.IsTrue(tables.Any(t => t.Schema == "accounting" && t.Name == "Accounts"));
        Assert.IsTrue(tables.Any(t => t.Schema == "organization" && t.Name == "Companies"));
        Assert.IsTrue(tables.Any(t => t.Schema == "accounting" && t.Name == "EFaturaPurchase"));
        Assert.IsTrue(
            tables.All(t => !string.IsNullOrWhiteSpace(t.Purpose)),
            "Todas as tabelas mapeadas devem ter um propósito em pt-PT: " +
            string.Join(", ", tables.Where(t => string.IsNullOrWhiteSpace(t.Purpose)).Select(t => $"{t.Schema}.{t.Name}")));
        Assert.IsTrue(tables.All(t => t.ColumnCount > 0));
    }

    [TestMethod]
    public void DetailsAccountsColumnsWithTypesKeysAndDescriptions()
    {
        var accounts = Service().GetTable("accounting", "Accounts");

        Assert.IsNotNull(accounts);
        Assert.AreEqual("Accounts", accounts!.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(accounts.Purpose));

        var id = accounts.Columns.Single(c => c.Name == "Id");
        Assert.IsTrue(id.PrimaryKey);
        Assert.AreEqual("Guid", id.ClrType);
        Assert.AreSame(accounts.Columns[0], id, "A chave primária vem primeiro.");

        var companyId = accounts.Columns.Single(c => c.Name == "CompanyId");
        Assert.IsTrue(companyId.ForeignKey);
        Assert.AreEqual("organization.Companies", companyId.References);

        var code = accounts.Columns.Single(c => c.Name == "Code");
        Assert.IsTrue(code.MaxLength is > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(code.Description));

        var audited = accounts.Columns.Single(c => c.Name == "CreatedAtUtc");
        Assert.IsFalse(string.IsNullOrWhiteSpace(audited.Description));

        var described = accounts.Columns.Count(c => !string.IsNullOrWhiteSpace(c.Description));
        Assert.IsTrue(
            described >= accounts.Columns.Count * 0.7,
            $"Cobertura de descrições baixa: {described}/{accounts.Columns.Count}.");
    }

    [TestMethod]
    public void UnknownTableReturnsNull()
    {
        Assert.IsNull(Service().GetTable("accounting", "NaoExiste"));
        Assert.IsNull(Service().GetTable("nada", "Accounts"));
    }
}
