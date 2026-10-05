using System.Globalization;

namespace NexGenAccountingAI.Application.Maps;

/// <summary>
/// As fórmulas dos mapas: contas com <c>+ - * /</c>, parênteses, números e referências
/// a outras linhas (ou colunas) pelo código. <c>A % B</c> é «A em percentagem de B».
/// Uma referência desconhecida é um erro; uma divisão por zero dá «sem valor».
/// Não há funções nem acesso a mais nada: é aritmética e só.
/// </summary>
public sealed class MapFormula
{
    private readonly Node _root;

    private MapFormula(Node root, IReadOnlyList<string> references)
    {
        _root = root;
        References = references;
    }

    /// <summary>Os códigos referidos, pela ordem em que aparecem.</summary>
    public IReadOnlyList<string> References { get; }

    /// <exception cref="FormatException">A fórmula não é válida (mensagem em português).</exception>
    public static MapFormula Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("A fórmula está vazia.");
        }

        var parser = new Parser(text);
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        return new MapFormula(root, parser.References);
    }

    /// <param name="resolve">O valor de cada referência; <c>null</c> é «sem valor» e propaga-se.</param>
    public decimal? Evaluate(Func<string, decimal?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return _root.Evaluate(resolve);
    }

    private abstract record Node
    {
        public abstract decimal? Evaluate(Func<string, decimal?> resolve);
    }

    private sealed record Number(decimal Value) : Node
    {
        public override decimal? Evaluate(Func<string, decimal?> resolve) => Value;
    }

    private sealed record Reference(string Code) : Node
    {
        public override decimal? Evaluate(Func<string, decimal?> resolve) => resolve(Code);
    }

    private sealed record Negate(Node Operand) : Node
    {
        public override decimal? Evaluate(Func<string, decimal?> resolve) => -Operand.Evaluate(resolve);
    }

    private sealed record Binary(char Operator, Node Left, Node Right) : Node
    {
        public override decimal? Evaluate(Func<string, decimal?> resolve)
        {
            var left = Left.Evaluate(resolve);
            var right = Right.Evaluate(resolve);
            if (left is null || right is null)
            {
                return null;
            }

            return Operator switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' => right == 0m ? null : left / right,
                '%' => right == 0m ? null : left / right * 100m,
                _ => null,
            };
        }
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public List<string> References { get; } = [];

        public Node ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                var op = Peek();
                if (op is not ('+' or '-'))
                {
                    return left;
                }

                _position++;
                left = new Binary(op, left, ParseTerm());
            }
        }

        public void ExpectEnd()
        {
            if (Peek() != '\0')
            {
                throw new FormatException($"Carácter inesperado «{text[_position]}» na fórmula «{text}».");
            }
        }

        private Node ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                var op = Peek();
                if (op is not ('*' or '/' or '%'))
                {
                    return left;
                }

                _position++;
                left = new Binary(op, left, ParseFactor());
            }
        }

        private Node ParseFactor()
        {
            var character = Peek();
            if (character == '-')
            {
                _position++;
                return new Negate(ParseFactor());
            }

            if (character == '(')
            {
                _position++;
                var inner = ParseExpression();
                if (Peek() != ')')
                {
                    throw new FormatException($"Falta fechar um parêntese na fórmula «{text}».");
                }

                _position++;
                return inner;
            }

            var start = _position;
            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] is '_' or '.' or ','))
            {
                _position++;
            }

            if (_position == start)
            {
                throw new FormatException(character == '\0'
                    ? $"A fórmula «{text}» acaba a meio."
                    : $"Carácter inesperado «{character}» na fórmula «{text}».");
            }

            var token = text[start.._position];
            // Um número (com vírgula ou ponto decimal) ou o código de uma linha.
            if (char.IsDigit(token[0]) && decimal.TryParse(token.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                return new Number(number);
            }

            if (token.Contains(',', StringComparison.Ordinal) || token.Contains('.', StringComparison.Ordinal))
            {
                throw new FormatException($"«{token}» não é um número nem o código de uma linha na fórmula «{text}».");
            }

            References.Add(token);
            return new Reference(token);
        }

        private char Peek()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }

            return _position < text.Length ? text[_position] : '\0';
        }
    }
}
