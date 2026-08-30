namespace AccountingAI.WinForms;

internal static class UiTheme
{
    public static readonly Color Primary = Color.FromArgb(21, 76, 121);
    public static readonly Color PrimaryDark = Color.FromArgb(14, 53, 86);
    public static readonly Color Accent = Color.FromArgb(0, 137, 123);
    public static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    public static readonly Color Border = Color.FromArgb(215, 222, 230);
    public static readonly Color Text = Color.FromArgb(34, 45, 57);
    public static readonly Color Muted = Color.FromArgb(96, 110, 125);

    public static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AllowUserToResizeColumns = true,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            Dock = DockStyle.Fill,
            EnableHeadersVisualStyles = false,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Primary;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
        grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 232, 248);
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(3);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.RowTemplate.Height = 28;
        return grid;
    }
}
