using System.Data;
using AccountingAI.Core.Phc;

namespace AccountingAI.WinForms.Controls;

internal sealed class PurchaseAuditPage : UserControl
{
    private readonly DataGridView _efaturaGrid = UiTheme.CreateGrid();
    private readonly DataGridView _phcGrid = UiTheme.CreateGrid();
    private readonly Button _refresh = new();
    private readonly Label _summary = new();
    private readonly DateTimePicker _searchStart = AuditDatePicker();
    private readonly DateTimePicker _searchEnd = AuditDatePicker();
    private readonly Func<DateOnly, DateOnly, Task<PurchaseAuditResult?>> _reload;
    private PurchaseAuditResult _currentResult;
    private Point _dragStart;
    private DataGridView? _dragSource;
    private bool _loading;
    private bool _synchronizingSelection;

    public PurchaseAuditPage(
        PurchaseAuditResult initial,
        Func<DateOnly, DateOnly, Task<PurchaseAuditResult?>> reload,
        GridLayoutManager? layouts = null)
    {
        _currentResult = initial;
        _reload = reload ?? throw new ArgumentNullException(nameof(reload));
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;
        Padding = new Padding(10);

        _efaturaGrid.MultiSelect = false;
        _phcGrid.MultiSelect = false;
        _efaturaGrid.SelectionChanged += (_, _) => SynchronizeSelection(_efaturaGrid, _phcGrid);
        _phcGrid.SelectionChanged += (_, _) => SynchronizeSelection(_phcGrid, _efaturaGrid);
        _efaturaGrid.DataBindingComplete += (_, _) => ConfigureGrid(_efaturaGrid);
        _phcGrid.DataBindingComplete += (_, _) => ConfigureGrid(_phcGrid);
        EnableManualMatching(_efaturaGrid, fromEfatura: true);
        EnableManualMatching(_phcGrid, fromEfatura: false);
        layouts?.Attach(_efaturaGrid, "auditoria-compras.efatura");
        layouts?.Attach(_phcGrid, "auditoria-compras.phc");

        _refresh.AutoSize = true;
        _refresh.BackColor = UiTheme.Primary;
        _refresh.FlatStyle = FlatStyle.Flat;
        _refresh.ForeColor = Color.White;
        _refresh.Padding = new Padding(12, 5, 12, 5);
        _refresh.Text = "Atualizar auditoria PHC";
        _refresh.FlatAppearance.BorderSize = 0;
        _refresh.Click += async (_, _) => await ReloadAsync();

        _summary.AutoSize = true;
        _summary.ForeColor = UiTheme.Muted;
        _summary.Font = new Font("Segoe UI", 9F);
        _summary.Margin = new Padding(14, 8, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        toolbar.Controls.Add(ToolbarLabel("Data inicial"));
        toolbar.Controls.Add(_searchStart);
        toolbar.Controls.Add(ToolbarLabel("Data final"));
        toolbar.Controls.Add(_searchEnd);
        toolbar.Controls.Add(_refresh);
        toolbar.Controls.Add(_summary);

        var grids = new TableLayoutPanel
        {
            BackColor = UiTheme.Border,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 1
        };
        grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grids.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        grids.Controls.Add(BuildGridPanel("Documentos do e-Fatura", _efaturaGrid), 0, 0);
        grids.Controls.Add(BuildGridPanel("Documentos registados no PHC", _phcGrid), 1, 0);

        var note = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            ForeColor = UiTheme.Muted,
            Padding = new Padding(0, 8, 0, 0),
            Text = "Arraste um documento de uma grelha para o documento correspondente na outra. A correspondência automática usa NIF, sufixo do número, proximidade da data, total e IVA."
        };

        var content = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        content.Controls.Add(grids);
        content.Controls.Add(note);
        content.Controls.Add(toolbar);
        Controls.Add(content);
        LoadResult(initial);
    }

    private async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        _refresh.Enabled = false;
        _summary.Text = "A consultar FO e FOT no PHC…";
        UseWaitCursor = true;
        try
        {
            var start = DateOnly.FromDateTime(_searchStart.Value);
            var end = DateOnly.FromDateTime(_searchEnd.Value);
            if (end < start)
            {
                MessageBox.Show(
                    this,
                    "A data final não pode ser anterior à data inicial.",
                    "Auditoria de compras",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var result = await _reload(start, end);
            if (result is not null)
            {
                LoadResult(result);
            }
        }
        finally
        {
            UseWaitCursor = false;
            _refresh.Enabled = true;
            _loading = false;
        }
    }

    private void LoadResult(PurchaseAuditResult result, bool updateDates = true)
    {
        if (_currentResult.ManualMatches.Count > 0 && result.PhcDocuments.Count > 0 &&
            result.ManualMatches.Count == 0)
        {
            result = PhcPurchaseAuditService.Compare(
                result.EfaturaDocuments,
                result.PhcDocuments,
                result.PhcSearchStart,
                result.PhcSearchEnd,
                result.Options,
                _currentResult.ManualMatches);
        }

        _currentResult = result;
        if (updateDates && result.PhcSearchStart.HasValue && result.PhcSearchEnd.HasValue)
        {
            _searchStart.Value = result.PhcSearchStart.Value.ToDateTime(TimeOnly.MinValue);
            _searchEnd.Value = result.PhcSearchEnd.Value.ToDateTime(TimeOnly.MinValue);
        }

        _efaturaGrid.DataSource = result.EfaturaRows.ToDataTable();
        _phcGrid.DataSource = result.PhcRows.ToDataTable();
        var range = result.PhcSearchStart.HasValue && result.PhcSearchEnd.HasValue
            ? $" | PHC pesquisado de {result.PhcSearchStart.Value:dd/MM/yyyy} a {result.PhcSearchEnd.Value:dd/MM/yyyy}"
            : string.Empty;
        _summary.Text = result.PairCount == 0
            ? "Sem compras e-Fatura carregadas."
            : result.Summary + range +
              $" | tolerâncias: total ±{result.Options.TotalTolerance:N2} €, IVA ±{result.Options.VatTolerance:N2} €, data ±{result.Options.DateToleranceDays} dia(s)";
    }

    private void EnableManualMatching(DataGridView grid, bool fromEfatura)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            grid.AllowDrop = true;
        }
        grid.MouseDown += (_, eventArgs) =>
        {
            _dragStart = eventArgs.Location;
            _dragSource = grid;
        };
        grid.MouseMove += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Left || _dragSource != grid ||
                Math.Abs(eventArgs.X - _dragStart.X) < SystemInformation.DragSize.Width / 2 &&
                Math.Abs(eventArgs.Y - _dragStart.Y) < SystemInformation.DragSize.Height / 2)
            {
                return;
            }

            var hit = grid.HitTest(_dragStart.X, _dragStart.Y);
            if (hit.RowIndex < 0)
            {
                return;
            }

            var keyColumn = fromEfatura ? "Chave e-Fatura" : "Chave PHC";
            var key = Convert.ToString(grid.Rows[hit.RowIndex].Cells[keyColumn].Value)?.Trim() ?? string.Empty;
            if (key.Length > 0)
            {
                _dragSource = null;
                grid.DoDragDrop(new AuditDrag(fromEfatura, key), DragDropEffects.Link);
            }
        };
        grid.DragEnter += (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetData(typeof(AuditDrag)) is AuditDrag drag && drag.FromEfatura != fromEfatura)
            {
                eventArgs.Effect = DragDropEffects.Link;
            }
        };
        grid.DragOver += (_, eventArgs) =>
        {
            if (eventArgs.Data?.GetData(typeof(AuditDrag)) is AuditDrag drag && drag.FromEfatura != fromEfatura)
            {
                eventArgs.Effect = DragDropEffects.Link;
            }
        };
        grid.DragDrop += (_, eventArgs) => CompleteManualMatch(grid, fromEfatura, eventArgs);
    }

    private void CompleteManualMatch(DataGridView targetGrid, bool targetIsEfatura, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(typeof(AuditDrag)) is not AuditDrag drag ||
            drag.FromEfatura == targetIsEfatura)
        {
            return;
        }

        var point = targetGrid.PointToClient(new Point(eventArgs.X, eventArgs.Y));
        var hit = targetGrid.HitTest(point.X, point.Y);
        if (hit.RowIndex < 0)
        {
            return;
        }

        var targetKeyColumn = targetIsEfatura ? "Chave e-Fatura" : "Chave PHC";
        var targetKey = Convert.ToString(targetGrid.Rows[hit.RowIndex].Cells[targetKeyColumn].Value)?.Trim() ?? string.Empty;
        var efaturaKey = drag.FromEfatura ? drag.DocumentKey : targetKey;
        var phcKey = drag.FromEfatura ? targetKey : drag.DocumentKey;
        if (efaturaKey.Length == 0 || phcKey.Length == 0)
        {
            MessageBox.Show(
                this,
                "Solte o documento sobre uma linha que contenha um documento do outro sistema.",
                "Relacionar documentos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var mappings = _currentResult.ManualMatches
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var existing in mappings
                     .Where(pair => pair.Key.Equals(efaturaKey, StringComparison.OrdinalIgnoreCase) ||
                                    pair.Value.Equals(phcKey, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            mappings.Remove(existing);
        }

        mappings[efaturaKey] = phcKey;
        LoadResult(
            PhcPurchaseAuditService.Compare(
                _currentResult.EfaturaDocuments,
                _currentResult.PhcDocuments,
                _currentResult.PhcSearchStart,
                _currentResult.PhcSearchEnd,
                _currentResult.Options,
                mappings),
            updateDates: false);
    }

    private static Control BuildGridPanel(string title, DataGridView grid)
    {
        var panel = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(1) };
        var label = new Label
        {
            AutoSize = false,
            BackColor = Color.White,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = UiTheme.Primary,
            Height = 36,
            Padding = new Padding(8, 7, 0, 0),
            Text = title
        };
        panel.Controls.Add(grid);
        panel.Controls.Add(label);
        return panel;
    }

    private void SynchronizeSelection(DataGridView source, DataGridView target)
    {
        if (_synchronizingSelection || source.CurrentRow is null)
        {
            return;
        }

        var index = source.CurrentRow.Index;
        if (index < 0 || index >= target.Rows.Count)
        {
            return;
        }

        _synchronizingSelection = true;
        try
        {
            target.ClearSelection();
            target.Rows[index].Selected = true;
            var visibleCell = target.Rows[index].Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
            if (visibleCell is not null)
            {
                target.CurrentCell = visibleCell;
            }
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.MinimumWidth = 90;
            if (column.Name is "Ficheiro" or "Chave de auditoria" or "Chave e-Fatura" or "Chave PHC")
            {
                column.Visible = false;
            }

            if (column.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Contains("IVA", StringComparison.OrdinalIgnoreCase))
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            var status = Convert.ToString(row.Cells["Estado da auditoria"].Value) ?? string.Empty;
            row.DefaultCellStyle.BackColor = status switch
            {
                "Conforme" => Color.FromArgb(226, 244, 234),
                "Diferenças" => Color.FromArgb(255, 228, 225),
                "Sem correspondência no PHC" or "Apenas no PHC" => Color.FromArgb(255, 246, 204),
                _ when status.StartsWith("Conforme", StringComparison.Ordinal) => Color.FromArgb(226, 244, 234),
                _ when status.StartsWith("Diferenças", StringComparison.Ordinal) => Color.FromArgb(255, 228, 225),
                _ => Color.FromArgb(226, 239, 250)
            };
        }
    }

    private static DateTimePicker AuditDatePicker() => new()
    {
        CustomFormat = "dd/MM/yyyy",
        Format = DateTimePickerFormat.Custom,
        Width = 112
    };

    private static Label ToolbarLabel(string text) => new()
    {
        AutoSize = true,
        Margin = new Padding(8, 8, 3, 0),
        Text = text
    };

    private sealed record AuditDrag(bool FromEfatura, string DocumentKey);
}
