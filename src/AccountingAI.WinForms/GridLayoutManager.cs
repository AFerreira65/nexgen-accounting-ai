using AccountingAI.Core.Configuration;

namespace AccountingAI.WinForms;

internal sealed class GridLayoutManager : IDisposable
{
    private readonly ConfigurationStore _store;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private AccountingAiConfiguration _configuration;
    private bool _dirty;

    public GridLayoutManager(AccountingAiConfiguration configuration, ConfigurationStore store)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _saveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _saveTimer.Tick += (_, _) => Flush();
    }

    public Exception? LastSaveError { get; private set; }

    public event Action<Exception>? SaveFailed;

    public void Attach(DataGridView grid, string gridKey)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (string.IsNullOrWhiteSpace(gridKey))
        {
            throw new ArgumentException("A grelha deve ter uma chave de configuração.", nameof(gridKey));
        }

        grid.AllowUserToResizeColumns = true;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        var initialized = false;
        var applying = false;

        void ApplyLayout()
        {
            if (grid.Columns.Count == 0)
            {
                return;
            }

            applying = true;
            try
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    column.Resizable = DataGridViewTriState.True;
                }

                var hasSavedWidths = _configuration.GridColumnWidths.TryGetValue(gridKey, out var widths);
                if (!initialized && !hasSavedWidths)
                {
                    grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                }

                if (widths is not null)
                {
                    foreach (DataGridViewColumn column in grid.Columns)
                    {
                        if (widths.TryGetValue(ColumnKey(column), out var width))
                        {
                            column.Width = Math.Clamp(width, Math.Max(20, column.MinimumWidth), 3000);
                        }
                    }
                }

                initialized = true;
            }
            finally
            {
                applying = false;
            }
        }

        grid.DataBindingComplete += (_, _) => ApplyLayout();
        grid.ColumnAdded += (_, eventArgs) =>
        {
            eventArgs.Column.Resizable = DataGridViewTriState.True;
            if (grid.IsHandleCreated)
            {
                grid.BeginInvoke(ApplyLayout);
            }
        };
        grid.ColumnWidthChanged += (_, eventArgs) =>
        {
            if (!initialized || applying || eventArgs.Column is null)
            {
                return;
            }

            if (!_configuration.GridColumnWidths.TryGetValue(gridKey, out var widths))
            {
                widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _configuration.GridColumnWidths[gridKey] = widths;
            }

            widths[ColumnKey(eventArgs.Column)] = eventArgs.Column.Width;
            ScheduleSave();
        };
        ApplyLayout();
    }

    public void CopyLayoutsTo(AccountingAiConfiguration target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.GridColumnWidths = _configuration.GridColumnWidths.ToDictionary(
            grid => grid.Key,
            grid => grid.Value.ToDictionary(column => column.Key, column => column.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    public void UseConfiguration(AccountingAiConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public bool Flush()
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return true;
        }

        try
        {
            _store.Save(_configuration);
            _dirty = false;
            LastSaveError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastSaveError = exception;
            SaveFailed?.Invoke(exception);
            return false;
        }
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static string ColumnKey(DataGridViewColumn column) =>
        string.IsNullOrWhiteSpace(column.DataPropertyName) ? column.Name : column.DataPropertyName;
}
