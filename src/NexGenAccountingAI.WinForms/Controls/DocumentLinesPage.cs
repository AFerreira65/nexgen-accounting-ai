using System.Data;
using NexGenAccountingAI.Core.Localization;
using NexGenAccountingAI.Core.Models;

namespace NexGenAccountingAI.WinForms.Controls;

internal sealed class DocumentLinesPage : UserControl
{
    private readonly DataTable _documents;
    private readonly DataTable _lines;
    private readonly DataGridView _documentGrid;
    private readonly DataGridView _lineGrid;
    private readonly DataGridView _detailsGrid;
    private readonly TextBox _searchBox;
    private readonly Label _documentCount;
    private readonly Label _lineCount;
    private readonly string _documentKey;

    public DocumentLinesPage(
        RecordTable? documents,
        RecordTable? lines,
        string documentKey,
        GridLayoutManager? layouts = null,
        string gridKey = "")
    {
        _documentKey = documentKey;
        _documents = documents?.ToDataTable() ?? new DataTable();
        _lines = lines?.ToDataTable() ?? new DataTable();

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;
        Padding = new Padding(10);

        _documentGrid = UiTheme.CreateGrid();
        _documentGrid.DataSource = _documents;
        _documentGrid.SelectionChanged += (_, _) => SelectDocument();
        _documentGrid.DataBindingComplete += (_, _) => ConfigureColumns(_documentGrid);
        if (layouts is not null)
        {
            layouts.Attach(_documentGrid, $"{gridKey}.documentos");
        }

        _lineGrid = UiTheme.CreateGrid();
        _lineGrid.DataSource = _lines.DefaultView;
        _lineGrid.DataBindingComplete += (_, _) => ConfigureColumns(_lineGrid);
        if (layouts is not null)
        {
            layouts.Attach(_lineGrid, $"{gridKey}.linhas");
        }

        _detailsGrid = UiTheme.CreateGrid();
        _detailsGrid.AllowUserToOrderColumns = false;
        _detailsGrid.ColumnHeadersVisible = false;
        _detailsGrid.Columns.Add("Campo", "Campo");
        _detailsGrid.Columns.Add("Valor", "Valor");
        _detailsGrid.Columns[0].Width = 260;
        _detailsGrid.Columns[1].Width = 600;
        if (layouts is not null)
        {
            layouts.Attach(_detailsGrid, $"{gridKey}.detalhe");
        }

        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Pesquisar documentos…",
            Width = 360
        };
        _searchBox.TextChanged += (_, _) => FilterDocuments();

        _documentCount = CountLabel();
        _lineCount = CountLabel();

        var documentToolbar = Toolbar(_searchBox, _documentCount);
        var documentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        documentPanel.Controls.Add(_documentGrid);
        documentPanel.Controls.Add(documentToolbar);

        var detailTabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        var linesTab = new TabPage("Linhas do documento") { BackColor = Color.White };
        var linesPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        linesPanel.Controls.Add(_lineGrid);
        linesPanel.Controls.Add(Toolbar(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            ForeColor = UiTheme.Text,
            Margin = new Padding(3, 7, 10, 0),
            Text = "Documento selecionado"
        }, _lineCount));
        linesTab.Controls.Add(linesPanel);
        detailTabs.TabPages.Add(linesTab);

        var fieldsTab = new TabPage("Campos do documento") { BackColor = Color.White };
        fieldsTab.Controls.Add(_detailsGrid);
        detailTabs.TabPages.Add(fieldsTab);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Border
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
        layout.Controls.Add(documentPanel, 0, 0);
        layout.Controls.Add(detailTabs, 0, 1);
        Controls.Add(layout);

        _documentCount.Text = FormatCount(_documents.Rows.Count, "documento");
        if (_documents.Rows.Count == 0)
        {
            _searchBox.Enabled = false;
            _detailsGrid.Rows.Add("Informação", "Sem documentos nesta secção.");
        }
        else
        {
            SelectDocument();
        }
    }

    private void SelectDocument()
    {
        _detailsGrid.Rows.Clear();
        if (_documentGrid.CurrentRow?.DataBoundItem is not DataRowView selected)
        {
            _lines.DefaultView.RowFilter = "1 = 0";
            _lineCount.Text = FormatCount(0, "linha");
            return;
        }

        foreach (DataColumn column in _documents.Columns)
        {
            var value = Convert.ToString(selected.Row[column]) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _detailsGrid.Rows.Add(SaftFieldLabels.Get(column.ColumnName), value);
            }
        }

        if (!_lines.Columns.Contains(_documentKey) || !_lines.Columns.Contains("Ficheiro"))
        {
            _lines.DefaultView.RowFilter = "1 = 0";
            _lineCount.Text = FormatCount(0, "linha");
            return;
        }

        var keyValue = Convert.ToString(selected.Row[_documentKey]) ?? string.Empty;
        var sourceFile = Convert.ToString(selected.Row["Ficheiro"]) ?? string.Empty;
        _lines.DefaultView.RowFilter =
            $"[{EscapeColumn(_documentKey)}] = '{EscapeValue(keyValue)}' AND [Ficheiro] = '{EscapeValue(sourceFile)}'";
        _lineCount.Text = FormatCount(_lines.DefaultView.Count, "linha");
    }

    private void FilterDocuments()
    {
        var search = _searchBox.Text.Trim();
        if (search.Length == 0)
        {
            _documents.DefaultView.RowFilter = string.Empty;
        }
        else
        {
            var value = EscapeLikeValue(search);
            _documents.DefaultView.RowFilter = string.Join(" OR ", _documents.Columns.Cast<DataColumn>()
                .Select(column => $"Convert([{EscapeColumn(column.ColumnName)}], 'System.String') LIKE '%{value}%'"));
        }

        _documentCount.Text = FormatCount(_documents.DefaultView.Count, "documento");
        SelectDocument();
    }

    private static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        toolbar.Controls.AddRange(controls);
        return toolbar;
    }

    private static Label CountLabel() => new()
    {
        AutoSize = true,
        ForeColor = UiTheme.Muted,
        Font = new Font("Segoe UI", 9F),
        Margin = new Padding(14, 7, 0, 0)
    };

    private static string EscapeColumn(string value) => value.Replace("]", "]]", StringComparison.Ordinal);

    private static string EscapeValue(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeLikeValue(string value) => EscapeValue(value)
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("*", "[*]", StringComparison.Ordinal);

    private static void ConfigureColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var fieldName = column.DataPropertyName;
            column.HeaderText = SaftFieldLabels.Get(fieldName);
            column.MinimumWidth = 90;
            if (fieldName.Contains("Amount", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Price", StringComparison.OrdinalIgnoreCase))
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }
    }

    private static string FormatCount(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count:N0} {singular}s";
}
