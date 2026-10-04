using NexGenAccountingAI.Core.Configuration;
using NexGenAccountingAI.Core.Phc;

namespace NexGenAccountingAI.WinForms.Dialogs;

internal sealed class PhcSeriesMappingsDialog : Form
{
    private readonly DataGridView _invoiceGrid;
    private readonly DataGridView _receiptGrid;
    private readonly IReadOnlyList<SaftDocumentSeries> _invoiceSeries;
    private readonly IReadOnlyList<SaftDocumentSeries> _receiptSeries;
    private readonly IReadOnlyList<PhcDocumentType> _invoiceDocuments;
    private readonly IReadOnlyList<PhcDocumentType> _receiptDocuments;

    public PhcSeriesMappingsDialog(
        string companyName,
        IEnumerable<SaftDocumentSeries> invoiceSeries,
        IEnumerable<SaftDocumentSeries> receiptSeries,
        PhcDocumentCatalog invoiceCatalog,
        PhcDocumentCatalog receiptCatalog,
        IEnumerable<PhcSeriesMapping> invoiceMappings,
        IEnumerable<PhcSeriesMapping> receiptMappings,
        GridLayoutManager? layouts = null)
    {
        var savedInvoiceMappings = invoiceMappings.Select(mapping => mapping.Clone()).ToList();
        var savedReceiptMappings = receiptMappings.Select(mapping => mapping.Clone()).ToList();
        _invoiceSeries = MergeSeries(invoiceSeries, savedInvoiceMappings);
        _receiptSeries = MergeSeries(receiptSeries, savedReceiptMappings);
        _invoiceDocuments = MergeDocuments(invoiceCatalog.Documents, savedInvoiceMappings, invoiceCatalog.SourceTable);
        _receiptDocuments = MergeDocuments(receiptCatalog.Documents, savedReceiptMappings, receiptCatalog.SourceTable);

        Text = $"Séries SAF-T e documentos PHC — {companyName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 620);
        ClientSize = new Size(1120, 740);
        Font = new Font("Segoe UI", 9F);

        var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(16, 10, 16, 0) };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = UiTheme.Primary,
            Location = new Point(16, 8),
            Text = "Correspondência de séries SAF-T para o PHC"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 46),
            Text = "Cada série deve apontar para o documento PHC que será usado numa futura importação. Esta janela não grava documentos no ERP."
        });

        _invoiceGrid = BuildGrid(_invoiceSeries, _invoiceDocuments);
        _receiptGrid = BuildGrid(_receiptSeries, _receiptDocuments);
        layouts?.Attach(_invoiceGrid, "configuracao.series-faturacao");
        layouts?.Attach(_receiptGrid, "configuracao.series-recebimentos");

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildPage(
            "Faturação",
            $"Séries encontradas em SalesInvoices. Documentos PHC lidos de {invoiceCatalog.SourceTable}.",
            _invoiceGrid,
            _invoiceSeries,
            _invoiceDocuments));
        tabs.TabPages.Add(BuildPage(
            "Recebimentos",
            $"Séries encontradas em Payments. Documentos PHC lidos de {receiptCatalog.SourceTable}.",
            _receiptGrid,
            _receiptSeries,
            _receiptDocuments));

        PopulateGrid(_invoiceGrid, _invoiceSeries, _invoiceDocuments, savedInvoiceMappings);
        PopulateGrid(_receiptGrid, _receiptSeries, _receiptDocuments, savedReceiptMappings);

        var buttons = new FlowLayoutPanel
        {
            BackColor = Color.White,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 64,
            Padding = new Padding(12),
            WrapContents = false
        };
        var save = new Button
        {
            AutoSize = true,
            BackColor = UiTheme.Primary,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Padding = new Padding(16, 6, 16, 6),
            Text = "Guardar correspondências"
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) => Confirm();
        var cancel = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Padding = new Padding(12, 6, 12, 6),
            Text = "Cancelar"
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var shell = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        header.Dock = DockStyle.Fill;
        buttons.Dock = DockStyle.Fill;
        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(tabs, 0, 1);
        shell.Controls.Add(buttons, 0, 2);
        Controls.Add(shell);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public List<PhcSeriesMapping> InvoiceMappings { get; private set; } = [];

    public List<PhcSeriesMapping> ReceiptMappings { get; private set; } = [];

    private static TabPage BuildPage(
        string title,
        string description,
        DataGridView grid,
        IReadOnlyList<SaftDocumentSeries> series,
        IReadOnlyList<PhcDocumentType> documents)
    {
        var page = new TabPage(title) { BackColor = UiTheme.Canvas, Padding = new Padding(12) };
        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        var add = new Button { AutoSize = true, Padding = new Padding(10, 4, 10, 4), Text = "Adicionar linha" };
        add.Click += (_, _) => AddMappingRow(grid, series, documents);
        var remove = new Button { AutoSize = true, Padding = new Padding(10, 4, 10, 4), Text = "Remover linha" };
        remove.Click += (_, _) => RemoveSelectedRows(grid);
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(remove);
        toolbar.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(14, 8, 0, 0),
            Text = description
        });
        page.Controls.Add(grid);
        page.Controls.Add(toolbar);
        return page;
    }

    private static DataGridView BuildGrid(
        IReadOnlyList<SaftDocumentSeries> series,
        IReadOnlyList<PhcDocumentType> documents)
    {
        var grid = UiTheme.CreateGrid();
        grid.AllowUserToAddRows = false;
        grid.AutoGenerateColumns = false;
        grid.MultiSelect = true;
        grid.ReadOnly = false;
        grid.DataError += (_, eventArgs) => eventArgs.ThrowException = false;
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataSource = series.ToList(),
            DisplayMember = nameof(SaftDocumentSeries.DisplayName),
            DisplayStyleForCurrentCellOnly = true,
            HeaderText = "Série do SAF-T",
            MinimumWidth = 300,
            Name = "SaftSeries",
            ValueMember = nameof(SaftDocumentSeries.Key),
            Width = 390
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataSource = documents.ToList(),
            DisplayMember = nameof(PhcDocumentType.DisplayName),
            DisplayStyleForCurrentCellOnly = true,
            HeaderText = "Documento PHC",
            MinimumWidth = 320,
            Name = "PhcDocument",
            ValueMember = nameof(PhcDocumentType.Key),
            Width = 480
        });
        return grid;
    }

    private static void PopulateGrid(
        DataGridView grid,
        IReadOnlyList<SaftDocumentSeries> series,
        IReadOnlyList<PhcDocumentType> documents,
        IReadOnlyList<PhcSeriesMapping> mappings)
    {
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            var seriesKey = SeriesKey(mapping.SaftDocumentType, mapping.SaftSeries);
            var document = documents.FirstOrDefault(item =>
                item.Number == mapping.PhcDocumentNumber &&
                item.SourceTable.Equals(mapping.PhcTableName, StringComparison.OrdinalIgnoreCase));
            grid.Rows.Add(seriesKey, document?.Key ?? string.Empty);
            mappedKeys.Add(seriesKey);
        }

        foreach (var item in series.Where(item => !mappedKeys.Contains(item.Key)))
        {
            grid.Rows.Add(item.Key, string.Empty);
        }
    }

    private static void AddMappingRow(
        DataGridView grid,
        IReadOnlyList<SaftDocumentSeries> series,
        IReadOnlyList<PhcDocumentType> documents)
    {
        if (series.Count == 0)
        {
            MessageBox.Show(grid.FindForm(), "Não existem séries SAF-T disponíveis para esta empresa.", "Séries SAF-T", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var used = grid.Rows.Cast<DataGridViewRow>()
            .Select(row => Convert.ToString(row.Cells["SaftSeries"].Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = series.FirstOrDefault(item => !used.Contains(item.Key)) ?? series[0];
        var rowIndex = grid.Rows.Add(available.Key, documents.FirstOrDefault()?.Key ?? string.Empty);
        grid.CurrentCell = grid.Rows[rowIndex].Cells["SaftSeries"];
    }

    private static void RemoveSelectedRows(DataGridView grid)
    {
        var rows = grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(row => row.Index).ToList();
        if (rows.Count == 0 && grid.CurrentRow is not null)
        {
            rows.Add(grid.CurrentRow);
        }

        foreach (var row in rows)
        {
            grid.Rows.Remove(row);
        }
    }

    private void Confirm()
    {
        if (!TryReadMappings(_invoiceGrid, _invoiceSeries, _invoiceDocuments, "faturação", out var invoiceMappings) ||
            !TryReadMappings(_receiptGrid, _receiptSeries, _receiptDocuments, "recebimentos", out var receiptMappings))
        {
            return;
        }

        InvoiceMappings = invoiceMappings;
        ReceiptMappings = receiptMappings;
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool TryReadMappings(
        DataGridView grid,
        IReadOnlyList<SaftDocumentSeries> series,
        IReadOnlyList<PhcDocumentType> documents,
        string area,
        out List<PhcSeriesMapping> mappings)
    {
        grid.EndEdit();
        mappings = [];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in grid.Rows)
        {
            var seriesKey = Convert.ToString(row.Cells["SaftSeries"].Value)?.Trim() ?? string.Empty;
            var documentKey = Convert.ToString(row.Cells["PhcDocument"].Value)?.Trim() ?? string.Empty;
            if (seriesKey.Length == 0 && documentKey.Length == 0)
            {
                continue;
            }

            var seriesItem = series.FirstOrDefault(item => item.Key.Equals(seriesKey, StringComparison.OrdinalIgnoreCase));
            var documentItem = documents.FirstOrDefault(item => item.Key.Equals(documentKey, StringComparison.OrdinalIgnoreCase));
            if (seriesItem is null || documentItem is null)
            {
                MessageBox.Show(this, $"Complete a série SAF-T e o documento PHC em todas as linhas de {area}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!used.Add(seriesItem.Key))
            {
                MessageBox.Show(this, $"A série {seriesItem.DocumentType} — {seriesItem.Series} está repetida em {area}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            mappings.Add(new PhcSeriesMapping
            {
                SaftDocumentType = seriesItem.DocumentType,
                SaftSeries = seriesItem.Series,
                PhcDocumentNumber = documentItem.Number,
                PhcDocumentName = documentItem.Name,
                PhcTableName = documentItem.SourceTable
            });
        }

        return true;
    }

    private static IReadOnlyList<SaftDocumentSeries> MergeSeries(
        IEnumerable<SaftDocumentSeries> current,
        IReadOnlyList<PhcSeriesMapping> mappings)
    {
        var result = current.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            var key = SeriesKey(mapping.SaftDocumentType, mapping.SaftSeries);
            result.TryAdd(key, new SaftDocumentSeries(mapping.SaftDocumentType, mapping.SaftSeries, 0));
        }

        return result.Values
            .OrderBy(item => item.DocumentType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Series, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PhcDocumentType> MergeDocuments(
        IEnumerable<PhcDocumentType> current,
        IReadOnlyList<PhcSeriesMapping> mappings,
        string defaultTable)
    {
        var result = current.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings.Where(mapping => mapping.PhcDocumentNumber is not null))
        {
            var table = mapping.PhcTableName.Length == 0 ? defaultTable : mapping.PhcTableName;
            var item = new PhcDocumentType(
                mapping.PhcDocumentNumber!.Value,
                mapping.PhcDocumentName,
                string.Empty,
                false,
                false,
                table);
            result.TryAdd(item.Key, item);
        }

        return result.Values.OrderBy(item => item.Number).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string SeriesKey(string documentType, string series) => $"{documentType}\u001f{series}";
}
