using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer.Persistence;

/// <summary>
/// Acrescenta a cada entidade editável quatro colunas-sombra de auditoria —
/// <c>CreatedAtUtc</c>, <c>CreatedBy</c>, <c>ModifiedAtUtc</c>, <c>ModifiedBy</c>
/// — sem tocar no domínio. O <see cref="AuditStampingInterceptor"/> preenche-as
/// ao gravar; a presença de <c>ModifiedAtUtc</c> é o sinal de que um tipo é
/// auditado. As entidades que já guardam um <c>CreatedAtUtc</c> próprio
/// (<c>EFaturaPurchase</c>, <c>SaftValidationRun</c>) ficam de fora.
/// </summary>
internal static class AuditShadowProperties
{
    /// <summary>O sinal, para o interceptor, de que um tipo é auditado.</summary>
    public const string ModifiedAtUtc = "ModifiedAtUtc";

    private static readonly Type[] AuditedTypes =
    [
        typeof(JournalEntry),
        typeof(LedgerAccount),
        typeof(AccountingJournal),
        typeof(AccountingDocumentType),
        typeof(DocumentNumberSeries),
        typeof(Company),
        typeof(UserAccount),
        typeof(CompanyUser),
        typeof(UserCompanySettings),
        typeof(SavedAnalysis),
        typeof(ReportTemplate),
        typeof(ManagementReport),
        typeof(CompanyAiSettings),
        typeof(VatReturnFiling),
        typeof(CompanyAtCredentials),
        typeof(AccountVatClassification),
        typeof(IrcEstimateFiling),
        typeof(Party),
        typeof(VatCode),
        typeof(EntryTemplate),
        typeof(CostCenter),
        typeof(FixedAsset),
        typeof(IrcReturnValue),
        typeof(FiscalDossierItem),
        typeof(BudgetLine),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var type in AuditedTypes)
        {
            var builder = modelBuilder.Entity(type);

            // Algumas entidades (Company, UserAccount, CompanyUser) já têm um
            // CreatedAtUtc próprio — não se lhe toca; as restantes ganham-no como
            // coluna-sombra, com o mesmo default das existentes.
            if (builder.Metadata.FindProperty("CreatedAtUtc") is null)
            {
                builder.Property<DateTimeOffset>("CreatedAtUtc").HasDefaultValueSql("SYSUTCDATETIME()");
            }

            builder.Property<Guid?>("CreatedBy");
            builder.Property<DateTimeOffset?>(ModifiedAtUtc);
            builder.Property<Guid?>("ModifiedBy");
        }
    }

    public static bool IsAudited(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType) =>
        entityType.FindProperty(ModifiedAtUtc) is not null;
}
