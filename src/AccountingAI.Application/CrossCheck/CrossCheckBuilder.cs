namespace AccountingAI.Application.CrossCheck;

/// <summary>Uma fatura do e-Fatura, tal como entra no cruzamento.</summary>
public sealed record CrossCheckInvoiceInput(
    Guid Id,
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNo,
    string? Atcud,
    DateOnly InvoiceDate,
    decimal TaxBase,
    decimal TaxAmount,
    decimal GrossTotal);

/// <summary>
/// Um lançamento de compra da contabilidade (tem linhas em classe 6 e/ou
/// <c>2432*</c>), já com a base e o IVA dedutível somados.
/// </summary>
public sealed record CrossCheckPurchaseEntryInput(
    string Reference,
    DateOnly EntryDate,
    string DocumentReference,
    string Description,
    decimal PurchaseBase,
    decimal DeductibleVat);

/// <summary>Rendimentos e IVA liquidado da contabilidade num mês.</summary>
public sealed record CrossCheckSalesMonthInput(int Year, int Month, decimal Income, decimal OutputVat);

/// <summary>
/// Monta o <see cref="CrossCheckReport"/> a partir das faturas do e-Fatura, dos
/// lançamentos de compra e das somas de vendas do período. É puro (sem base de
/// dados): a correspondência fatura↔lançamento faz-se pelo número de documento
/// normalizado com uma tolerância de datas, já que a contabilidade não guarda o
/// NIF do fornecedor ao nível do documento.
/// </summary>
public static class CrossCheckBuilder
{
    /// <summary>Dias de diferença tolerados entre a data da fatura e a data do lançamento.</summary>
    public const int DateToleranceDays = 31;

    /// <summary>Comprimento mínimo do número normalizado para se tentar a correspondência.</summary>
    public const int MinInvoiceKeyLength = 3;

    /// <summary>Máximo de linhas devolvidas em cada lista de divergências.</summary>
    public const int MaxListed = 200;

    public static CrossCheckReport Build(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<CrossCheckInvoiceInput> invoices,
        IReadOnlyList<CrossCheckPurchaseEntryInput> purchaseEntries,
        IReadOnlyList<CrossCheckSalesMonthInput> salesMonths,
        decimal periodDebit,
        decimal periodCredit)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(purchaseEntries);
        ArgumentNullException.ThrowIfNull(salesMonths);

        var orderedInvoices = invoices
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.InvoiceNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedEntries = purchaseEntries
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entryKeys = orderedEntries
            .Select(entry => Normalize(entry.DocumentReference))
            .ToArray();
        var entryMatched = new bool[orderedEntries.Count];
        var invoiceMatched = new bool[orderedInvoices.Count];
        var matchedCount = 0;

        for (var i = 0; i < orderedInvoices.Count; i++)
        {
            var invoiceKey = Normalize(orderedInvoices[i].InvoiceNo);
            if (invoiceKey.Length < MinInvoiceKeyLength)
            {
                continue;
            }

            for (var j = 0; j < orderedEntries.Count; j++)
            {
                if (entryMatched[j] || entryKeys[j].Length < MinInvoiceKeyLength)
                {
                    continue;
                }

                if (!KeysMatch(invoiceKey, entryKeys[j]))
                {
                    continue;
                }

                var dayGap = Math.Abs(
                    orderedEntries[j].EntryDate.DayNumber - orderedInvoices[i].InvoiceDate.DayNumber);
                if (dayGap > DateToleranceDays)
                {
                    continue;
                }

                entryMatched[j] = true;
                invoiceMatched[i] = true;
                matchedCount++;
                break;
            }
        }

        var unbooked = new List<CrossCheckUnbookedInvoice>();
        for (var i = 0; i < orderedInvoices.Count && unbooked.Count < MaxListed; i++)
        {
            if (invoiceMatched[i])
            {
                continue;
            }

            var invoice = orderedInvoices[i];
            unbooked.Add(new CrossCheckUnbookedInvoice(
                invoice.Id, invoice.SupplierTaxId, invoice.SupplierName, invoice.InvoiceNo,
                invoice.Atcud, invoice.InvoiceDate, invoice.TaxBase, invoice.TaxAmount, invoice.GrossTotal));
        }

        var unmatched = new List<CrossCheckUnmatchedEntry>();
        for (var j = 0; j < orderedEntries.Count && unmatched.Count < MaxListed; j++)
        {
            if (entryMatched[j])
            {
                continue;
            }

            var entry = orderedEntries[j];
            unmatched.Add(new CrossCheckUnmatchedEntry(
                entry.Reference, entry.EntryDate, entry.DocumentReference, entry.Description,
                decimal.Round(entry.PurchaseBase, 2), decimal.Round(entry.DeductibleVat, 2)));
        }

        var months = BuildPurchaseMonths(from, to, orderedInvoices, orderedEntries);

        var purchases = new CrossCheckPurchases(
            decimal.Round(orderedInvoices.Sum(invoice => invoice.TaxBase), 2),
            decimal.Round(orderedInvoices.Sum(invoice => invoice.TaxAmount), 2),
            orderedInvoices.Count,
            decimal.Round(orderedEntries.Sum(entry => entry.PurchaseBase), 2),
            decimal.Round(orderedEntries.Sum(entry => entry.DeductibleVat), 2),
            orderedEntries.Count,
            matchedCount,
            months,
            unbooked,
            unmatched);

        var sales = new CrossCheckSales(
            decimal.Round(salesMonths.Sum(month => month.Income), 2),
            decimal.Round(salesMonths.Sum(month => month.OutputVat), 2),
            salesMonths
                .OrderBy(month => month.Year)
                .ThenBy(month => month.Month)
                .Select(month => new CrossCheckSalesMonth(
                    month.Year, month.Month, decimal.Round(month.Income, 2), decimal.Round(month.OutputVat, 2)))
                .ToList());

        return new CrossCheckReport(
            from,
            to,
            purchases,
            sales,
            new CrossCheckTotals(decimal.Round(periodDebit, 2), decimal.Round(periodCredit, 2)));
    }

    private static IReadOnlyList<CrossCheckPurchaseMonth> BuildPurchaseMonths(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<CrossCheckInvoiceInput> invoices,
        IReadOnlyList<CrossCheckPurchaseEntryInput> entries)
    {
        var buckets = new SortedDictionary<(int Year, int Month), MonthAccumulator>();

        MonthAccumulator Bucket(int year, int month)
        {
            var key = (year, month);
            if (!buckets.TryGetValue(key, out var accumulator))
            {
                accumulator = new MonthAccumulator();
                buckets[key] = accumulator;
            }

            return accumulator;
        }

        foreach (var invoice in invoices)
        {
            if (invoice.InvoiceDate < from || invoice.InvoiceDate > to)
            {
                continue;
            }

            var accumulator = Bucket(invoice.InvoiceDate.Year, invoice.InvoiceDate.Month);
            accumulator.EfaturaBase += invoice.TaxBase;
            accumulator.EfaturaVat += invoice.TaxAmount;
            accumulator.EfaturaCount++;
        }

        foreach (var entry in entries)
        {
            if (entry.EntryDate < from || entry.EntryDate > to)
            {
                continue;
            }

            var accumulator = Bucket(entry.EntryDate.Year, entry.EntryDate.Month);
            accumulator.AccountingBase += entry.PurchaseBase;
            accumulator.AccountingVat += entry.DeductibleVat;
            accumulator.AccountingEntryCount++;
        }

        return buckets
            .Select(pair => new CrossCheckPurchaseMonth(
                pair.Key.Year,
                pair.Key.Month,
                decimal.Round(pair.Value.EfaturaBase, 2),
                decimal.Round(pair.Value.EfaturaVat, 2),
                pair.Value.EfaturaCount,
                decimal.Round(pair.Value.AccountingBase, 2),
                decimal.Round(pair.Value.AccountingVat, 2),
                pair.Value.AccountingEntryCount))
            .ToList();
    }

    /// <summary>Fica só com letras e dígitos, em maiúsculas — tolera espaços, barras, pontos e prefixos de série.</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToUpperInvariant(character);
            }
        }

        return new string(buffer[..length]);
    }

    private static bool KeysMatch(string invoiceKey, string entryKey) =>
        entryKey.Contains(invoiceKey, StringComparison.Ordinal) ||
        invoiceKey.Contains(entryKey, StringComparison.Ordinal);

    private sealed class MonthAccumulator
    {
        public decimal EfaturaBase;
        public decimal EfaturaVat;
        public int EfaturaCount;
        public decimal AccountingBase;
        public decimal AccountingVat;
        public int AccountingEntryCount;
    }
}
