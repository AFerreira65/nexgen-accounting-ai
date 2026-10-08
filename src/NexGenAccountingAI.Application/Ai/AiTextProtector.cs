using System.Text;
using System.Text.RegularExpressions;

namespace NexGenAccountingAI.Application.Ai;

/// <summary>
/// Tira do texto que vai para a IA os nomes e os identificadores, trocando-os por marcadores, e repõe-nos na resposta. A IA vê
/// «[TERCEIRO-1] fatura de 1 200 €» em vez do nome do fornecedor; o utilizador lê a resposta com o nome verdadeiro. Cobre:
/// <list type="bullet">
/// <item>o nome da empresa → <c>[EMPRESA]</c>;</item>
/// <item>os nomes de terceiros (clientes e fornecedores) que se lhe indicam → <c>[TERCEIRO-n]</c>;</item>
/// <item>NIF → <c>[NIF-n]</c>, IBAN português → <c>[IBAN-n]</c>, endereços de email → <c>[EMAIL-n]</c>.</item>
/// </list>
/// A numeração segue a ordem em que cada coisa aparece, e mantém-se enquanto o mesmo objeto for usado — por isso um pedido com
/// várias rondas (a conversa com ferramentas) usa o mesmo objeto do princípio ao fim. Não é anonimização estatística: um nome
/// escrito por extenso num sítio inesperado (ou com erros) pode escapar; é uma redução do que sai, não uma garantia.
/// </summary>
public sealed class AiTextProtector
{
    /// <summary>Nomes mais curtos do que isto não se trocam (apanhavam palavras comuns).</summary>
    public const int MinNameLength = 4;

    /// <summary>Quantos nomes de terceiros se procuram, no máximo (os mais longos primeiro).</summary>
    public const int MaxThirdParties = 5000;

    private static readonly Regex Nif = new(@"(?<![\d.,\-])[1-35-9]\d{8}(?![\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Iban = new(@"\bPT\d{2}(?: ?\d){21}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new(@"\[(EMPRESA|(?:TERCEIRO|NIF|IBAN|EMAIL)-\d+)\]|\b((?:TERCEIRO|NIF|IBAN|EMAIL)-\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] LegalForms = [", Unipessoal, Lda.", ", Unipessoal Lda.", " Unipessoal, Lda.", ", Lda.", ", Lda", " Lda.", " Lda", ", S.A.", " S.A.", ", SA", " SA"];

    private readonly Regex? names;
    private readonly Dictionary<string, string> kindByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> placeholderByOriginal = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> originalByPlaceholder = new(StringComparer.OrdinalIgnoreCase);
    private int thirdParties;
    private int nifs;
    private int ibans;
    private int emails;

    /// <param name="companyName">O nome da empresa (e a forma sem o tipo de sociedade).</param>
    /// <param name="thirdPartyNames">Os nomes de clientes e fornecedores a esconder.</param>
    public AiTextProtector(string? companyName, IEnumerable<string>? thirdPartyNames = null)
    {
        var companyForms = new List<string>();
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            var trimmed = companyName.Trim();
            companyForms.Add(trimmed);
            foreach (var form in LegalForms)
            {
                if (trimmed.EndsWith(form, StringComparison.OrdinalIgnoreCase))
                {
                    companyForms.Add(trimmed[..^form.Length].Trim());
                }
            }
        }

        var all = new List<string>();
        foreach (var form in companyForms.Where(item => item.Length >= MinNameLength).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            kindByName[form] = "EMPRESA";
            all.Add(form);
        }

        foreach (var name in (thirdPartyNames ?? []).Select(item => item?.Trim() ?? string.Empty)
                     .Where(item => item.Length >= MinNameLength && !kindByName.ContainsKey(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(item => item.Length)
                     .Take(MaxThirdParties))
        {
            kindByName[name] = "TERCEIRO";
            all.Add(name);
        }

        originalByPlaceholder["[EMPRESA]"] = companyForms.FirstOrDefault() ?? string.Empty;
        if (all.Count > 0)
        {
            var alternation = string.Join('|', all.OrderByDescending(item => item.Length).Select(Regex.Escape));
            names = new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>O texto sem os nomes e identificadores, com os marcadores no lugar deles.</summary>
    public string Protect(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        if (names is not null)
        {
            result = names.Replace(result, match => kindByName[match.Value] == "EMPRESA" ? "[EMPRESA]" : Mark(match.Value, "TERCEIRO", ref thirdParties));
        }

        result = Iban.Replace(result, match => Mark(match.Value, "IBAN", ref ibans));
        result = Email.Replace(result, match => Mark(match.Value, "EMAIL", ref emails));
        return Nif.Replace(result, match => Mark(match.Value, "NIF", ref nifs));
    }

    /// <summary>Repõe os nomes e os identificadores verdadeiros no texto que veio da IA.</summary>
    /// <param name="jsonEscaped">O texto é JSON: o que se repõe escreve-se já com as aspas e barras escapadas.</param>
    public string Restore(string? text, bool jsonEscaped = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return Placeholder.Replace(text, match =>
        {
            var key = $"[{(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToUpperInvariant()}]";
            if (!originalByPlaceholder.TryGetValue(key, out var original) || original.Length == 0)
            {
                return match.Value;
            }

            return jsonEscaped ? JsonEscape(original) : original;
        });
    }

    private string Mark(string original, string kind, ref int counter)
    {
        var normalized = Regex.Replace(original, @"\s+", string.Empty);
        var key = $"{kind}|{(kind is "TERCEIRO" or "EMAIL" ? original.Trim().ToLowerInvariant() : normalized)}";
        if (placeholderByOriginal.TryGetValue(key, out var existing))
        {
            return existing;
        }

        counter++;
        var placeholder = $"[{kind}-{counter}]";
        placeholderByOriginal[key] = placeholder;
        originalByPlaceholder[placeholder] = original;
        return placeholder;
    }

    private static string JsonEscape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }
}
