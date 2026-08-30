namespace AccountingAI.Core.Efatura;

public sealed record EfaturaPortalDayQuery(DateOnly StartDate, DateOnly EndDate);

public static class EfaturaPortalExtractionPlan
{
    public static string FormatPortalDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsEmptyResultText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"não\s+existem\s+(?:faturas|documentos)|não\s+foram\s+encontrad(?:as|os)\b|nenhum(?:a)?\s+(?:fatura|documento)|sem\s+(?:faturas|documentos)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static IReadOnlyList<EfaturaPortalDayQuery> CreateDailyQueries(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(endDate));
        }

        var numberOfDays = endDate.DayNumber - startDate.DayNumber + 1;
        return Enumerable.Range(0, numberOfDays)
            .Select(offset => startDate.AddDays(offset))
            .Select(day => new EfaturaPortalDayQuery(day, day))
            .ToArray();
    }
}
