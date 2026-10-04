using System.Data;
using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.WinForms.Controls;

internal sealed class SummaryPage : UserControl
{
    public SummaryPage(SaftDataSet data, GridLayoutManager? layouts = null)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = UiTheme.Canvas;
        Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 14),
            Text = "Visão geral"
        };
        layout.Controls.Add(title);

        var cards = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 16)
        };
        cards.Controls.Add(CreateCard("Ficheiros", data.SourceFiles.Count));
        cards.Controls.Add(CreateCard("Faturas", data.Count(SaftTableKeys.Invoices)));
        cards.Controls.Add(CreateCard("Linhas", data.Count(SaftTableKeys.InvoiceLines)));
        cards.Controls.Add(CreateCard("Clientes", data.Count(SaftTableKeys.Customers)));
        cards.Controls.Add(CreateCard("Produtos", data.Count(SaftTableKeys.Products)));
        cards.Controls.Add(CreateCard("Mov. contabilísticos", data.Count(SaftTableKeys.Transactions)));
        layout.Controls.Add(cards);

        var filesLabel = SectionLabel("Ficheiros carregados");
        layout.Controls.Add(filesLabel);

        var filesGrid = UiTheme.CreateGrid();
        filesGrid.Height = Math.Max(150, Math.Min(340, 42 + (data.SourceFiles.Count * 29)));
        filesGrid.DataSource = BuildFilesTable(data);
        layouts?.Attach(filesGrid, "resumo.ficheiros");
        filesGrid.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(filesGrid);

        Controls.Add(layout);
    }

    private static Control CreateCard(string caption, int value)
    {
        var panel = new Panel
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 12, 12),
            Padding = new Padding(14),
            Size = new Size(180, 88)
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = UiTheme.Primary,
            Location = new Point(12, 10),
            Text = value.ToString("N0")
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = UiTheme.Muted,
            Location = new Point(14, 52),
            Text = caption
        });
        return panel;
    }

    private static Label SectionLabel(string text) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 12F),
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 4, 0, 4),
        Text = text
    };

    private static DataTable BuildFilesTable(SaftDataSet data)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.GetCultureInfo("pt-PT") };
        table.Columns.Add("Ficheiro");
        table.Columns.Add("Versão");
        table.Columns.Add("Tipo");
        table.Columns.Add("Empresa");
        table.Columns.Add("Período");
        table.Columns.Add("Conteúdo detetado");
        table.Columns.Add("Tamanho");

        foreach (var file in data.SourceFiles)
        {
            table.Rows.Add(
                file.FileName,
                file.Version,
                file.TaxAccountingBasis,
                file.CompanyName,
                $"{file.StartDate} a {file.EndDate}",
                file.DetectedContent,
                FormatSize(file.Size));
        }

        return table;
    }

    private static string FormatSize(long size) => size >= 1024 * 1024
        ? $"{size / 1024d / 1024d:N1} MB"
        : $"{size / 1024d:N0} KB";
}
