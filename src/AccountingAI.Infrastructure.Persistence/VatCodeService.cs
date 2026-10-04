using AccountingAI.Application.Fiscal;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Um código de IVA, com as enumerações pelo nome (a PWA traduz para pt-PT).</summary>
public sealed record VatCodeRow(
    string Code,
    string Name,
    string Rate,
    decimal Percentage,
    string Nature,
    string Region,
    bool IsActive);

/// <summary>Os dados editáveis de um código de IVA (o código em si só se define na criação).</summary>
public sealed record VatCodeInput(
    string Name,
    VatRate Rate,
    decimal Percentage,
    VatOperationNature Nature,
    VatRegion Region,
    bool IsActive = true);

/// <summary>
/// Os códigos de IVA propostos a uma empresa. As percentagens vêm da tabela
/// fiscal do ano (<see cref="FiscalTableCatalog"/>), ficam gravadas em cada
/// código e são editáveis, para a empresa as corrigir sem esperar por uma
/// versão nova.
/// </summary>
public static class VatCodeDefaults
{
    private static (decimal Reduced, decimal Intermediate, decimal Normal) PercentagesOf(VatRegion region, int year)
    {
        var table = FiscalTableCatalog.For(Math.Max(year, FiscalTableCatalog.FirstYear))!;
        var rates = table.Vat.Single(item => item.Region == region.ToString());
        return (rates.Reduced * 100m, rates.Intermediate * 100m, rates.Normal * 100m);
    }

    private static readonly (string Prefix, string Label, VatOperationNature Nature, bool AllRates)[] Taxed =
    [
        ("V", "Vendas de bens", VatOperationNature.TransmissoesInternas, true),
        ("S", "Prestações de serviços", VatOperationNature.PrestacoesServicosInternas, true),
        ("C-EXI", "Compras de existências", VatOperationNature.IvaDedutivelExistencias, true),
        ("C-OBS", "Compras de outros bens e serviços", VatOperationNature.IvaDedutivelOutros, true),
        ("C-IMO", "Compras de imobilizado", VatOperationNature.IvaDedutivelImobilizado, false),
        ("AIB", "Aquisições intracomunitárias de bens", VatOperationNature.AquisicoesIntracomunitariasBens, false),
        ("AIS", "Aquisições intracomunitárias de serviços", VatOperationNature.AquisicoesIntracomunitariasServicos, false),
        ("AUT", "Autoliquidação (outras)", VatOperationNature.AutoliquidacaoOutras, false),
    ];

    public static IReadOnlyList<VatCode> For(Guid companyId, VatRegion region, int year)
    {
        var (reduced, intermediate, normal) = PercentagesOf(region, year);
        var codes = new List<VatCode>();

        foreach (var (prefix, label, nature, allRates) in Taxed)
        {
            codes.Add(VatCode.Create(companyId, $"{prefix}-NOR", $"{label} — taxa normal", VatRate.Normal, normal, nature, region));
            if (allRates)
            {
                codes.Add(VatCode.Create(companyId, $"{prefix}-INT", $"{label} — taxa intermédia", VatRate.Intermedia, intermediate, nature, region));
                codes.Add(VatCode.Create(companyId, $"{prefix}-RED", $"{label} — taxa reduzida", VatRate.Reduzida, reduced, nature, region));
            }
        }

        codes.Add(VatCode.Create(companyId, "V-ISE", "Operações isentas sem direito à dedução", VatRate.Isento, 0m, VatOperationNature.OperacoesIsentasSemDeducao, region));
        codes.Add(VatCode.Create(companyId, "V-EXP", "Exportações e isentas com direito à dedução", VatRate.Isento, 0m, VatOperationNature.ExportacoesEIsentasComDeducao, region));
        codes.Add(VatCode.Create(companyId, "V-TIB", "Transmissões intracomunitárias de bens", VatRate.Isento, 0m, VatOperationNature.TransmissoesIntracomunitariasBens, region));
        codes.Add(VatCode.Create(companyId, "S-PSI", "Prestações de serviços intracomunitárias", VatRate.Isento, 0m, VatOperationNature.PrestacoesServicosIntracomunitarias, region));
        codes.Add(VatCode.Create(companyId, "V-TRI", "Operações triangulares", VatRate.Isento, 0m, VatOperationNature.OperacoesTriangulares, region));
        return codes;
    }

    public static VatRegion RegionOf(TaxRegion region) => region switch
    {
        TaxRegion.Azores => VatRegion.Acores,
        TaxRegion.Madeira => VatRegion.Madeira,
        _ => VatRegion.Continente,
    };
}

/// <summary>
/// Mantém os códigos de IVA (<see cref="VatCode"/>) de uma empresa: lista,
/// criação, edição e a criação dos códigos por omissão da região da empresa.
/// As regras falhadas saem como <see cref="ArgumentException"/> ou
/// <see cref="InvalidOperationException"/> com a mensagem em pt-PT.
/// </summary>
public sealed class VatCodeService(AccountingDbContext context)
{
    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IReadOnlyList<VatCodeRow>?> ListAsync(
        Guid userId, Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var codes = await context.VatCodes
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && (includeInactive || item.IsActive))
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return codes.Select(ToRow).ToList();
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<VatCodeRow?> CreateAsync(
        Guid userId, Guid companyId, string code, VatCodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var vatCode = VatCode.Create(companyId, code, input.Name, input.Rate, input.Percentage, input.Nature, input.Region);
        var duplicate = await context.VatCodes
            .AnyAsync(item => item.CompanyId == companyId && item.Code == vatCode.Code, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw new InvalidOperationException("Já existe um código de IVA com esse código na empresa.");
        }

        context.VatCodes.Add(vatCode);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(vatCode);
    }

    /// <returns><c>null</c> sem acesso à empresa ou código desconhecido.</returns>
    public async Task<VatCodeRow?> UpdateAsync(
        Guid userId, Guid companyId, string code, VatCodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        var vatCode = await context.VatCodes
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Code == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (vatCode is null)
        {
            return null;
        }

        var changesMeaning = input.Rate != vatCode.Rate || input.Nature != vatCode.Nature || input.Region != vatCode.Region ||
            decimal.Round(input.Percentage, 2, MidpointRounding.AwayFromZero) != vatCode.Percentage;
        if (changesMeaning &&
            await context.JournalEntryLines.AnyAsync(line => line.VatCodeId == vatCode.Id, cancellationToken).ConfigureAwait(false))
        {
            // Mudar o significado de um código usado mudaria a leitura do passado.
            throw new InvalidOperationException(
                "O código já tem lançamentos: a taxa, a percentagem, a natureza e a região não podem mudar. Desative-o e crie um código novo.");
        }

        vatCode.Update(input.Name, input.Rate, input.Percentage, input.Nature, input.Region);
        vatCode.SetActive(input.IsActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToRow(vatCode);
    }

    /// <summary>Cria os códigos por omissão que ainda não existem; não altera os que já existem.</summary>
    /// <returns>-1 sem acesso; senão o número de códigos criados.</returns>
    public async Task<int> EnsureDefaultsAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return -1;
        }

        var existing = await context.VatCodes
            .Where(item => item.CompanyId == companyId)
            .Select(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        var missing = VatCodeDefaults.For(companyId, VatCodeDefaults.RegionOf(company.TaxRegion), DateTime.UtcNow.Year)
            .Where(item => !existingSet.Contains(item.Code))
            .ToList();

        context.VatCodes.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return missing.Count;
    }

    private static VatCodeRow ToRow(VatCode item) => new(
        item.Code, item.Name, item.Rate.ToString(), item.Percentage, item.Nature.ToString(), item.Region.ToString(), item.IsActive);

    private Task<Company?> FindCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
