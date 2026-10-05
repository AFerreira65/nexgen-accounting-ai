namespace NexGenAccountingAI.Application.Maps;

/// <summary>
/// A escolha das contas de uma linha, por prefixos do código: <c>61 + 62 - 6221</c>
/// são todas as contas começadas por 61 ou 62, menos as começadas por 6221; um
/// intervalo <c>61..63</c> vale por 61, 62 e 63. A vírgula serve de «+». Como é por
/// prefixo, uma subconta criada depois entra sozinha na linha.
/// </summary>
public sealed class MapAccountSelector
{
    private readonly List<(string From, string To)> _include = [];
    private readonly List<(string From, string To)> _exclude = [];

    private MapAccountSelector()
    {
    }

    public bool IsEmpty => _include.Count == 0;

    /// <summary>As classes (primeiro algarismo) que a escolha abrange.</summary>
    public IEnumerable<char> Classes => _include.SelectMany(range => Enumerable.Range(range.From[0], range.To[0] - range.From[0] + 1)).Select(code => (char)code).Distinct();

    /// <exception cref="FormatException">O texto não é uma escolha de contas válida (mensagem em português).</exception>
    public static MapAccountSelector Parse(string? text)
    {
        var selector = new MapAccountSelector();
        var source = (text ?? string.Empty).Replace(',', '+');
        var position = 0;
        var negative = false;
        while (position < source.Length)
        {
            var character = source[position];
            if (char.IsWhiteSpace(character))
            {
                position++;
                continue;
            }

            if (character is '+' or '-')
            {
                negative = character == '-';
                position++;
                continue;
            }

            var start = position;
            while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '.'))
            {
                position++;
            }

            if (position == start)
            {
                throw new FormatException($"Carácter inesperado «{character}» na escolha de contas «{text}». Use prefixos de conta separados por + e -, por exemplo «61 + 62 - 6221».");
            }

            var term = source[start..position];
            var range = term.Split("..", StringSplitOptions.None);
            if (range.Length > 2 || range.Any(part => part.Length == 0 || part.Contains('.', StringComparison.Ordinal)))
            {
                throw new FormatException($"«{term}» não é um prefixo de conta nem um intervalo (como «61..63») na escolha «{text}».");
            }

            var from = range[0].ToUpperInvariant();
            var to = range[^1].ToUpperInvariant();
            if (string.CompareOrdinal(from, to) > 0)
            {
                throw new FormatException($"O intervalo «{term}» está ao contrário.");
            }

            (negative ? selector._exclude : selector._include).Add((from, to));
            negative = false;
        }

        return selector;
    }

    public bool Matches(string accountCode)
    {
        ArgumentNullException.ThrowIfNull(accountCode);
        return _include.Any(range => InRange(accountCode, range)) && !_exclude.Any(range => InRange(accountCode, range));
    }

    /// <summary>A conta começa por um prefixo entre <c>From</c> e <c>To</c> (comparando só o comprimento do prefixo).</summary>
    private static bool InRange(string code, (string From, string To) range)
    {
        static string Head(string value, int length) => value.Length <= length ? value : value[..length];
        return code.Length >= range.From.Length &&
            string.CompareOrdinal(Head(code, range.From.Length), range.From) >= 0 &&
            string.CompareOrdinal(Head(code, range.To.Length), range.To) <= 0 &&
            code.Length >= Math.Min(range.From.Length, range.To.Length);
    }
}
