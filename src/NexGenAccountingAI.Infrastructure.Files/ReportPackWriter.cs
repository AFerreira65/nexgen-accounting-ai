using System.Globalization;
using NexGenAccountingAI.Application.Maps;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// O PDF de relato (F4B-04): uma capa, as secções pela ordem pedida — texto em
/// parágrafos, tabelas com os níveis e os negritos dos mapas — e o rodapé com a
/// página. Serve ao relatório para a banca e a qualquer relato composto de mapas.
/// </summary>
public static class ReportPackWriter
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");
    private static readonly Color Ink = new(29, 47, 55);
    private static readonly Color Muted = new(91, 107, 114);
    private static readonly Color Accent = new(22, 115, 106);
    private static readonly Color HeaderFill = new(232, 240, 242);
    private static readonly Color Rule = new(222, 229, 232);

    /// <summary>Acima disto uma tabela do relato não cabe num PDF útil.</summary>
    public const int TableRowLimit = 3000;

    /// <summary>A partir de quantas colunas uma tabela vai numa página ao alto.</summary>
    public const int WideColumns = 8;

    public static byte[] ToPdf(ReportPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var tooLong = pack.Sections.FirstOrDefault(section => section.Table is { } table && table.Rows.Count > TableRowLimit);
        if (tooLong is not null)
        {
            throw new ArgumentException($"A secção «{tooLong.Title}» tem {tooLong.Table!.Rows.Count} linhas; um relato leva no máximo {TableRowLimit} por tabela.", nameof(pack));
        }

        EnsureFonts();
        var document = new Document();
        document.Info.Title = pack.Title;
        document.Info.Author = pack.CompanyName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;

        Cover(document, pack);

        // Uma tabela larga (o mapa mensal) vai numa secção ao alto; as outras ao baixo.
        var body = Body(document, pack, landscape: false);
        var number = 0;
        foreach (var section in pack.Sections)
        {
            number++;
            var wide = section.Table is { } candidate && candidate.Header.Count > WideColumns;
            if (wide != (body.PageSetup.Orientation == Orientation.Landscape))
            {
                body = Body(document, pack, wide);
            }

            var heading = body.AddParagraph($"{number}. {section.Title}");
            heading.Format.Font.Size = 14;
            heading.Format.Font.Bold = true;
            heading.Format.Font.Color = Accent;
            heading.Format.SpaceBefore = Unit.FromPoint(number == 1 || body.Elements.Count == 1 ? 0 : 18);
            heading.Format.SpaceAfter = Unit.FromPoint(8);
            heading.Format.KeepWithNext = true;

            foreach (var text in section.Paragraphs.Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                var paragraph = body.AddParagraph(text.Trim());
                paragraph.Format.Alignment = ParagraphAlignment.Justify;
                paragraph.Format.LineSpacingRule = LineSpacingRule.Multiple;
                paragraph.Format.LineSpacing = 1.25;
                paragraph.Format.SpaceAfter = Unit.FromPoint(6);
            }

            if (section.Table is { } table)
            {
                Table(body, table, wide);
            }

            foreach (var note in section.Notes ?? [])
            {
                var paragraph = body.AddParagraph(note);
                paragraph.Format.Font.Size = 7.5;
                paragraph.Format.Font.Color = Muted;
                paragraph.Format.SpaceBefore = Unit.FromPoint(4);
            }
        }

        if (!string.IsNullOrWhiteSpace(pack.Notice))
        {
            var notice = body.AddParagraph(pack.Notice);
            notice.Format.Font.Size = 7.5;
            notice.Format.Font.Color = Muted;
            notice.Format.SpaceBefore = Unit.FromPoint(18);
            notice.Format.Borders.Top.Width = 0.5;
            notice.Format.Borders.Top.Color = Rule;
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static void Cover(Document document, ReportPack pack)
    {
        var cover = document.AddSection();
        cover.PageSetup = PageSetup(document);
        cover.PageSetup.TopMargin = Unit.FromCentimeter(7);

        var band = cover.AddParagraph(pack.CompanyName.ToUpperInvariant());
        band.Format.Font.Size = 10;
        band.Format.Font.Bold = true;
        band.Format.Font.Color = Accent;
        band.Format.SpaceAfter = Unit.FromPoint(24);
        band.Format.Borders.Bottom.Width = 1.5;
        band.Format.Borders.Bottom.Color = Accent;
        band.Format.Borders.DistanceFromBottom = Unit.FromPoint(6);

        var title = cover.AddParagraph(pack.Title);
        title.Format.Font.Size = 26;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(6);
        if (!string.IsNullOrWhiteSpace(pack.Subtitle))
        {
            var subtitle = cover.AddParagraph(pack.Subtitle);
            subtitle.Format.Font.Size = 13;
            subtitle.Format.Font.Color = Muted;
        }

        var period = cover.AddParagraph(pack.Period);
        period.Format.Font.Size = 13;
        period.Format.SpaceBefore = Unit.FromPoint(18);

        var details = cover.AddParagraph();
        details.Format.SpaceBefore = Unit.FromCentimeter(6);
        details.Format.Font.Size = 9.5;
        details.Format.Font.Color = Muted;
        details.AddText($"{pack.CompanyName} · NIF {pack.CompanyTaxId}");
        if (!string.IsNullOrWhiteSpace(pack.Preparer))
        {
            details.AddLineBreak();
            details.AddText($"Preparado por {pack.Preparer}");
        }

        details.AddLineBreak();
        details.AddText($"Emitido em {pack.GeneratedAt.ToLocalTime().ToString("d 'de' MMMM 'de' yyyy", Portuguese)}");

        var index = cover.AddParagraph("Conteúdo");
        index.Format.Font.Size = 9;
        index.Format.Font.Bold = true;
        index.Format.SpaceBefore = Unit.FromPoint(24);
        index.Format.SpaceAfter = Unit.FromPoint(4);
        var number = 0;
        foreach (var section in pack.Sections)
        {
            var line = cover.AddParagraph($"{++number}. {section.Title}");
            line.Format.Font.Size = 9;
            line.Format.LeftIndent = Unit.FromPoint(8);
        }
    }

    private static Section Body(Document document, ReportPack pack, bool landscape)
    {
        var body = document.AddSection();
        body.PageSetup = PageSetup(document);
        // O clone traz a largura e a altura do A4 ao baixo já fixadas; a orientação só por si não as troca.
        body.PageSetup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        body.PageSetup.PageWidth = Unit.FromCentimeter(landscape ? 29.7 : 21.0);
        body.PageSetup.PageHeight = Unit.FromCentimeter(landscape ? 21.0 : 29.7);
        var footer = body.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Muted;
        footer.AddText($"{pack.CompanyName} · {pack.Title} · {pack.Period} · Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();
        return body;
    }

    private static PageSetup PageSetup(Document document)
    {
        var setup = document.DefaultPageSetup.Clone();
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = Orientation.Portrait;
        setup.LeftMargin = Unit.FromCentimeter(2);
        setup.RightMargin = Unit.FromCentimeter(2);
        setup.TopMargin = Unit.FromCentimeter(2);
        setup.BottomMargin = Unit.FromCentimeter(2);
        return setup;
    }

    private static void Table(Section body, ReportPackTable table, bool landscape)
    {
        var columns = table.Header.Count;
        if (columns == 0)
        {
            return;
        }

        // Ponto de partida igual ao das listagens: os números levam a largura do maior
        // valor; as colunas de texto repartem o resto. Mais de seis colunas, letra menor.
        double fontSize = columns switch { <= 4 => 9, <= 7 => 8, <= 10 => 7, _ => 6 };
        var usable = (landscape ? 29.7 : 21.0) - 4.0;
        var longest = new int[columns];
        for (var column = 0; column < columns; column++)
        {
            longest[column] = table.Header[column].Length;
            foreach (var row in table.Rows)
            {
                if (column < row.Cells.Count)
                {
                    longest[column] = Math.Max(longest[column], Math.Min(row.Cells[column].Length + (column == 0 ? row.Level * 2 : 0), 60));
                }
            }
        }

        double NumberWidth(int column) => ((longest[column] * fontSize * 0.56) + 8) / 28.35;
        var textColumns = Enumerable.Range(0, columns).Where(column => !table.Numeric[column]).ToList();
        var numbersWidth = Enumerable.Range(0, columns).Where(column => table.Numeric[column]).Sum(NumberWidth);
        // Se os números e um mínimo para o texto não couberem, a letra encolhe até caber.
        while (numbersWidth + (textColumns.Count * 2.2) > usable && fontSize > 4.5)
        {
            fontSize -= 0.5;
            numbersWidth = Enumerable.Range(0, columns).Where(column => table.Numeric[column]).Sum(NumberWidth);
        }

        var textWeight = Math.Max(1, textColumns.Sum(column => Math.Clamp(longest[column], 6, 60)));
        var textWidth = Math.Max(usable - numbersWidth, textColumns.Count * 2.2);

        var grid = body.AddTable();
        grid.Format.Font.Size = fontSize;
        grid.Borders.Bottom.Width = 0.25;
        grid.Borders.Bottom.Color = Rule;
        grid.LeftPadding = Unit.FromPoint(3);
        grid.RightPadding = Unit.FromPoint(3);
        grid.TopPadding = Unit.FromPoint(2.5);
        grid.BottomPadding = Unit.FromPoint(2.5);
        for (var column = 0; column < columns; column++)
        {
            var width = table.Numeric[column] ? NumberWidth(column) : textWidth * Math.Clamp(longest[column], 6, 60) / textWeight;
            var gridColumn = grid.AddColumn(Unit.FromCentimeter(width));
            gridColumn.Format.Alignment = table.Numeric[column] ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = grid.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = HeaderFill;
        for (var column = 0; column < columns; column++)
        {
            header.Cells[column].AddParagraph(table.Header[column]);
        }

        foreach (var row in table.Rows)
        {
            var gridRow = grid.AddRow();
            if (row.Bold || row.IsTitle)
            {
                gridRow.Format.Font.Bold = true;
            }

            if (row.IsTitle)
            {
                gridRow.Shading.Color = new Color(245, 248, 249);
                gridRow.Format.Font.Color = Muted;
            }

            for (var column = 0; column < Math.Min(columns, row.Cells.Count); column++)
            {
                var text = row.Cells[column];
                if (text.Length == 0)
                {
                    continue;
                }

                var paragraph = gridRow.Cells[column].AddParagraph(text);
                if (column == 0 && row.Level > 0)
                {
                    paragraph.Format.LeftIndent = Unit.FromPoint(row.Level * 9);
                }
            }
        }

        var after = body.AddParagraph();
        after.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static readonly Lazy<bool> Fonts = new(() =>
    {
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
}
