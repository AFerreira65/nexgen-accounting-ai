using System.Globalization;
using System.Text;

namespace NexGenAccountingAI.Domain.Banking;

/// <summary>
/// Uma regra para lançar movimentos do extrato: quando o descritivo contém o
/// texto da regra (sem distinguir maiúsculas nem acentos), o movimento é
/// lançado com o lançamento-tipo indicado, com a conta do banco na
/// contrapartida. Serve para o que se repete — comissões, imposto do selo,
/// juros, a renda paga por débito direto.
/// </summary>
public sealed class BankEntryRule
{
    public const int PatternMaxLength = 100;

    private BankEntryRule()
    {
    }

    private BankEntryRule(Guid id, Guid companyId, string pattern)
    {
        Id = id;
        CompanyId = companyId;
        Pattern = pattern;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O texto procurado no descritivo, já normalizado (maiúsculas, sem acentos, espaços simples).</summary>
    public string Pattern { get; private set; } = string.Empty;

    public Guid EntryTemplateId { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static BankEntryRule Create(Guid companyId, string pattern)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new BankEntryRule(Guid.NewGuid(), companyId, NormalizePattern(pattern));
    }

    public void Update(Guid entryTemplateId, bool isActive)
    {
        if (entryTemplateId == Guid.Empty)
        {
            throw new ArgumentException("O lançamento-tipo é obrigatório.", nameof(entryTemplateId));
        }

        EntryTemplateId = entryTemplateId;
        IsActive = isActive;
    }

    public bool Matches(string? description) => Normalize(description).Contains(Pattern, StringComparison.Ordinal);

    public static string NormalizePattern(string? pattern)
    {
        var normalized = Normalize(pattern);
        if (normalized.Length < 3)
        {
            throw new ArgumentException("O texto da regra tem de ter pelo menos 3 caracteres.", nameof(pattern));
        }

        if (normalized.Length > PatternMaxLength)
        {
            throw new ArgumentException($"O texto da regra não pode ter mais de {PatternMaxLength} caracteres.", nameof(pattern));
        }

        return normalized;
    }

    /// <summary>Maiúsculas, sem acentos e com os espaços reduzidos a um.</summary>
    public static string Normalize(string? value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
