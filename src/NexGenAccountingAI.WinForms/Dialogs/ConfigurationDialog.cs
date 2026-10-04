using NexGenAccountingAI.Core.Configuration;
using NexGenAccountingAI.Core.Efatura;
using NexGenAccountingAI.Core.Models;
using NexGenAccountingAI.Core.Phc;

namespace NexGenAccountingAI.WinForms.Dialogs;

internal sealed class ConfigurationDialog : Form
{
    private readonly TextBox _customerTaxId = Field();
    private readonly TextBox _pfxPath = Field();
    private readonly TextBox _authenticationCertificatePath = Field();
    private readonly TextBox _downloadDirectory = Field();
    private readonly TextBox _phcDirectory = Field();
    private readonly TextBox _atPassword = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true
    };
    private readonly TextBox _companyWfaUsername = Field();
    private readonly TextBox _wfaPassword = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true
    };
    private readonly CheckBox _createCustomers = new() { AutoSize = true, Text = "Cria Clientes" };
    private readonly CheckBox _createProducts = new() { AutoSize = true, Text = "Cria Artigos" };
    private readonly CheckBox _moveStock = new() { AutoSize = true, Text = "Movimenta Stocks" };
    private readonly ComboBox _genericCustomer = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly ComboBox _invoiceImportMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly NumericUpDown _auditTotalTolerance = AuditMoneyTolerance();
    private readonly NumericUpDown _auditVatTolerance = AuditMoneyTolerance();
    private readonly NumericUpDown _auditDateToleranceDays = new()
    {
        Minimum = 0,
        Maximum = 366,
        Width = 62,
        TextAlign = HorizontalAlignment.Right
    };
    private readonly NumericUpDown _auditDocumentSuffixLength = new()
    {
        Minimum = 3,
        Maximum = 20,
        Width = 54,
        TextAlign = HorizontalAlignment.Right
    };
    private readonly DataGridView _vatArticleMappings = BuildVatArticleMappingsGrid();
    private readonly Panel _vatMappingArea = new() { Dock = DockStyle.Fill };
    private readonly Button _loadArticles = new()
    {
        AutoSize = true,
        Padding = new Padding(8, 2, 8, 2),
        Text = "Carregar artigos ST"
    };
    private readonly Label _selectedCompany = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 9F),
        ForeColor = UiTheme.Primary
    };
    private readonly ComboBox _environment = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DataGridView _phcCompanies = BuildPhcCompaniesGrid();
    private readonly Label _phcStatus = new() { AutoSize = true, ForeColor = UiTheme.Muted };
    private readonly GridLayoutManager? _layouts;
    private readonly SaftDataSet _saftData;
    private readonly Dictionary<string, IReadOnlyList<PhcArticle>> _stockArticles = new(StringComparer.OrdinalIgnoreCase);
    private GroupBox? _companySettingsGroup;
    private bool _updatingCompanySettings;

    public ConfigurationDialog(
        AccountingAiConfiguration current,
        GridLayoutManager? layouts = null,
        SaftDataSet? saftData = null,
        string? preferredCompanyTaxId = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        Configuration = current.Clone();
        _layouts = layouts;
        _saftData = saftData ?? new SaftDataSet();
        layouts?.Attach(_phcCompanies, "configuracao.empresas-phc");
        layouts?.Attach(_vatArticleMappings, "configuracao.iva-artigos");

        Text = "Configuração do NexGen Accounting AI";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(1050, 780);
        ClientSize = new Size(1200, 900);
        Font = new Font("Segoe UI", 9F);

        _customerTaxId.Text = current.CustomerTaxId;
        _pfxPath.Text = current.ProducerPfxPath;
        _authenticationCertificatePath.Text = current.AuthenticationCertificatePath;
        _downloadDirectory.Text = current.PortalDownloadDirectory;
        _phcDirectory.Text = current.PhcDirectory;
        _environment.Items.AddRange(["Produção", "Testes"]);
        _environment.SelectedIndex = current.AtEnvironment == EfaturaAtEnvironment.Production ? 0 : 1;
        _invoiceImportMode.Items.AddRange(
        [
            new InvoiceImportModeOption(
                PhcInvoiceImportMode.OneDocumentForGenericCustomer,
                "Agrupa 1 documento no Cliente genérico"),
            new InvoiceImportModeOption(
                PhcInvoiceImportMode.DocumentsWithLinesGroupedByVat,
                "Lança todos os documentos com as linhas agrupadas pelo IVA"),
            new InvoiceImportModeOption(
                PhcInvoiceImportMode.DocumentsAsInSaft,
                "Lança os documentos como estão no SAF-T")
        ]);

        _phcCompanies.SelectionChanged += (_, _) => LoadSelectedCompanySettings();
        _createCustomers.CheckedChanged += (_, _) => SaveSelectedCompanySettings();
        _createProducts.CheckedChanged += (_, _) =>
        {
            if (!_createProducts.Checked)
            {
                _moveStock.Checked = false;
            }

            _moveStock.Enabled = _createProducts.Checked;
            SaveSelectedCompanySettings();
        };
        _moveStock.CheckedChanged += (_, _) => SaveSelectedCompanySettings();
        _atPassword.TextChanged += (_, _) => SaveSelectedCompanySettings();
        _companyWfaUsername.TextChanged += (_, _) => SaveSelectedCompanySettings();
        _wfaPassword.TextChanged += (_, _) => SaveSelectedCompanySettings();
        _genericCustomer.SelectedIndexChanged += (_, _) => SaveSelectedCompanySettings();
        _invoiceImportMode.SelectedIndexChanged += (_, _) =>
        {
            UpdateVatMappingVisibility();
            SaveSelectedCompanySettings();
        };
        _auditTotalTolerance.ValueChanged += (_, _) => SaveSelectedCompanySettings();
        _auditVatTolerance.ValueChanged += (_, _) => SaveSelectedCompanySettings();
        _auditDateToleranceDays.ValueChanged += (_, _) => SaveSelectedCompanySettings();
        _auditDocumentSuffixLength.ValueChanged += (_, _) => SaveSelectedCompanySettings();
        _vatArticleMappings.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_vatArticleMappings.IsCurrentCellDirty)
            {
                _vatArticleMappings.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _vatArticleMappings.CellValueChanged += (_, _) => SaveSelectedCompanySettings();
        _loadArticles.Click += async (_, _) => await LoadStockArticlesAsync();

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        var atPage = new TabPage("Autoridade Tributária") { BackColor = UiTheme.Canvas, Padding = new Padding(16) };
        atPage.Controls.Add(BuildAtConfiguration());
        tabs.TabPages.Add(atPage);
        var phcPage = new TabPage("Empresas PHC") { BackColor = UiTheme.Canvas, Padding = new Padding(16) };
        phcPage.Controls.Add(BuildPhcConfiguration());
        tabs.TabPages.Add(phcPage);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
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
            Text = "Guardar"
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
            RowCount = 2
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.Controls.Add(tabs, 0, 0);
        shell.Controls.Add(buttons, 0, 1);
        Controls.Add(shell);
        AcceptButton = save;
        CancelButton = cancel;
        var preferredCompany = string.IsNullOrWhiteSpace(preferredCompanyTaxId)
            ? null
            : Configuration.PhcCompanies.FirstOrDefault(company =>
                company.TaxRegistrationNumber.Equals(preferredCompanyTaxId.Trim(), StringComparison.Ordinal));
        RefreshPhcCompaniesGrid(preferredCompany);
        LoadSelectedCompanySettings();
    }

    public AccountingAiConfiguration Configuration { get; private set; }

    private Control BuildAtConfiguration()
    {
        var layout = new TableLayoutPanel
        {
            AutoScroll = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));

        AddHeader(layout, "Consulta à AT", "Valores predefinidos para o Portal e para o webservice oficial. O NexGen Accounting AI apenas consulta dados.");
        AddRow(layout, 2, "NIF do adquirente", _customerTaxId);
        AddRow(layout, 3, "Ambiente do webservice", _environment);
        AddFileRow(layout, 4, "Certificado de produtor (.pfx)", _pfxPath, "Certificados PFX (*.pfx)|*.pfx|Todos os ficheiros (*.*)|*.*");
        AddFileRow(layout, 5, "Chave pública de autenticação AT", _authenticationCertificatePath, "Certificados (*.cer;*.crt)|*.cer;*.crt|Todos os ficheiros (*.*)|*.*");
        AddFolderRow(layout, 6, "Pasta dos CSV do Portal", _downloadDirectory);

        var note = new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            MaximumSize = new Size(730, 0),
            Padding = new Padding(0, 12, 0, 0),
            Text = "O utilizador e a senha WFA são configurados por empresa. Para certifphc.pfx, a chave pode ser lida da configuração PHC apenas em memória; certificados próprios continuam a aceitar introdução manual."
        };
        layout.Controls.Add(note, 0, 7);
        layout.SetColumnSpan(note, 3);
        return layout;
    }

    private Control BuildPhcConfiguration()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        var header = new Panel { Dock = DockStyle.Top, Height = 72 };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = UiTheme.Primary,
            Location = new Point(0, 0),
            Text = "Instalação e empresas PHC"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 40),
            Text = "As empresas são atualizadas a partir do EP.DBF. O NIF é associado ao abrir o primeiro SAF-T."
        });

        var folderLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(0, 3, 0, 3)
        };
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        folderLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = UiTheme.Text,
            Text = "Pasta da instalação PHC"
        }, 0, 0);
        folderLayout.Controls.Add(_phcDirectory, 1, 0);
        var browse = new Button { Dock = DockStyle.Fill, Text = "Procurar…" };
        browse.Click += (_, _) => SelectPhcDirectory();
        folderLayout.Controls.Add(browse, 2, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 8)
        };
        var refresh = new Button { AutoSize = true, Padding = new Padding(10, 4, 10, 4), Text = "Atualizar do EP.DBF" };
        refresh.Click += (_, _) => RefreshPhcCatalog();
        var credentials = new Button { AutoSize = true, Padding = new Padding(10, 4, 10, 4), Text = "Testar credenciais ODBC…" };
        credentials.Click += async (_, _) => await EditSelectedOdbcCredentialsAsync();
        actions.Controls.Add(refresh);
        actions.Controls.Add(credentials);
        actions.Controls.Add(_phcStatus);

        var companySettings = BuildSelectedCompanySettings();

        var note = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            ForeColor = UiTheme.Muted,
            MaximumSize = new Size(900, 0),
            Padding = new Padding(0, 8, 0, 0),
            Text = "O ficheiro de configuração completo é protegido pelo Windows para o utilizador atual. As palavras-passe nunca são mostradas nesta grelha."
        };

        panel.Controls.Add(_phcCompanies);
        panel.Controls.Add(note);
        panel.Controls.Add(companySettings);
        panel.Controls.Add(actions);
        panel.Controls.Add(folderLayout);
        panel.Controls.Add(header);
        return panel;
    }

    private Control BuildSelectedCompanySettings()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Bottom,
            Height = 380,
            Padding = new Padding(10),
            Text = "Configuração da empresa selecionada"
        };
        _companySettingsGroup = group;
        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(_selectedCompany, 0, 0);
        layout.SetColumnSpan(_selectedCompany, 3);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        options.Controls.Add(_createCustomers);
        options.Controls.Add(_createProducts);
        options.Controls.Add(_moveStock);
        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Importação futura" }, 0, 1);
        layout.Controls.Add(options, 1, 1);
        layout.SetColumnSpan(options, 2);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Senha Portal (NIF)" }, 0, 2);
        layout.Controls.Add(_atPassword, 1, 2);
        layout.SetColumnSpan(_atPassword, 2);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Utilizador WFA" }, 0, 3);
        layout.Controls.Add(_companyWfaUsername, 1, 3);
        layout.SetColumnSpan(_companyWfaUsername, 2);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Senha WFA" }, 0, 4);
        layout.Controls.Add(_wfaPassword, 1, 4);
        layout.SetColumnSpan(_wfaPassword, 2);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Auditoria e-Fatura" }, 0, 5);
        var auditOptions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        auditOptions.Controls.Add(AuditOptionLabel("Total ± €"));
        auditOptions.Controls.Add(_auditTotalTolerance);
        auditOptions.Controls.Add(AuditOptionLabel("IVA ± €"));
        auditOptions.Controls.Add(_auditVatTolerance);
        auditOptions.Controls.Add(AuditOptionLabel("Dias ±"));
        auditOptions.Controls.Add(_auditDateToleranceDays);
        auditOptions.Controls.Add(AuditOptionLabel("Sufixo mínimo"));
        auditOptions.Controls.Add(_auditDocumentSuffixLength);
        layout.Controls.Add(auditOptions, 1, 5);
        layout.SetColumnSpan(auditOptions, 2);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Cliente genérico PHC" }, 0, 6);
        layout.Controls.Add(_genericCustomer, 1, 6);
        var loadClients = new Button { Dock = DockStyle.Fill, Text = "Carregar clientes CL" };
        loadClients.Click += async (_, _) => await LoadClientsAsync();
        layout.Controls.Add(loadClients, 2, 6);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Lançamento de faturas" }, 0, 7);
        layout.Controls.Add(_invoiceImportMode, 1, 7);
        layout.SetColumnSpan(_invoiceImportMode, 2);

        var vatToolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        vatToolbar.Controls.Add(_loadArticles);
        vatToolbar.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(12, 7, 0, 0),
            Text = "Relacionar cada taxa de IVA do SAF-T com um artigo PHC."
        });
        _vatMappingArea.Controls.Add(_vatArticleMappings);
        _vatMappingArea.Controls.Add(vatToolbar);
        layout.Controls.Add(_vatMappingArea, 0, 8);
        layout.SetColumnSpan(_vatMappingArea, 3);

        layout.Controls.Add(new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = "Séries SAF-T → PHC" }, 0, 9);
        var seriesMappings = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Configurar faturação e recebimentos…"
        };
        seriesMappings.Click += async (_, _) => await OpenSeriesMappingsAsync();
        layout.Controls.Add(seriesMappings, 1, 9);
        layout.SetColumnSpan(seriesMappings, 2);
        group.Controls.Add(layout);
        return group;
    }

    private void Confirm()
    {
        SaveSelectedCompanySettings();
        var taxId = _customerTaxId.Text.Trim();
        if (taxId.Length > 0 && (taxId.Length != 9 || !taxId.All(char.IsDigit)))
        {
            MessageBox.Show(this, "O NIF do adquirente deve ter 9 algarismos.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ApplyPhcGridToConfiguration())
        {
            return;
        }

        if (!ValidateWfaSettings())
        {
            return;
        }

        if (!ValidateInvoiceImportSettings())
        {
            return;
        }

        var result = Configuration.Clone();
        result.CustomerTaxId = taxId;
        result.ProducerPfxPath = _pfxPath.Text.Trim();
        result.AuthenticationCertificatePath = _authenticationCertificatePath.Text.Trim();
        result.AtEnvironment = _environment.SelectedIndex == 0
                ? EfaturaAtEnvironment.Production
                : EfaturaAtEnvironment.Test;
        result.PortalDownloadDirectory = string.IsNullOrWhiteSpace(_downloadDirectory.Text)
                ? AccountingAiConfiguration.DefaultPortalDownloadDirectory
                : Path.GetFullPath(_downloadDirectory.Text.Trim());
        result.PhcDirectory = string.IsNullOrWhiteSpace(_phcDirectory.Text)
            ? string.Empty
            : Path.GetFullPath(_phcDirectory.Text.Trim());
        Configuration = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectPhcDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecionar a pasta que contém o EP.DBF da instalação PHC",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(_phcDirectory.Text))
        {
            dialog.InitialDirectory = _phcDirectory.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _phcDirectory.Text = dialog.SelectedPath;
            RefreshPhcCatalog();
        }
    }

    private void RefreshPhcCatalog()
    {
        try
        {
            var catalog = new PhcCompanyCatalogReader().Read(_phcDirectory.Text);
            Configuration.PhcDirectory = Path.GetDirectoryName(catalog.FilePath) ?? _phcDirectory.Text.Trim();
            var result = PhcCompanyCatalogSynchronizer.Synchronize(Configuration, catalog.Companies);
            RefreshPhcCompaniesGrid();
            _phcStatus.Text = $"{result.Available:N0} empresa(s) — {Path.GetFileName(catalog.FilePath)}";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Empresas PHC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task EditSelectedOdbcCredentialsAsync()
    {
        if (_phcCompanies.CurrentRow?.Tag is not PhcCompanyConfiguration company || company.OdbcName.Length == 0)
        {
            MessageBox.Show(this, "Selecione uma empresa que tenha um ODBC definido.", "Credenciais PHC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existingCredential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        using var dialog = new PhcCredentialDialog(
            company.OdbcName,
            existingCredential?.Username ?? string.Empty,
            existingCredential?.Password ?? string.Empty);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var candidate = new PhcOdbcCredential
        {
            OdbcName = company.OdbcName,
            Username = dialog.Username,
            Password = dialog.Password
        };
        UseWaitCursor = true;
        try
        {
            var result = await new PhcOdbcDataSource().TestConnectionAsync(company, candidate);
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Ligação PHC válida" : "Falha na ligação PHC",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            if (!result.Success)
            {
                return;
            }

            candidate.LastTestSucceeded = true;
            candidate.LastTestedUtc = DateTimeOffset.UtcNow;
            if (existingCredential is null)
            {
                Configuration.PhcOdbcCredentials.Add(candidate);
            }
            else
            {
                existingCredential.Username = candidate.Username;
                existingCredential.Password = candidate.Password;
                existingCredential.LastTestSucceeded = candidate.LastTestSucceeded;
                existingCredential.LastTestedUtc = candidate.LastTestedUtc;
            }

            RefreshPhcCompaniesGrid(company);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task LoadClientsAsync()
    {
        if (_phcCompanies.CurrentRow?.Tag is not PhcCompanyConfiguration company)
        {
            MessageBox.Show(this, "Selecione uma empresa PHC.", "Clientes PHC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        if (credential is null || credential.Username.Length == 0 || credential.Password.Length == 0 || !credential.LastTestSucceeded)
        {
            await EditSelectedOdbcCredentialsAsync();
            credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
                item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
            if (credential is null || !credential.LastTestSucceeded)
            {
                return;
            }
        }

        UseWaitCursor = true;
        try
        {
            var clients = await new PhcOdbcDataSource().GetClientsAsync(company, credential);
            _updatingCompanySettings = true;
            _genericCustomer.Items.Clear();
            _genericCustomer.Items.AddRange(clients.Cast<object>().ToArray());
            var preferred = clients.FirstOrDefault(client => company.GenericCustomerNumber == client.Number)
                            ?? clients.FirstOrDefault(client => client.TaxRegistrationNumber.Equals("999999990", StringComparison.Ordinal))
                            ?? clients.FirstOrDefault();
            if (preferred is not null)
            {
                _genericCustomer.SelectedItem = preferred;
                company.GenericCustomerNumber = preferred.Number;
                company.GenericCustomerName = preferred.Name;
                company.GenericCustomerTaxId = preferred.TaxRegistrationNumber;
            }

            _phcStatus.Text = $"{clients.Count:N0} cliente(s) lido(s) da tabela CL.";
        }
        catch (Exception exception) when (exception is System.Data.Odbc.OdbcException or InvalidOperationException or ArgumentException)
        {
            credential.LastTestSucceeded = false;
            MessageBox.Show(this, $"Não foi possível ler os clientes da tabela CL: {exception.Message}", "Clientes PHC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshPhcCompaniesGrid(company);
        }
        finally
        {
            _updatingCompanySettings = false;
            UseWaitCursor = false;
        }
    }

    private async Task LoadStockArticlesAsync()
    {
        SaveSelectedCompanySettings();
        if (_phcCompanies.CurrentRow?.Tag is not PhcCompanyConfiguration company)
        {
            MessageBox.Show(this, "Selecione uma empresa PHC.", "Artigos PHC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        if (credential is null || credential.Username.Length == 0 || credential.Password.Length == 0 || !credential.LastTestSucceeded)
        {
            await EditSelectedOdbcCredentialsAsync();
            credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
                item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
            if (credential is null || !credential.LastTestSucceeded)
            {
                return;
            }
        }

        UseWaitCursor = true;
        try
        {
            var articles = await new PhcOdbcDataSource().GetStockArticlesAsync(company, credential);
            _stockArticles[CompanyCatalogKey(company)] = articles;
            _updatingCompanySettings = true;
            PopulateVatArticleGrid(company);
            _phcStatus.Text = $"{articles.Count:N0} artigo(s) lido(s) da tabela ST.";
        }
        catch (Exception exception) when (exception is System.Data.Odbc.OdbcException or InvalidOperationException or ArgumentException)
        {
            credential.LastTestSucceeded = false;
            MessageBox.Show(
                this,
                $"Não foi possível ler os artigos da tabela ST: {exception.Message}",
                "Artigos PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            RefreshPhcCompaniesGrid(company);
        }
        finally
        {
            _updatingCompanySettings = false;
            UseWaitCursor = false;
        }
    }

    private async Task OpenSeriesMappingsAsync()
    {
        SaveSelectedCompanySettings();
        if (!ApplyPhcGridToConfiguration())
        {
            return;
        }

        if (_phcCompanies.CurrentRow?.Tag is not PhcCompanyConfiguration company)
        {
            MessageBox.Show(this, "Selecione uma empresa PHC.", "Séries SAF-T e PHC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (company.TaxRegistrationNumber.Length == 0)
        {
            MessageBox.Show(
                this,
                "Associe primeiro o contribuinte do SAF-T a esta empresa PHC.",
                "Séries SAF-T e PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        if (credential is null || credential.Username.Length == 0 || credential.Password.Length == 0 || !credential.LastTestSucceeded)
        {
            await EditSelectedOdbcCredentialsAsync();
            credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
                item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
            if (credential is null || !credential.LastTestSucceeded)
            {
                return;
            }
        }

        UseWaitCursor = true;
        try
        {
            var invoiceSeries = SaftSeriesExtractor.ExtractInvoices(_saftData, company.TaxRegistrationNumber);
            var receiptSeries = SaftSeriesExtractor.ExtractReceipts(_saftData, company.TaxRegistrationNumber);
            var dataSource = new PhcOdbcDataSource();
            var invoiceCatalog = await dataSource.GetInvoiceDocumentTypesAsync(company, credential);
            var receiptCatalog = await dataSource.GetReceiptDocumentTypesAsync(company, credential);
            UseWaitCursor = false;

            using var dialog = new PhcSeriesMappingsDialog(
                company.Name,
                invoiceSeries,
                receiptSeries,
                invoiceCatalog,
                receiptCatalog,
                company.InvoiceSeriesMappings,
                company.ReceiptSeriesMappings,
                _layouts);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            company.InvoiceSeriesMappings = dialog.InvoiceMappings.Select(mapping => mapping.Clone()).ToList();
            company.ReceiptSeriesMappings = dialog.ReceiptMappings.Select(mapping => mapping.Clone()).ToList();
            _phcStatus.Text =
                $"{company.InvoiceSeriesMappings.Count:N0} série(s) de faturação e {company.ReceiptSeriesMappings.Count:N0} série(s) de recebimentos configuradas.";
        }
        catch (Exception exception) when (exception is System.Data.Odbc.OdbcException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            MessageBox.Show(
                this,
                $"Não foi possível carregar os documentos PHC de TD e TSRE/RETD: {exception.Message}",
                "Séries SAF-T e PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void LoadSelectedCompanySettings()
    {
        _updatingCompanySettings = true;
        try
        {
            var company = _phcCompanies.CurrentRow?.Tag as PhcCompanyConfiguration;
            var enabled = company is not null;
            _selectedCompany.Text = enabled
                ? $"{company!.Name} — NIF {(company.TaxRegistrationNumber.Length > 0 ? company.TaxRegistrationNumber : "por associar")}"
                : "Selecione uma empresa na grelha.";
            _createCustomers.Enabled = enabled;
            _createProducts.Enabled = enabled;
            _moveStock.Enabled = enabled && company!.CreateProducts;
            _atPassword.Enabled = enabled;
            _companyWfaUsername.Enabled = enabled;
            _wfaPassword.Enabled = enabled;
            _genericCustomer.Enabled = enabled;
            _invoiceImportMode.Enabled = enabled;
            _auditTotalTolerance.Enabled = enabled;
            _auditVatTolerance.Enabled = enabled;
            _auditDateToleranceDays.Enabled = enabled;
            _auditDocumentSuffixLength.Enabled = enabled;
            _loadArticles.Enabled = enabled;
            _createCustomers.Checked = company?.CreateCustomers ?? false;
            _createProducts.Checked = company?.CreateProducts ?? false;
            _moveStock.Checked = company?.CreateProducts == true && company.MoveStock;
            _atPassword.Text = company?.AtPassword ?? string.Empty;
            _companyWfaUsername.Text = company?.WfaUsername ?? string.Empty;
            _wfaPassword.Text = company?.WfaPassword ?? string.Empty;
            _auditTotalTolerance.Value = ClampNumeric(
                _auditTotalTolerance,
                company?.PurchaseAuditTotalTolerance ?? PurchaseAuditOptions.Default.TotalTolerance);
            _auditVatTolerance.Value = ClampNumeric(
                _auditVatTolerance,
                company?.PurchaseAuditVatTolerance ?? PurchaseAuditOptions.Default.VatTolerance);
            _auditDateToleranceDays.Value = ClampNumeric(
                _auditDateToleranceDays,
                company?.PurchaseAuditDateToleranceDays ?? PurchaseAuditOptions.Default.DateToleranceDays);
            _auditDocumentSuffixLength.Value = ClampNumeric(
                _auditDocumentSuffixLength,
                company?.PurchaseAuditDocumentSuffixLength ?? PurchaseAuditOptions.Default.MinimumDocumentSuffixLength);
            _genericCustomer.Items.Clear();
            if (company?.GenericCustomerNumber is not null)
            {
                var saved = new PhcClient(
                    company.GenericCustomerNumber.Value,
                    company.GenericCustomerName,
                    company.GenericCustomerTaxId);
                _genericCustomer.Items.Add(saved);
                _genericCustomer.SelectedItem = saved;
            }

            _invoiceImportMode.SelectedItem = _invoiceImportMode.Items
                .Cast<InvoiceImportModeOption>()
                .FirstOrDefault(option => option.Mode == (company?.InvoiceImportMode ?? PhcInvoiceImportMode.DocumentsAsInSaft));
            PopulateVatArticleGrid(company);
            UpdateVatMappingVisibility();
        }
        finally
        {
            _updatingCompanySettings = false;
        }
    }

    private void SaveSelectedCompanySettings()
    {
        if (_updatingCompanySettings || _phcCompanies.CurrentRow?.Tag is not PhcCompanyConfiguration company)
        {
            return;
        }

        company.CreateCustomers = _createCustomers.Checked;
        company.CreateProducts = _createProducts.Checked;
        company.MoveStock = _createProducts.Checked && _moveStock.Checked;
        company.AtPassword = _atPassword.Text;
        company.WfaUsername = _companyWfaUsername.Text.Trim();
        company.WfaPassword = _wfaPassword.Text;
        company.PurchaseAuditTotalTolerance = _auditTotalTolerance.Value;
        company.PurchaseAuditVatTolerance = _auditVatTolerance.Value;
        company.PurchaseAuditDateToleranceDays = Decimal.ToInt32(_auditDateToleranceDays.Value);
        company.PurchaseAuditDocumentSuffixLength = Decimal.ToInt32(_auditDocumentSuffixLength.Value);
        if (_invoiceImportMode.SelectedItem is InvoiceImportModeOption importMode)
        {
            company.InvoiceImportMode = importMode.Mode;
        }

        SaveVatArticleMappings(company);
        if (_genericCustomer.SelectedItem is PhcClient client)
        {
            company.GenericCustomerNumber = client.Number;
            company.GenericCustomerName = client.Name;
            company.GenericCustomerTaxId = client.TaxRegistrationNumber;
        }
    }

    private void PopulateVatArticleGrid(PhcCompanyConfiguration? company)
    {
        _vatArticleMappings.Rows.Clear();
        var rateColumn = (DataGridViewComboBoxColumn)_vatArticleMappings.Columns["VatRate"]!;
        var articleColumn = (DataGridViewComboBoxColumn)_vatArticleMappings.Columns["PhcArticle"]!;
        if (company is null)
        {
            rateColumn.DataSource = Array.Empty<SaftVatRate>();
            articleColumn.DataSource = Array.Empty<PhcArticle>();
            return;
        }

        var rates = SaftVatRateExtractor.Extract(_saftData, company.TaxRegistrationNumber)
            .Concat(company.VatArticleMappings.Select(mapping => new SaftVatRate(mapping.VatRate)))
            .GroupBy(rate => rate.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(rate => rate.Rate)
            .ToList();
        var articles = (_stockArticles.TryGetValue(CompanyCatalogKey(company), out var loadedArticles)
                ? loadedArticles
                : Array.Empty<PhcArticle>())
            .Concat(company.VatArticleMappings
                .Where(mapping => mapping.PhcArticleReference.Length > 0)
                .Select(mapping => new PhcArticle(mapping.PhcArticleReference, mapping.PhcArticleDesignation)))
            .GroupBy(article => article.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(article => article.Reference, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        rateColumn.DataSource = rates;
        articleColumn.DataSource = articles;

        foreach (var rate in rates)
        {
            var mapping = company.VatArticleMappings.FirstOrDefault(item => item.VatRate == rate.Rate);
            _vatArticleMappings.Rows.Add(rate.Key, mapping?.PhcArticleReference ?? string.Empty);
        }
    }

    private void SaveVatArticleMappings(PhcCompanyConfiguration company)
    {
        _vatArticleMappings.EndEdit();
        var rates = ((DataGridViewComboBoxColumn)_vatArticleMappings.Columns["VatRate"]!).DataSource as IEnumerable<SaftVatRate>
                    ?? [];
        var articles = ((DataGridViewComboBoxColumn)_vatArticleMappings.Columns["PhcArticle"]!).DataSource as IEnumerable<PhcArticle>
                       ?? [];
        var rateItems = rates.ToDictionary(rate => rate.Key, StringComparer.OrdinalIgnoreCase);
        var articleItems = articles
            .GroupBy(article => article.Reference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var mappings = new List<PhcVatArticleMapping>();
        foreach (DataGridViewRow row in _vatArticleMappings.Rows)
        {
            var rateKey = Convert.ToString(row.Cells["VatRate"].Value)?.Trim() ?? string.Empty;
            var articleReference = Convert.ToString(row.Cells["PhcArticle"].Value)?.Trim() ?? string.Empty;
            if (!rateItems.TryGetValue(rateKey, out var rate) ||
                !articleItems.TryGetValue(articleReference, out var article))
            {
                continue;
            }

            mappings.Add(new PhcVatArticleMapping
            {
                VatRate = rate.Rate,
                PhcArticleReference = article.Reference,
                PhcArticleDesignation = article.Designation
            });
        }

        company.VatArticleMappings = mappings
            .GroupBy(mapping => mapping.VatRate)
            .Select(group => group.First())
            .OrderBy(mapping => mapping.VatRate)
            .ToList();
    }

    private void UpdateVatMappingVisibility()
    {
        var visible = _invoiceImportMode.SelectedItem is InvoiceImportModeOption option &&
                      option.Mode != PhcInvoiceImportMode.DocumentsAsInSaft;
        _vatMappingArea.Visible = visible;
        if (_companySettingsGroup is not null)
        {
            _companySettingsGroup.Height = visible ? 460 : 315;
        }
    }

    private static string CompanyCatalogKey(PhcCompanyConfiguration company) =>
        $"{company.OdbcName}\u001f{company.DatabaseName}";

    private void RefreshPhcCompaniesGrid(PhcCompanyConfiguration? companyToSelect = null)
    {
        companyToSelect ??= _phcCompanies.CurrentRow?.Tag as PhcCompanyConfiguration;
        _updatingCompanySettings = true;
        _phcCompanies.Rows.Clear();
        foreach (var company in Configuration.PhcCompanies
                     .OrderByDescending(company => company.PresentInEp)
                     .ThenBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var credential = Configuration.PhcOdbcCredentials.FirstOrDefault(item =>
                item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
            var rowIndex = _phcCompanies.Rows.Add(
                company.PresentInEp,
                company.Name,
                company.OdbcName,
                company.DatabaseName,
                company.TaxRegistrationNumber,
                CredentialStatus(credential));
            _phcCompanies.Rows[rowIndex].Tag = company;
            if (ReferenceEquals(company, companyToSelect))
            {
                _phcCompanies.CurrentCell = _phcCompanies.Rows[rowIndex].Cells["Nome"];
            }
        }

        if (_phcCompanies.CurrentCell is null && _phcCompanies.Rows.Count > 0)
        {
            _phcCompanies.CurrentCell = _phcCompanies.Rows[0].Cells["Nome"];
        }

        _updatingCompanySettings = false;

        if (Configuration.PhcCompanies.Count == 0)
        {
            _phcStatus.Text = "Ainda não foram lidas empresas do EP.DBF.";
        }

        LoadSelectedCompanySettings();
    }

    private static string CredentialStatus(PhcOdbcCredential? credential)
    {
        if (credential is null || credential.Username.Length == 0 || credential.Password.Length == 0)
        {
            return "Por indicar";
        }

        return credential.LastTestSucceeded
            ? $"Testadas em {credential.LastTestedUtc?.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "Por testar";
    }

    private bool ApplyPhcGridToConfiguration()
    {
        var assignedTaxIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _phcCompanies.Rows)
        {
            if (row.Tag is not PhcCompanyConfiguration company)
            {
                continue;
            }

            var taxId = Convert.ToString(row.Cells["Nif"].Value)?.Trim() ?? string.Empty;
            if (taxId.Length > 0 && (taxId.Length != 9 || !taxId.All(char.IsDigit)))
            {
                MessageBox.Show(this, $"O contribuinte da empresa {company.Name} deve ter 9 algarismos.", "Empresas PHC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (taxId.Length > 0 && !assignedTaxIds.Add(taxId))
            {
                MessageBox.Show(this, $"O contribuinte {taxId} está associado a mais do que uma empresa PHC.", "Empresas PHC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            company.TaxRegistrationNumber = taxId;
        }

        return true;
    }

    private bool ValidateWfaSettings()
    {
        foreach (var company in Configuration.PhcCompanies)
        {
            var username = company.WfaUsername.Trim();
            var hasPassword = company.WfaPassword.Length > 0;
            if (username.Length == 0 && !hasPassword)
            {
                continue;
            }

            if (username.Length == 0 || !hasPassword)
            {
                MessageBox.Show(
                    this,
                    $"Indique o utilizador e a senha WFA da empresa {company.Name}.",
                    "Autoridade Tributária",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            var parts = username.Split('/', 2);
            if (parts.Length != 2 || parts[0].Length != 9 || !parts[0].All(char.IsDigit) ||
                parts[1].Length == 0 || !parts[1].All(char.IsDigit))
            {
                MessageBox.Show(
                    this,
                    $"O utilizador WFA da empresa {company.Name} deve estar no formato NIF/Utilizador.",
                    "Autoridade Tributária",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (company.TaxRegistrationNumber.Length > 0 &&
                !parts[0].Equals(company.TaxRegistrationNumber, StringComparison.Ordinal))
            {
                MessageBox.Show(
                    this,
                    $"O NIF do utilizador WFA não coincide com o contribuinte da empresa {company.Name}.",
                    "Autoridade Tributária",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        return true;
    }

    private static DataGridView BuildPhcCompaniesGrid()
    {
        var grid = UiTheme.CreateGrid();
        grid.AutoGenerateColumns = false;
        grid.Dock = DockStyle.Fill;
        grid.MultiSelect = false;
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Ativa", HeaderText = "No EP.DBF", ReadOnly = true, Width = 75 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nome", HeaderText = "Empresa", ReadOnly = true, MinimumWidth = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Odbc", HeaderText = "ODBC", ReadOnly = true, MinimumWidth = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BaseDados", HeaderText = "Base de dados", ReadOnly = true, MinimumWidth = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nif", HeaderText = "Contribuinte", ReadOnly = false, MinimumWidth = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Credenciais", HeaderText = "Credenciais ODBC", ReadOnly = true, MinimumWidth = 130 });
        return grid;
    }

    private static DataGridView BuildVatArticleMappingsGrid()
    {
        var grid = UiTheme.CreateGrid();
        grid.AllowUserToAddRows = false;
        grid.AutoGenerateColumns = false;
        grid.DataError += (_, eventArgs) => eventArgs.ThrowException = false;
        grid.Dock = DockStyle.Fill;
        grid.MultiSelect = false;
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DisplayMember = nameof(SaftVatRate.DisplayName),
            DisplayStyleForCurrentCellOnly = true,
            HeaderText = "Taxa de IVA do SAF-T",
            MinimumWidth = 220,
            Name = "VatRate",
            ValueMember = nameof(SaftVatRate.Key),
            Width = 280
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DisplayMember = nameof(PhcArticle.DisplayName),
            DisplayStyleForCurrentCellOnly = true,
            HeaderText = "Artigo PHC (ST)",
            MinimumWidth = 400,
            Name = "PhcArticle",
            ValueMember = nameof(PhcArticle.Reference),
            Width = 620
        });
        return grid;
    }

    private bool ValidateInvoiceImportSettings()
    {
        foreach (var company in Configuration.PhcCompanies.Where(company =>
                     company.InvoiceImportMode != PhcInvoiceImportMode.DocumentsAsInSaft))
        {
            if (company.InvoiceImportMode == PhcInvoiceImportMode.OneDocumentForGenericCustomer &&
                company.GenericCustomerNumber is null)
            {
                MessageBox.Show(
                    this,
                    $"Selecione o Cliente genérico PHC para a empresa {company.Name}.",
                    "Configuração de lançamento",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            var rates = SaftVatRateExtractor.Extract(_saftData, company.TaxRegistrationNumber);
            var mappedRates = company.VatArticleMappings
                .Where(mapping => mapping.PhcArticleReference.Length > 0)
                .Select(mapping => mapping.VatRate)
                .ToHashSet();
            var missingRates = rates.Where(rate => !mappedRates.Contains(rate.Rate)).ToList();
            if (missingRates.Count == 0)
            {
                continue;
            }

            MessageBox.Show(
                this,
                $"Relacione todas as taxas de IVA da empresa {company.Name} com artigos PHC. " +
                $"Faltam: {string.Join(", ", missingRates.Select(rate => rate.DisplayName))}.",
                "Configuração de lançamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static void AddHeader(TableLayoutPanel layout, string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 70 };
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
            Location = new Point(2, 40),
            Text = subtitle
        });
        layout.Controls.Add(panel, 0, 0);
        layout.SetColumnSpan(panel, 3);
        layout.SetRowSpan(panel, 2);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Text, Text = caption };
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

    private static void AddFolderRow(TableLayoutPanel layout, int row, string caption, TextBox field)
    {
        var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Text, Text = caption };
        var browse = new Button { Dock = DockStyle.Fill, Text = "Procurar…" };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Selecionar a pasta onde serão guardados os CSV descarregados do e-Fatura",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };
            if (Directory.Exists(field.Text))
            {
                dialog.InitialDirectory = field.Text;
            }

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                field.Text = dialog.SelectedPath;
            }
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
        layout.Controls.Add(browse, 2, row);
    }

    private static TextBox Field() => new() { BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill };

    private static NumericUpDown AuditMoneyTolerance() => new()
    {
        DecimalPlaces = 2,
        Increment = 0.05m,
        Maximum = 1000m,
        Width = 74,
        TextAlign = HorizontalAlignment.Right,
        ThousandsSeparator = true
    };

    private static Label AuditOptionLabel(string text) => new()
    {
        AutoSize = true,
        Margin = new Padding(10, 6, 3, 0),
        Text = text
    };

    private static decimal ClampNumeric(NumericUpDown control, decimal value) =>
        Math.Min(control.Maximum, Math.Max(control.Minimum, value));

    private sealed record InvoiceImportModeOption(PhcInvoiceImportMode Mode, string Description)
    {
        public override string ToString() => Description;
    }
}
