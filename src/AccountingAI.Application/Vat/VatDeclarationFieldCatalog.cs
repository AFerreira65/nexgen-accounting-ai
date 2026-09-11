using AccountingAI.Domain.Accounting;

namespace AccountingAI.Application.Vat;

/// <summary>
/// O campo numerado da declaração periódica de IVA em vigor que corresponde a uma
/// linha do resumo (<see cref="VatSummary"/> em <c>Infrastructure.SqlServer</c>) —
/// best-effort, nunca oficial. Ver <see cref="VatDeclarationFieldCatalog.Disclaimer"/>.
/// </summary>
/// <param name="Number">O número do campo.</param>
/// <param name="Note">Uma nota, quando o campo precisa de contexto adicional (ex.: Anexo R).</param>
public sealed record VatDeclarationField(int Number, string? Note = null);

/// <summary>
/// Catálogo, em código, do campo numerado da declaração periódica de IVA em vigor
/// que corresponde a cada combinação (base/imposto, natureza, taxa) do resumo de
/// IVA (F2-09B). Transcrito do <see cref="Source"/> — um manual pedagógico com o
/// formulário e exemplos completos e cruzados, não o texto oficial da AT — e por
/// isso <b>nunca</b> deve ser tratado como certificado; ver <see cref="Disclaimer"/>.
/// Só cobre operações localizadas na sede da empresa (campos 1 a 41, 61); as
/// realizadas numa região diferente da sede vão para o Anexo R (mesma numeração,
/// noutro impresso) — este catálogo assinala-o na <see cref="VatDeclarationField.Note"/>
/// mas não distingue automaticamente sede vs. Anexo R.
/// </summary>
public static class VatDeclarationFieldCatalog
{
    public const string Source =
        "Ribeiro, J. C., & Silva, F. J. (2026). Manual prático de preenchimento da " +
        "declaração do IVA e anexos. CEOS.PP, ISCAP, Instituto Politécnico do Porto.";

    public const string Disclaimer =
        "Mapeamento de apoio, não oficial nem certificado pela AT — confirme sempre " +
        "contra o modelo em vigor antes de submeter. Cobre só as operações localizadas " +
        "na sede da empresa; as localizadas numa região diferente exigem o Anexo R " +
        "(a mesma numeração, noutro impresso), que este apoio assinala mas não substitui.";

    private const string AnexoRNote =
        "Quando a operação está localizada numa região diferente da sede da empresa, " +
        "este número refere-se ao Anexo R dessa região (mesma numeração), não à " +
        "declaração principal.";

    /// <summary>
    /// O campo correspondente a uma linha do resumo, ou <c>null</c> quando a
    /// combinação não tem um campo específico e inequívoco neste catálogo (ex.:
    /// a base de uma compra, que não tem campo próprio — só o imposto dedutível).
    /// </summary>
    public static VatDeclarationField? ForRow(string kind, VatOperationNature nature, VatRate rate, VatRegion region)
    {
        var note = region == VatRegion.Continente ? null : AnexoRNote;

        if (kind == "base" && nature is VatOperationNature.TransmissoesInternas or VatOperationNature.PrestacoesServicosInternas)
        {
            return rate switch
            {
                VatRate.Reduzida => new VatDeclarationField(1, note),
                VatRate.Intermedia => new VatDeclarationField(5, note),
                VatRate.Normal => new VatDeclarationField(3, note),
                _ => null,
            };
        }

        if (kind == "imposto" && nature is VatOperationNature.TransmissoesInternas or VatOperationNature.PrestacoesServicosInternas)
        {
            return rate switch
            {
                VatRate.Reduzida => new VatDeclarationField(2, note),
                VatRate.Intermedia => new VatDeclarationField(6, note),
                VatRate.Normal => new VatDeclarationField(4, note),
                _ => null,
            };
        }

        if (kind == "base" && nature == VatOperationNature.ExportacoesEIsentasComDeducao)
        {
            return new VatDeclarationField(8, note);
        }

        if (kind == "base" && nature == VatOperationNature.OperacoesIsentasSemDeducao)
        {
            return new VatDeclarationField(9, note);
        }

        if (nature == VatOperationNature.AquisicoesIntracomunitariasBens)
        {
            return kind switch
            {
                "base" => new VatDeclarationField(12, note),
                "imposto" => new VatDeclarationField(13, note),
                _ => null,
            };
        }

        if (nature == VatOperationNature.AquisicoesIntracomunitariasServicos)
        {
            return kind switch
            {
                "base" => new VatDeclarationField(16, note),
                "imposto" => new VatDeclarationField(17, note),
                _ => null,
            };
        }

        if (kind == "imposto" && nature == VatOperationNature.IvaDedutivelImobilizado)
        {
            return new VatDeclarationField(20, note);
        }

        if (kind == "imposto" && nature == VatOperationNature.IvaDedutivelExistencias)
        {
            return rate switch
            {
                VatRate.Reduzida => new VatDeclarationField(21, note),
                VatRate.Intermedia => new VatDeclarationField(23, note),
                VatRate.Normal => new VatDeclarationField(22, note),
                _ => null,
            };
        }

        if (kind == "imposto" && nature == VatOperationNature.IvaDedutivelOutros)
        {
            return new VatDeclarationField(24, note);
        }

        if (kind == "imposto" && nature == VatOperationNature.RegularizacoesFavorSujeitoPassivo)
        {
            return new VatDeclarationField(40, note);
        }

        if (kind == "imposto" && nature == VatOperationNature.RegularizacoesFavorEstado)
        {
            return new VatDeclarationField(41, note);
        }

        return null;
    }
}
