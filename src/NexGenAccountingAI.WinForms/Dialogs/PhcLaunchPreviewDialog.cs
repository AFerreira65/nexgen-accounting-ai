using NexGenAccountingAI.Core.Phc;

namespace NexGenAccountingAI.WinForms.Dialogs;

internal sealed class PhcLaunchPreviewDialog : Form
{
    public PhcLaunchPreviewDialog(PhcAggregatedInvoiceTarget target, GridLayoutManager? layouts = null)
        : this([target], layouts)
    {
    }

    public PhcLaunchPreviewDialog(
        IReadOnlyList<PhcAggregatedInvoiceTarget> targets,
        GridLayoutManager? layouts = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("Não existem documentos para pré-visualizar.", nameof(targets));
        }

        var target = targets[0];
        Text = "Confirmar lançamento no PHC";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 620);
        ClientSize = new Size(1080, 700);
        Font = new Font("Segoe UI", 9F);

        var header = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 4) };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = UiTheme.Primary,
            Location = new Point(18, 10),
            Text = "Pré-visualização dos documentos agregados"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(20, 47),
            Text = "A confirmação seguinte cria registos no PHC numa única transação. Em caso de erro é efetuado rollback."
        });

        var details = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 4, 18, 8),
            RowCount = 4
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddDetail(details, 0, "Empresa PHC", target.Company.Name, "Base de dados", target.Company.DatabaseName);
        AddDetail(details, 1, "Documento PHC", $"{target.DocumentNumber} — {target.DocumentName}", "Data", target.Draft.DocumentDate.ToString("dd/MM/yyyy"));
        AddDetail(details, 2, "Cliente genérico", $"{target.CustomerNumber} — {target.CustomerName}", "Documentos SAF-T", target.Draft.DocumentCount.ToString("N0"));
        AddDetail(details, 3, "Documentos a criar", targets.Count.ToString("N0"), "Anulados ignorados", target.Draft.CancelledDocumentCount.ToString("N0"));

        var grid = UiTheme.CreateGrid();
        grid.AllowUserToAddRows = false;
        grid.AutoGenerateColumns = false;
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DocumentoGrupo", HeaderText = "Documento / grupo", MinimumWidth = 240, Width = 300 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Taxa", HeaderText = "Taxa de IVA", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CodIsencao", HeaderText = "Cód. isenção", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MotivoIsencao", HeaderText = "Motivo de isenção", MinimumWidth = 220, Width = 280 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Artigo", HeaderText = "Artigo PHC", MinimumWidth = 260, Width = 330 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CPOC", HeaderText = "CPOC", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TabIVA", HeaderText = "Tab. IVA", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stock", HeaderText = "Movimenta stock", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Liquido", HeaderText = "Líquido", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "IVA", HeaderText = "IVA", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total", Width = 130 });
        foreach (var document in targets)
        {
            foreach (var line in document.Lines)
            {
                grid.Rows.Add(
                    document.Draft.GroupLabel,
                    $"{line.SaftVatRate:N2} %",
                    line.TaxExemptionCode,
                    line.TaxExemptionReason,
                    $"{line.ArticleReference} — {line.ArticleDesignation}",
                    line.Cpoc,
                    line.VatTable,
                    line.MovesStock ? "Sim" : "Não",
                    line.NetAmount.ToString("N2"),
                    line.VatAmount.ToString("N2"),
                    line.GrossAmount.ToString("N2"));
            }
        }
        layouts?.Attach(grid, "lancamento-phc.iva-artigos");

        var totals = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = UiTheme.Primary,
            Padding = new Padding(18, 10, 18, 4),
            TextAlign = ContentAlignment.MiddleRight,
            Text = $"Líquido: {targets.Sum(item => item.Draft.NetTotal):N2} €     " +
                   $"IVA: {targets.Sum(item => item.Draft.VatTotal):N2} €     " +
                   $"Total: {targets.Sum(item => item.Draft.GrossTotal):N2} €"
        };

        var buttons = new FlowLayoutPanel
        {
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12),
            WrapContents = false
        };
        var launch = new Button
        {
            AutoSize = true,
            BackColor = UiTheme.Primary,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Padding = new Padding(16, 6, 16, 6),
            Text = "Confirmar e lançar no PHC"
        };
        launch.FlatAppearance.BorderSize = 0;
        launch.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Padding = new Padding(12, 6, 12, 6),
            Text = "Cancelar"
        };
        buttons.Controls.Add(launch);
        buttons.Controls.Add(cancel);

        var shell = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 5 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(details, 0, 1);
        shell.Controls.Add(grid, 0, 2);
        shell.Controls.Add(totals, 0, 3);
        shell.Controls.Add(buttons, 0, 4);
        Controls.Add(shell);
        AcceptButton = launch;
        CancelButton = cancel;
    }

    private static void AddDetail(
        TableLayoutPanel layout,
        int row,
        string leftLabel,
        string leftValue,
        string rightLabel,
        string rightValue)
    {
        layout.Controls.Add(Caption(leftLabel), 0, row);
        layout.Controls.Add(Value(leftValue), 1, row);
        layout.Controls.Add(Caption(rightLabel), 2, row);
        layout.Controls.Add(Value(rightValue), 3, row);
    }

    private static Label Caption(string text) => new()
    {
        Anchor = AnchorStyles.Left,
        AutoSize = true,
        ForeColor = UiTheme.Muted,
        Text = text
    };

    private static Label Value(string text) => new()
    {
        Anchor = AnchorStyles.Left,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 9F),
        ForeColor = UiTheme.Text,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft
    };
}
