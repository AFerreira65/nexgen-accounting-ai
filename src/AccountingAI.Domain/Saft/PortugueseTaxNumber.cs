namespace AccountingAI.Domain.Saft;

/// <summary>
/// Dígito de controlo de um NIF/NIPC português: nove dígitos, com o último a
/// verificar os oito primeiros por módulo 11 (pesos 9…2).
/// </summary>
public static class PortugueseTaxNumber
{
    /// <summary>Verdadeiro se <paramref name="value"/> tem nove dígitos e o dígito de controlo confere.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = value.Trim();
        if (digits.Length != 9 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 8; index++)
        {
            sum += (digits[index] - '0') * (9 - index);
        }

        var remainder = sum % 11;
        var check = remainder < 2 ? 0 : 11 - remainder;
        return check == digits[8] - '0';
    }

    /// <summary>Verdadeiro se o valor é composto só por dígitos (para distinguir um NIF malformado de um identificador estrangeiro).</summary>
    public static bool IsAllDigits(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().All(char.IsAsciiDigit);
}
