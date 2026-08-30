using System.Data;
using AccountingAI.Core.Localization;
using AccountingAI.Core.Models;

namespace AccountingAI.WinForms.Controls;

internal sealed class RecordGridPage : UserControl
{
    private readonly DataGridView _grid;
    private readonly DataGridView _details;
    private readonly TextBox _searchBox;
    private readonly Label _countLabel;
    private readonly DataTable _table;

    public RecordGridPage(
        RecordTable? records,
        string emptyMessage = "Sem registos neste conjunto de ficheiros.",
        GridLayoutManager? layouts = null,
        string gridKey = "")
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;
        Padding = new Padding(10);

        _table = records?.ToDataTable() ?? new DataTable();
        _grid = UiTheme.CreateGrid();
        _grid.DataSource = _table;
        _grid.SelectionChanged += (_, _) => ShowSelectedRow();
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        if (layouts is not null)
        {
            layouts.Attach(_grid, $"{gridKey}.registos");
        }

        _details = UiTheme.CreateGrid();
        _details.AllowUserToOrderColumns = false;
        _details.ColumnHeadersVisible = false;
        _details.Columns.Add("Campo", "Campo");
        _details.Columns.Add("Valor", "Valor");
        _details.Columns[0].Width = 260;
        _details.Columns[1].Width = 600;
        if (layouts is not null)
        {
            layouts.Attach(_details, $"{gridKey}.detalhe");
        }

        _searchBox = new TextBox
        {
            AccessibleName = "Pesquisar em todos os campos",
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Pesquisar em todos os campos…",
            Width = 360
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();

        _countLabel = new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(14, 7, 0, 0),
            Text = FormatCount(_table.Rows.Count)
        };

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        toolbar.Controls.Add(_searchBox);
        toolbar.Controls.Add(_countLabel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Border
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 66F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
        layout.Controls.Add(_grid, 0, 0);
        layout.Controls.Add(_details, 0, 1);

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        content.Controls.Add(layout);
        content.Controls.Add(toolbar);
        Controls.Add(content);

        if (_table.Rows.Count == 0)
        {
            _searchBox.Enabled = false;
            _details.Rows.Add("Informação", emptyMessage);
        }
    }

    private void ApplyFilter()
    {
        var search = _searchBox.Text.Trim();
        if (search.Length == 0)
        {
            _table.DefaultView.RowFilter = string.Empty;
        }
        else
        {
            var escapedValue = search.Replace("'", "''", StringComparison.Ordinal)
                .Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("*", "[*]", StringComparison.Ordinal);
            var filters = _table.Columns.Cast<DataColumn>()
                .Select(column => $"Convert([{column.ColumnName.Replace("]", "]]", StringComparison.Ordinal)}], 'System.String') LIKE '%{escapedValue}%'");
            _table.DefaultView.RowFilter = string.Join(" OR ", filters);
        }

        _countLabel.Text = FormatCount(_table.DefaultView.Count);
    }

    private void ShowSelectedRow()
    {
        _details.Rows.Clear();
        if (_grid.CurrentRow?.DataBoundItem is not DataRowView row)
        {
            return;
        }

        foreach (DataColumn column in _table.Columns)
        {
            var value = Convert.ToString(row.Row[column]) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _details.Rows.Add(SaftFieldLabels.Get(column.ColumnName), value);
            }
        }
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
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

    private static string FormatCount(int count) => count == 1 ? "1 registo" : $"{count:N0} registos";
}
