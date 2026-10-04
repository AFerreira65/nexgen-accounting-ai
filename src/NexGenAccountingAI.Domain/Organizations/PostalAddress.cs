namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// Regras comuns aos campos de morada da empresa e dos terceiros. Os limites
/// são os do SAF-T (PT): 210 caracteres na morada, 50 na localidade e 20 no
/// código postal.
/// </summary>
public static class PostalAddress
{
    public const int AddressDetailMaxLength = 210;

    public const int CityMaxLength = 50;

    public const int PostalCodeMaxLength = 20;

    /// <summary>Apara o texto; em branco devolve <c>null</c>; acima do limite recusa.</summary>
    public static string? Normalize(string? value, int maxLength, string parameterName)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            throw new ArgumentException($"O campo não pode ter mais de {maxLength} caracteres.", parameterName);
        }

        return text;
    }

    /// <summary>Verdadeiro para um código postal português: quatro dígitos, hífen, três dígitos.</summary>
    public static bool IsPortuguesePostalCode(string value) =>
        value.Length == 8 &&
        value[4] == '-' &&
        value.AsSpan(0, 4).IndexOfAnyExceptInRange('0', '9') < 0 &&
        value.AsSpan(5, 3).IndexOfAnyExceptInRange('0', '9') < 0;
}
