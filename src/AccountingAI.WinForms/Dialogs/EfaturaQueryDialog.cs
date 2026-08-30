using AccountingAI.Core.Configuration;
using AccountingAI.Core.Efatura;

namespace AccountingAI.WinForms.Dialogs;

internal sealed class EfaturaQueryDialog : Form
{
    private readonly TextBox _customerTaxId = Field();
    private readonly TextBox _username = Field();
    private readonly TextBox _portalPassword = PasswordField();
    private readonly TextBox _pfxPath = Field();
    private readonly TextBox _pfxPassword = PasswordField();
    private readonly TextBox _authenticationCertificatePath = Field();
    private readonly DateTimePicker _startDate = DateField();
    private readonly DateTimePicker _endDate = DateField();
    private readonly ComboBox _environment = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    public EfaturaQueryDialog(
        DateOnly suggestedStart,
        DateOnly suggestedEnd,
        string suggestedTaxId,
        AccountingAiConfiguration configuration,
        string suggestedUsername = "",
        string suggestedPortalPassword = "",
        string suggestedPfxPassword = "")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Text = "Consultar compras no e-Fatura";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 565);
        Font = new Font("Segoe UI", 9F);

        _customerTaxId.Text = string.IsNullOrWhiteSpace(suggestedTaxId)
            ? configuration.CustomerTaxId
            : suggestedTaxId;
        _username.Text = !string.IsNullOrWhiteSpace(suggestedUsername)
            ? suggestedUsername
            : string.IsNullOrWhiteSpace(configuration.WfaUsername)
                ? _customerTaxId.Text
                : configuration.WfaUsername;
        _portalPassword.Text = suggestedPortalPassword;
        _pfxPath.Text = FirstExistingFile(
            configuration.ProducerPfxPath,
            @"C:\PHC\_LOC\certifphc.pfx",
            @"C:\PHC_LOC\certifphc.pfx");
        _pfxPassword.Text = suggestedPfxPassword;
        _authenticationCertificatePath.Text = FirstExistingFile(
            configuration.AuthenticationCertificatePath,
            @"C:\PHC\_LOC\chavepublicaat.cer",
            @"C:\PHC_LOC\chavepublicaat.cer",
            @"C:\PHC_LOC\ChaveCifraPublicaAT2023.cer");
        _startDate.Value = suggestedStart.ToDateTime(TimeOnly.MinValue);
        _endDate.Value = suggestedEnd.ToDateTime(TimeOnly.MinValue);
        _environment.Items.AddRange(["Produção", "Testes"]);
        _environment.SelectedIndex = configuration.AtEnvironment == EfaturaAtEnvironment.Production ? 0 : 1;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddHeader(layout, "Consulta oficial AT", "Consulta por adquirente, por mês, com paginação até 5 000 documentos por página.");
        AddRow(layout, 2, "NIF do adquirente", _customerTaxId);
        AddRow(layout, 3, "NIF ou subutilizador WFA", _username);
        AddRow(layout, 4, "Senha Portal das Finanças", _portalPassword);
        AddRow(layout, 5, "Data inicial", _startDate);
        AddRow(layout, 6, "Data final", _endDate);
        AddRow(layout, 7, "Ambiente", _environment);
        AddFileRow(layout, 8, "Certificado de produtor (.pfx)", _pfxPath, "Certificados PFX (*.pfx)|*.pfx|Todos os ficheiros (*.*)|*.*");
        AddRow(layout, 9, "Senha do certificado PFX", _pfxPassword);
        AddFileRow(layout, 10, "Chave pública de autenticação AT", _authenticationCertificatePath, "Certificados (*.cer;*.crt)|*.cer;*.crt|Todos os ficheiros (*.*)|*.*");

        var note = new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            MaximumSize = new Size(700, 0),
            Text = "O utilizador e a senha WFA vêm da empresa associada e estão protegidos no ficheiro de configuração. A chave de certifphc.pfx é lida da configuração PHC apenas em memória; para outro PFX, indique-a manualmente."
        };
        layout.Controls.Add(note, 0, 11);
        layout.SetColumnSpan(note, 3);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        var query = new Button
        {
            AutoSize = true,
            BackColor = UiTheme.Primary,
            DialogResult = DialogResult.None,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Padding = new Padding(16, 6, 16, 6),
            Text = "Consultar AT"
        };
        query.FlatAppearance.BorderSize = 0;
        query.Click += (_, _) => Confirm();
        var cancel = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Padding = new Padding(12, 6, 12, 6),
            Text = "Cancelar"
        };
        buttons.Controls.Add(query);
        buttons.Controls.Add(cancel);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = query;
        CancelButton = cancel;
    }

    public string CustomerTaxId => _customerTaxId.Text.Trim();

    public string Username => _username.Text.Trim();

    public string PortalPassword => _portalPassword.Text;

    public string PfxPath => _pfxPath.Text.Trim();

    public string PfxPassword => _pfxPassword.Text;

    public string AuthenticationCertificatePath => _authenticationCertificatePath.Text.Trim();

    public DateOnly StartDate => DateOnly.FromDateTime(_startDate.Value);

    public DateOnly EndDate => DateOnly.FromDateTime(_endDate.Value);

    public EfaturaAtEnvironment Environment => _environment.SelectedIndex == 0
        ? EfaturaAtEnvironment.Production
        : EfaturaAtEnvironment.Test;

    private void Confirm()
    {
        if (EndDate < StartDate)
        {
            MessageBox.Show(this, "A data final não pode ser anterior à data inicial.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomerTaxId) || string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrEmpty(PortalPassword) || string.IsNullOrWhiteSpace(PfxPath) ||
            string.IsNullOrWhiteSpace(AuthenticationCertificatePath))
        {
            MessageBox.Show(this, "Preencha o NIF, subutilizador, senha e ambos os certificados.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddHeader(TableLayoutPanel layout, string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 72 };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = UiTheme.Primary,
            Location = new Point(0, 0),
            Text = title
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 42),
            Text = subtitle
        });
        layout.Controls.Add(panel, 0, 0);
        layout.SetColumnSpan(panel, 3);
        layout.SetRowSpan(panel, 2);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        var label = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = UiTheme.Text,
            Text = caption
        };
        control.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
        layout.SetColumnSpan(control, 2);
    }

    private static void AddFileRow(TableLayoutPanel layout, int row, string caption, TextBox field, string filter)
    {
        var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Text, Text = caption };
        var browse = new Button { Dock = DockStyle.Fill, Text = "Procurar…" };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { CheckFileExists = true, Filter = filter };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                field.Text = dialog.FileName;
            }
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
        layout.Controls.Add(browse, 2, row);
    }

    private static TextBox Field() => new() { BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill };

    private static string FirstExistingFile(params string[] candidates) =>
        candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ??
        candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;

    private static TextBox PasswordField() => new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true
    };

    private static DateTimePicker DateField() => new()
    {
        Dock = DockStyle.Fill,
        Format = DateTimePickerFormat.Short
    };
}
