using AccountingAI.Core.Configuration;

namespace AccountingAI.WinForms.Dialogs;

internal sealed class PhcCompanyAssociationDialog : Form
{
    private readonly ComboBox _companies = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };

    public PhcCompanyAssociationDialog(string taxId, string saftCompanyName, IEnumerable<PhcCompanyConfiguration> companies)
    {
        Text = "Associar SAF-T a uma empresa PHC";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 260);
        Font = new Font("Segoe UI", 9F);

        foreach (var company in companies.OrderByDescending(company => company.PresentInEp).ThenBy(company => company.Name))
        {
            _companies.Items.Add(new CompanyItem(company));
        }
        if (_companies.Items.Count > 0)
        {
            _companies.SelectedIndex = 0;
        }

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 5,
            Dock = DockStyle.Fill,
            Padding = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Empresa no SAF-T" }, 0, 0);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI Semibold", 9F), Text = saftCompanyName }, 1, 0);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Contribuinte" }, 0, 1);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI Semibold", 9F), Text = taxId }, 1, 1);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Empresa PHC" }, 0, 2);
        layout.Controls.Add(_companies, 1, 2);
        var note = new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            MaximumSize = new Size(650, 0),
            Text = "Esta associação é pedida apenas na primeira abertura e ficará guardada no ficheiro encriptado."
        };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var associate = new Button { AutoSize = true, Padding = new Padding(12, 5, 12, 5), Text = "Associar" };
        associate.Click += (_, _) => Confirm();
        var later = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Text = "Mais tarde" };
        buttons.Controls.Add(associate);
        buttons.Controls.Add(later);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = associate;
        CancelButton = later;
    }

    public PhcCompanyConfiguration? SelectedCompany { get; private set; }

    private void Confirm()
    {
        if (_companies.SelectedItem is not CompanyItem item)
        {
            MessageBox.Show(this, "Selecione a empresa PHC correspondente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedCompany = item.Company;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record CompanyItem(PhcCompanyConfiguration Company)
    {
        public override string ToString()
        {
            var nif = Company.TaxRegistrationNumber.Length > 0 ? $" — NIF {Company.TaxRegistrationNumber}" : string.Empty;
            return $"{Company.Name} — BD {Company.DatabaseName} — ODBC {Company.OdbcName}{nif}";
        }
    }
}
