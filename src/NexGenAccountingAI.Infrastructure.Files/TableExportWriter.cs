using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Uma listagem a exportar: o título, a empresa, o cabeçalho das colunas e as linhas.
/// Cada célula é texto, número (<see cref="decimal"/>) ou vazia.
/// </summary>
public sealed record TableExport(
    string Title,
    string CompanyName,
    string CompanyTaxId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> Header,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    string? Subtitle = null);

/// <summary>
/// Gera Excel (XLSX) e PDF de qualquer listagem da aplicação (F4-04), no servidor: os
/// números ficam números no Excel, o PDF repete o cabeçalho em cada página e numera as
/// páginas. Antes só havia CSV e o «imprimir» do browser.
/// </summary>
public static partial class TableExportWriter
{
    /// <summary>Acima disto um PDF deixa de ser útil e demora demasiado; para volumes grandes, Excel.</summary>
    public const int PdfRowLimit = 5000;

    public const int XlsxRowLimit = 300_000;

    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");
    private const int HeaderRowNumber = 5;

    public static byte[] ToXlsx(TableExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (export.Rows.Count > XlsxRowLimit)
        {
            throw new ArgumentException($"A listagem tem {export.Rows.Count} linhas; o limite de uma exportação para Excel é {XlsxRowLimit}.", nameof(export));
        }

        var columns = ColumnCount(export);
        var numeric = NumericColumns(export, columns);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(export.Title));

        sheet.Cell(1, 1).SetValue(export.CompanyName).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(2, 1).SetValue($"NIF {export.CompanyTaxId}");
        sheet.Cell(3, 1).SetValue(export.Title).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(4, 1).SetValue(GeneratedLine(export)).Style.Font.SetFontColor(XLColor.FromHtml("#5b6b72"));

        var widths = new int[columns];
        for (var column = 0; column < columns; column++)
        {
            var title = column < export.Header.Count ? export.Header[column] : string.Empty;
            // SetValue com texto nunca cria uma fórmula, mesmo que comece por «=».
            sheet.Cell(HeaderRowNumber, column + 1).SetValue(title);
            widths[column] = title.Length;
        }

        var header = sheet.Range(HeaderRowNumber, 1, HeaderRowNumber, columns);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#e8f0f2")).Border.SetBottomBorder(XLBorderStyleValues.Thin);

        for (var index = 0; index < export.Rows.Count; index++)
        {
            var row = export.Rows[index];
            var rowNumber = HeaderRowNumber + 1 + index;
            for (var column = 0; column < Math.Min(columns, row.Count); column++)
            {
                var cell = sheet.Cell(rowNumber, column + 1);
                switch (row[column])
                {
                    case decimal number:
                        cell.SetValue(number);
                        widths[column] = Math.Max(widths[column], 14);
                        break;
                    case null:
                        break;
                    case var other:
                        var text = other.ToString() ?? string.Empty;
                        cell.SetValue(text);
                        widths[column] = Math.Max(widths[column], Math.Min(text.Length, 70));
                        break;
                }
            }

            if (IsTotalRow(row))
            {
                sheet.Range(rowNumber, 1, rowNumber, columns).Style.Font.SetBold();
            }
        }

        for (var column = 0; column < columns; column++)
        {
            var sheetColumn = sheet.Column(column + 1);
            sheetColumn.Width = Math.Clamp(widths[column] + 2, 8, 72);
            if (numeric[column])
            {
                sheetColumn.Style.NumberFormat.Format = "#,##0.00";
                sheet.Cell(HeaderRowNumber, column + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
        }

        if (export.Rows.Count > 0)
        {
            sheet.Range(HeaderRowNumber, 1, HeaderRowNumber + export.Rows.Count, columns).SetAutoFilter();
        }

        sheet.SheetView.FreezeRows(HeaderRowNumber);
        sheet.PageSetup.PageOrientation = columns > 6 ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(HeaderRowNumber, HeaderRowNumber);
        sheet.PageSetup.Footer.Center.AddText("Página ");
        sheet.PageSetup.Footer.Center.AddText(XLHFPredefinedText.PageNumber);
        sheet.PageSetup.Footer.Center.AddText(" de ");
        sheet.PageSetup.Footer.Center.AddText(XLHFPredefinedText.NumberOfPages);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] ToPdf(TableExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (export.Rows.Count > PdfRowLimit)
        {
            throw new ArgumentException($"A listagem tem {export.Rows.Count} linhas; um PDF leva no máximo {PdfRowLimit}. Exporte para Excel ou estreite os filtros.", nameof(export));
        }

        EnsureFonts();
        var columns = ColumnCount(export);
        var numeric = NumericColumns(export, columns);
        var landscape = columns > 6;
        var fontSize = columns switch { <= 6 => 9, <= 9 => 8, <= 12 => 7, _ => 6 };

        var document = new Document();
        document.Info.Title = export.Title;
        document.Info.Author = export.CompanyName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = fontSize;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);

        var company = section.AddParagraph();
        company.AddFormattedText(export.CompanyName, TextFormat.Bold).Font.Size = 11;
        company.AddText($"   NIF {export.CompanyTaxId}");
        var title = section.AddParagraph(export.Title);
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromPoint(4);
        var generated = section.AddParagraph(GeneratedLine(export));
        generated.Format.Font.Color = new Color(91, 107, 114);
        generated.Format.SpaceAfter = Unit.FromPoint(8);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7;
        footer.AddText($"{export.CompanyName} · {export.Title} · Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();

        // Largura das colunas. As de números levam a largura do maior valor já formatado
        // (senão os valores grandes encostam uns aos outros); as de texto repartem o resto
        // conforme o texto mais comprido. Se os números não couberem, a letra encolhe.
        var usable = (landscape ? 29.7 : 21.0) - 3.0;
        var longestText = new int[columns];
        var longestNumber = new int[columns];
        for (var column = 0; column < columns; column++)
        {
            longestText[column] = column < export.Header.Count ? export.Header[column].Length : 0;
            foreach (var row in export.Rows)
            {
                if (column >= row.Count)
                {
                    continue;
                }

                if (row[column] is decimal number)
                {
                    longestNumber[column] = Math.Max(longestNumber[column], number.ToString("N2", Portuguese).Length);
                }
                else if (row[column] is { } value)
                {
                    longestText[column] = Math.Max(longestText[column], Math.Min(value.ToString()?.Length ?? 0, 45));
                }
            }
        }

        double[] widths;
        while (true)
        {
            // Um algarismo em Arial ocupa cerca de 0,56 do corpo da letra; 8 pontos de margens.
            double NumberWidth(int column) =>
                ((Math.Max(longestNumber[column], longestText[column]) * fontSize * 0.56) + 8) / 28.35;
            var numbersWidth = Enumerable.Range(0, columns).Where(column => numeric[column]).Sum(NumberWidth);
            var textColumns = Enumerable.Range(0, columns).Where(column => !numeric[column]).ToList();
            var minimumText = textColumns.Count * 1.4;
            if (numbersWidth + minimumText > usable && fontSize > 5)
            {
                fontSize--;
                continue;
            }

            var textWeight = textColumns.Sum(column => (double)Math.Clamp(longestText[column], 5, 45));
            var textWidth = Math.Max(usable - numbersWidth, minimumText);
            widths = Enumerable.Range(0, columns)
                .Select(column => numeric[column]
                    ? NumberWidth(column)
                    : textWidth * Math.Clamp(longestText[column], 5, 45) / textWeight)
                .ToArray();
            break;
        }

        normal.Font.Size = fontSize;
        var table = section.AddTable();
        table.Borders.Bottom.Width = 0.25;
        table.Borders.Bottom.Color = new Color(222, 229, 232);
        table.LeftPadding = Unit.FromPoint(3);
        table.RightPadding = Unit.FromPoint(3);
        for (var column = 0; column < columns; column++)
        {
            var tableColumn = table.AddColumn(Unit.FromCentimeter(widths[column]));
            tableColumn.Format.Alignment = numeric[column] ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = new Color(232, 240, 242);
        for (var column = 0; column < columns; column++)
        {
            headerRow.Cells[column].AddParagraph(column < export.Header.Count ? export.Header[column] : string.Empty);
        }

        foreach (var row in export.Rows)
        {
            var tableRow = table.AddRow();
            if (IsTotalRow(row))
            {
                tableRow.Format.Font.Bold = true;
                tableRow.Shading.Color = new Color(245, 248, 249);
            }

            for (var column = 0; column < Math.Min(columns, row.Count); column++)
            {
                var text = row[column] switch
                {
                    decimal number => number.ToString("N2", Portuguese),
                    null => string.Empty,
                    var other => other.ToString() ?? string.Empty,
                };
                if (text.Length > 0)
                {
                    tableRow.Cells[column].AddParagraph(text);
                }
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static int ColumnCount(TableExport export) =>
        Math.Max(1, Math.Max(export.Header.Count, export.Rows.Count == 0 ? 0 : export.Rows.Max(row => row.Count)));

    /// <summary>Uma coluna é de números quando todas as suas células preenchidas o são (e tem alguma).</summary>
    private static bool[] NumericColumns(TableExport export, int columns)
    {
        var result = new bool[columns];
        for (var column = 0; column < columns; column++)
        {
            var any = false;
            var all = true;
            foreach (var row in export.Rows)
            {
                if (column >= row.Count || row[column] is null || (row[column] is string text && text.Length == 0))
                {
                    continue;
                }

                if (row[column] is decimal)
                {
                    any = true;
                }
                else
                {
                    all = false;
                    break;
                }
            }

            result[column] = any && all;
        }

        return result;
    }

    /// <summary>As linhas de subtotal e de total saem a negrito: reconhecem-se pelo primeiro texto da linha.</summary>
    private static bool IsTotalRow(IReadOnlyList<object?> row)
    {
        var first = row.OfType<string>().FirstOrDefault(text => text.Length > 0);
        return first is not null && TotalRowPattern().IsMatch(first);
    }

    private static string GeneratedLine(TableExport export) =>
        (string.IsNullOrWhiteSpace(export.Subtitle) ? string.Empty : export.Subtitle.Trim() + " · ") +
        $"Gerado em {export.GeneratedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Portuguese)}";

    private static string SheetName(string title)
    {
        var clean = InvalidSheetNameCharacters().Replace(title, " ").Trim();
        return clean.Length == 0 ? "Dados" : clean[..Math.Min(clean.Length, 31)];
    }

    private static readonly Lazy<bool> Fonts = new(() =>
    {
        // O PDFsharp «core» não lê as fontes do sistema sem isto; no Windows usa as do
        // Windows. Só se pode definir uma vez por processo, e ler o valor também o fixa.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            }
            catch (InvalidOperationException)
            {
                // Já foi definido por outro código do processo.
            }
        }

        return true;
    });

    private static void EnsureFonts() => _ = Fonts.Value;

    [GeneratedRegex(@"^\s*(sub-?total|total)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalRowPattern();

    [GeneratedRegex(@"[\[\]\*\?/\\:]")]
    private static partial Regex InvalidSheetNameCharacters();
}
