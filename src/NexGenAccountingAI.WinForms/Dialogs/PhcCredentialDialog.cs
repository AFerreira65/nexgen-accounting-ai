namespace NexGenAccountingAI.WinForms.Dialogs;

internal sealed class PhcCredentialDialog : Form
{
    private readonly TextBox _username = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };

    public PhcCredentialDialog(string odbcName, string username, string password)
    {
        Text = $"Credenciais PHC — {odbcName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 210);
        Font = new Font("Segoe UI", 9F);
        _username.Text = username;
        _password.Text = password;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 4,
            Dock = DockStyle.Fill,
            Padding = new Padding(18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "ODBC" }, 0, 0);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI Semibold", 9F), Text = odbcName }, 1, 0);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Utilizador" }, 0, 1);
        layout.Controls.Add(_username, 1, 1);
        layout.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Palavra-passe" }, 0, 2);
        layout.Controls.Add(_password, 1, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { AutoSize = true, Padding = new Padding(12, 5, 12, 5), Text = "Guardar" };
        save.Click += (_, _) => Confirm();
        var cancel = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Text = "Cancelar" };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || _password.Text.Length == 0)
        {
            MessageBox.Show(this, "Indique o utilizador e a palavra-passe do ODBC.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Username = _username.Text.Trim();
        Password = _password.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
