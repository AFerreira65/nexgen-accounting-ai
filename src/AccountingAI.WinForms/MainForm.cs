using AccountingAI.Core.Configuration;
using AccountingAI.Core.Efatura;
using AccountingAI.Core.Models;
using AccountingAI.Core.Parsing;
using AccountingAI.Core.Phc;
using AccountingAI.WinForms.Controls;
using AccountingAI.WinForms.Dialogs;

namespace AccountingAI.WinForms;

internal sealed class MainForm : Form
{
    private readonly string[] _startupArguments;
    private readonly TabControl _tabs;
    private readonly ToolStrip _toolStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripProgressBar _progress;
    private readonly ToolStripButton _openFilesButton;
    private readonly ToolStripButton _openFolderButton;
    private readonly ToolStripButton _importEfaturaButton;
    private readonly ToolStripButton _portalEfaturaButton;
    private readonly ToolStripButton _readEfaturaButton;
    private readonly ToolStripButton _launchPhcButton;
    private readonly ConfigurationStore _configurationStore;
    private readonly GridLayoutManager _gridLayouts;
    private AccountingAiConfiguration _configuration;
    private string? _phcStartupMessage;
    private SaftDataSet _data = new();
    private string[] _lastLoadedPaths = [];

    public MainForm(string[] startupArguments)
    {
        _startupArguments = startupArguments;
        Text = "Accounting AI";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1540, 920);
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 9F);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _configurationStore = new ConfigurationStore();
        _configuration = _configurationStore.Load();
        _gridLayouts = new GridLayoutManager(_configuration, _configurationStore);
        var menuStrip = BuildMenuStrip();
        _toolStrip = BuildToolStrip();
        _openFilesButton = (ToolStripButton)_toolStrip.Items[0];
        _openFolderButton = (ToolStripButton)_toolStrip.Items[1];
        _importEfaturaButton = (ToolStripButton)_toolStrip.Items[3];
        _portalEfaturaButton = (ToolStripButton)_toolStrip.Items[4];
        _readEfaturaButton = (ToolStripButton)_toolStrip.Items[5];
        _launchPhcButton = (ToolStripButton)_toolStrip.Items[8];
        _launchPhcButton.Enabled = false;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Padding = new Point(16, 6)
        };

        var statusStrip = new StatusStrip { SizingGrip = true };
        _statusLabel = new ToolStripStatusLabel("Pronto") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new ToolStripProgressBar
        {
            Alignment = ToolStripItemAlignment.Right,
            MarqueeAnimationSpeed = 25,
            Size = new Size(160, 16),
            Style = ProgressBarStyle.Marquee,
            Visible = false
        };
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_progress);
        _gridLayouts.SaveFailed += exception =>
            _statusLabel.Text = $"Não foi possível guardar a largura das colunas: {exception.Message}";

        Controls.Add(_tabs);
        Controls.Add(_toolStrip);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);
        MainMenuStrip = menuStrip;

        if (_configurationStore.LastLoadError is null)
        {
            RefreshPhcCatalogAtStartup();
        }
        else
        {
            _phcStartupMessage = "A configuração encriptada não pôde ser aberta e foi preservada sem alterações.";
        }
        ShowWelcomePage();
        if (!string.IsNullOrWhiteSpace(_phcStartupMessage))
        {
            _statusLabel.Text = _phcStartupMessage;
        }
        Shown += async (_, _) => await LoadStartupFilesAsync();
        FormClosing += (_, _) =>
        {
            if (!_gridLayouts.Flush() && _gridLayouts.LastSaveError is not null)
            {
                MessageBox.Show(
                    this,
                    _gridLayouts.LastSaveError.Message,
                    "Guardar largura das colunas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        FormClosed += (_, _) => _gridLayouts.Dispose();
    }

    private MenuStrip BuildMenuStrip()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9F)
        };
        var configuration = new ToolStripMenuItem("Configuração");
        configuration.DropDownItems.Add(new ToolStripMenuItem(
            "Configuração geral e empresas PHC…",
            null,
            (_, _) => ShowConfiguration()));
        menu.Items.Add(configuration);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            BackColor = UiTheme.PrimaryDark,
            ForeColor = Color.White,
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(24, 24),
            Padding = new Padding(10, 7, 10, 7),
            RenderMode = ToolStripRenderMode.System
        };

        strip.Items.Add(CreateButton("Abrir SAF-T", async (_, _) => await SelectFilesAsync()));
        strip.Items.Add(CreateButton("Abrir pasta", async (_, _) => await SelectFolderAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(CreateButton("Importar compras e-Fatura (CSV)", (_, _) => ImportEfaturaCsv()));
        strip.Items.Add(CreateButton("Portal e-Fatura (assistido)", (_, _) => OpenEfaturaPortal()));
        strip.Items.Add(CreateButton("Consultar compras na AT (webservice)", async (_, _) => await QueryEfaturaAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(CreateButton("Recarregar", async (_, _) => await ReloadAsync()));
        strip.Items.Add(CreateButton("Lançar no PHC", async (_, _) => await LaunchInPhcAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(CreateButton("Sobre", (_, _) => ShowAbout()));

        return strip;
    }

    private static ToolStripButton CreateButton(string text, EventHandler onClick)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(3, 0, 3, 0),
            Padding = new Padding(7, 4, 7, 4)
        };
        button.Click += onClick;
        return button;
    }

    private async Task LoadStartupFilesAsync()
    {
        if (_startupArguments.Length == 0)
        {
            return;
        }

        var paths = ExpandPaths(_startupArguments);
        if (paths.Length > 0)
        {
            await LoadSaftFilesAsync(paths);
        }
    }

    private async Task SelectFilesAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Ficheiro SAF-T (*.xml)|*.xml|Todos os ficheiros (*.*)|*.*",
            Multiselect = true,
            Title = "Selecionar ficheiros SAF-T (PT)"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadSaftFilesAsync(dialog.FileNames);
        }
    }

    private async Task SelectFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecionar uma pasta com ficheiros SAF-T (PT)",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var paths = Directory.GetFiles(dialog.SelectedPath, "*.xml", SearchOption.TopDirectoryOnly);
            if (paths.Length == 0)
            {
                MessageBox.Show(this, "A pasta selecionada não contém ficheiros XML.", "Accounting AI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await LoadSaftFilesAsync(paths);
        }
    }

    private async Task LoadSaftFilesAsync(string[] paths)
    {
        SetBusy(true, $"A ler {paths.Length:N0} ficheiro(s)…");
        try
        {
            var parser = new SaftParser();
            _data = await Task.Run(() => parser.ParseFiles(paths));
            _lastLoadedPaths = paths;
            AssociatePhcCompaniesFromSaft();
            BuildTabs();
            var errors = _data.ValidationIssues.Count(issue => issue.Severity == ValidationSeverity.Error);
            _statusLabel.Text = errors == 0
                ? $"{_data.SourceFiles.Count:N0} ficheiro(s) carregado(s)"
                : $"Leitura concluída com {errors:N0} erro(s) — consulte Validações";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Erro ao ler SAF-T", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Não foi possível concluir a leitura.";
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }
    }

    private async Task ReloadAsync()
    {
        if (_lastLoadedPaths.Length > 0)
        {
            await LoadSaftFilesAsync(_lastLoadedPaths);
        }
    }

    private void ImportEfaturaCsv()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Ficheiros CSV (*.csv;*.txt)|*.csv;*.txt|Todos os ficheiros (*.*)|*.*",
            Multiselect = true,
            Title = "Importar compras exportadas do e-Fatura"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ImportEfaturaCsvFiles(dialog.FileNames, "CSV selecionado(s)");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Microsoft.VisualBasic.FileIO.MalformedLineException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao importar e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenEfaturaPortal()
    {
        var (suggestedStart, suggestedEnd) = SuggestedDateRange();
        var company = CurrentPhcCompany();
        using var dialog = new EfaturaPortalDialog(
            suggestedStart,
            suggestedEnd,
            _configuration.PortalDownloadDirectory,
            company?.TaxRegistrationNumber ?? SuggestedTaxId(),
            company?.AtPassword ?? string.Empty,
            path => ImportEfaturaCsvFiles([path], "Portal e-Fatura"));
        dialog.ShowDialog(this);
    }

    private int ImportEfaturaCsvFiles(IEnumerable<string> paths, string source)
    {
        try
        {
            var imported = new EfaturaCsvImporter().ImportDetailed(paths);
            _data.MergeTable(imported.Purchases);
            _data.MergeTable(imported.Lines);
            _data.MergeTable(imported.Batches);
            foreach (var warning in imported.Warnings)
            {
                _data.ValidationIssues.Add(new ValidationIssue(ValidationSeverity.Warning, "e-Fatura CSV", "Lotes", warning));
            }

            BuildTabs();
            _statusLabel.Text = $"{source}: {imported.Purchases.Rows.Count:N0} compra(s) e {imported.Lines.Rows.Count:N0} linha(s) fiscal(is) importadas de {imported.Batches.Rows.Count:N0} CSV.";
            SelectTab("e-Fatura");
            return imported.Purchases.Rows.Count;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Microsoft.VisualBasic.FileIO.MalformedLineException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao importar e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return -1;
        }
    }

    private async Task QueryEfaturaAsync()
    {
        var (suggestedStart, suggestedEnd) = SuggestedDateRange();
        var company = CurrentPhcCompany();
        var pfxPath = new[]
            {
                _configuration.ProducerPfxPath,
                @"C:\PHC\_LOC\certifphc.pfx",
                @"C:\PHC_LOC\certifphc.pfx"
            }
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ??
            _configuration.ProducerPfxPath;
        var pfxPassword = company is null
            ? string.Empty
            : PhcAtCertificatePasswordReader.TryRead(
                _configuration.PhcDirectory,
                company.DatabaseName,
                pfxPath);
        var wfaPassword = company is null
            ? string.Empty
            : !string.IsNullOrEmpty(company.WfaPassword)
                ? company.WfaPassword
                : company.AtPassword;
        using var dialog = new EfaturaQueryDialog(
            suggestedStart,
            suggestedEnd,
            company?.TaxRegistrationNumber ?? SuggestedTaxId(),
            _configuration,
            company?.WfaUsername ?? string.Empty,
            wfaPassword,
            pfxPassword);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, "A preparar a ligação segura ao e-Fatura…");
        try
        {
            using var connection = EfaturaAtConnection.Load(
                dialog.Username,
                dialog.PortalPassword,
                dialog.CustomerTaxId,
                dialog.PfxPath,
                dialog.PfxPassword,
                dialog.AuthenticationCertificatePath,
                dialog.Environment);
            using var source = new AtEfaturaPurchaseSource(connection);
            var progress = new Progress<EfaturaQueryProgress>(update => _statusLabel.Text = update.Message);
            var result = await source.GetPurchasesAsync(dialog.StartDate, dialog.EndDate, progress);
            _data.MergeTable(result.Purchases);
            _data.MergeTable(result.Lines);
            foreach (var message in result.Messages)
            {
                _data.ValidationIssues.Add(new ValidationIssue(
                    ValidationSeverity.Information,
                    "AT e-Fatura",
                    "Consulta",
                    message));
            }

            BuildTabs();
            _statusLabel.Text = $"Consulta AT concluída: {result.Purchases.Rows.Count:N0} compra(s).";
            SelectTab("e-Fatura");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                           System.Security.Cryptography.CryptographicException or
                                           HttpRequestException or InvalidOperationException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Consulta e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "A consulta ao e-Fatura não foi concluída.";
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }
    }

    private void ShowConfiguration()
    {
        using var dialog = new ConfigurationDialog(
            _configuration,
            _gridLayouts,
            _data,
            SuggestedTaxId());
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _gridLayouts.CopyLayoutsTo(dialog.Configuration);
            _configurationStore.Save(dialog.Configuration);
            _configuration = dialog.Configuration;
            _gridLayouts.UseConfiguration(_configuration);
            _statusLabel.Text = $"Configuração guardada em {_configurationStore.FilePath}";
            UpdatePhcLaunchAvailability();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "Guardar configuração", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LaunchInPhcAsync()
    {
        var company = CurrentPhcCompany();
        if (company is null)
        {
            MessageBox.Show(
                this,
                "O contribuinte do SAF-T não está associado a uma empresa PHC. Abra Configuração > Empresas PHC e faça a associação.",
                "Lançar no PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (company.InvoiceImportMode != PhcInvoiceImportMode.OneDocumentForGenericCustomer)
        {
            MessageBox.Show(
                this,
                "Nesta fase só está disponível o modo 'Agrupa 1 documento no Cliente genérico'. Selecione-o na configuração da empresa.",
                "Lançar no PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var credential = await EnsurePhcCredentialAsync(company);
        if (credential is null)
        {
            _statusLabel.Text = "Lançamento PHC não iniciado: a ligação ODBC não foi validada.";
            MessageBox.Show(
                this,
                "Não foi efetuada qualquer gravação. O lançamento parou antes da pré-visualização porque a ligação ODBC não foi validada.",
                "Lançamento PHC não iniciado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        List<PhcAggregatedInvoiceTarget> targets;
        var service = new PhcAggregatedInvoiceService();
        SetBusy(true, "A validar os documentos e a configuração PHC…");
        try
        {
            var drafts = PhcAggregatedInvoiceBuilder.BuildDocuments(_data, company.TaxRegistrationNumber);
            targets = new List<PhcAggregatedInvoiceTarget>(drafts.Count);
            foreach (var draft in drafts)
            {
                targets.Add(await service.PrepareAsync(company, credential, draft));
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Não é possível lançar no PHC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "O lançamento PHC não foi iniciado.";
            return;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }

        using var preview = new PhcLaunchPreviewDialog(targets, _gridLayouts);
        if (preview.ShowDialog(this) != DialogResult.OK)
        {
            _statusLabel.Text = "Lançamento PHC cancelado antes da gravação.";
            MessageBox.Show(
                this,
                "O lançamento foi cancelado na pré-visualização. Não foi efetuada qualquer gravação no PHC.",
                "Lançamento PHC cancelado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, $"A criar {targets.Count:N0} documento(s) em FT, FT2, FT3, FTT, FI e FI2 no PHC…");
        try
        {
            var results = await service.CreateManyAsync(targets, credential);
            var firstNumber = results.Min(result => result.SequentialNumber);
            var lastNumber = results.Max(result => result.SequentialNumber);
            var numberText = firstNumber == lastNumber
                ? firstNumber.ToString("N0")
                : $"{firstNumber:N0} a {lastNumber:N0}";
            _statusLabel.Text = $"{results.Count:N0} documento(s) PHC criado(s), n.º {numberText}.";
            var details = string.Join(
                "\n",
                results.Zip(targets, (result, target) =>
                    $"• {result.SequentialNumber:N0} — {target.Draft.GroupLabel}: {result.GrossTotal:N2} € " +
                    $"({result.LinesInserted:N0} linha(s), {result.TaxTotalsInserted:N0} total(is) FTT)"));
            MessageBox.Show(
                this,
                $"{results.Count:N0} documento(s) criado(s) com sucesso numa única transação.\n\n" +
                $"Documento PHC: {targets[0].DocumentName}\n" +
                $"Data: {targets[0].Draft.DocumentDate:dd-MM-yyyy}\n\n" +
                details,
                "Lançamento PHC concluído",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Os documentos não foram criados. A transação conjunta foi anulada.\n\n{exception.Message}",
                "Erro ao lançar no PHC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _statusLabel.Text = "Falha no lançamento PHC — nenhuma gravação foi confirmada.";
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }
    }

    private async Task<PhcOdbcCredential?> EnsurePhcCredentialAsync(PhcCompanyConfiguration company)
    {
        if (string.IsNullOrWhiteSpace(company.OdbcName))
        {
            MessageBox.Show(this, "A empresa PHC não tem um ODBC definido.", "Ligação PHC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var credential = _configuration.PhcOdbcCredentials.FirstOrDefault(item =>
            item.OdbcName.Equals(company.OdbcName, StringComparison.OrdinalIgnoreCase));
        if (credential is { LastTestSucceeded: true } &&
            credential.Username.Length > 0 && credential.Password.Length > 0)
        {
            return credential;
        }

        using var dialog = new PhcCredentialDialog(
            company.OdbcName,
            credential?.Username ?? string.Empty,
            credential?.Password ?? string.Empty);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        var candidate = new PhcOdbcCredential
        {
            OdbcName = company.OdbcName,
            Username = dialog.Username,
            Password = dialog.Password
        };
        SetBusy(true, "A testar o utilizador e a palavra-passe do ODBC…");
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
                return null;
            }

            candidate.LastTestSucceeded = true;
            candidate.LastTestedUtc = DateTimeOffset.UtcNow;
            if (credential is null)
            {
                _configuration.PhcOdbcCredentials.Add(candidate);
                credential = candidate;
            }
            else
            {
                credential.Username = candidate.Username;
                credential.Password = candidate.Password;
                credential.LastTestSucceeded = true;
                credential.LastTestedUtc = candidate.LastTestedUtc;
            }

            _configurationStore.Save(_configuration);
            return credential;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Falha na ligação PHC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }
    }

    private void RefreshPhcCatalogAtStartup()
    {
        try
        {
            var phcDirectory = _configuration.PhcDirectory;
            if (string.IsNullOrWhiteSpace(phcDirectory))
            {
                var localDirectory = @"C:\PHC_LOC";
                var defaultDirectory = @"C:\PHC";
                if (File.Exists(Path.Combine(localDirectory, "EP.DBF")))
                {
                    phcDirectory = localDirectory;
                }
                else if (File.Exists(Path.Combine(defaultDirectory, "EP.DBF")))
                {
                    phcDirectory = defaultDirectory;
                }
            }

            if (string.IsNullOrWhiteSpace(phcDirectory))
            {
                _phcStartupMessage = "Configure a pasta PHC para ler as empresas do EP.DBF.";
                return;
            }

            var catalog = new PhcCompanyCatalogReader().Read(phcDirectory);
            _configuration.PhcDirectory = phcDirectory;
            var result = PhcCompanyCatalogSynchronizer.Synchronize(_configuration, catalog.Companies);
            _configurationStore.Save(_configuration);
            _phcStartupMessage = $"PHC: {result.Available:N0} empresa(s) atualizada(s) a partir de {catalog.FilePath}.";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                           UnauthorizedAccessException or InvalidOperationException)
        {
            _phcStartupMessage = $"Não foi possível atualizar as empresas PHC: {exception.Message}";
        }
    }

    private void AssociatePhcCompaniesFromSaft()
    {
        var taxPayers = _data.Tables.TryGetValue(SaftTableKeys.Header, out var headers)
            ? headers.Rows
                .Where(record => record["TaxRegistrationNumber"].Length == 9 && record["TaxRegistrationNumber"].All(char.IsDigit))
                .GroupBy(record => record["TaxRegistrationNumber"], StringComparer.Ordinal)
                .Select(group => new
                {
                    TaxId = group.Key,
                    CompanyName = group.Select(record => record["CompanyName"]).FirstOrDefault(name => name.Length > 0) ?? string.Empty
                })
            : [];

        var changed = false;
        foreach (var taxPayer in taxPayers)
        {
            if (_configuration.PhcCompanies.Any(company =>
                    company.TaxRegistrationNumber.Equals(taxPayer.TaxId, StringComparison.Ordinal)))
            {
                continue;
            }

            if (_configuration.PhcCompanies.Count == 0)
            {
                _data.ValidationIssues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "PHC",
                    "Associação de empresa",
                    $"O contribuinte {taxPayer.TaxId} ainda não pode ser associado porque não foram lidas empresas do EP.DBF."));
                continue;
            }

            using var dialog = new PhcCompanyAssociationDialog(
                taxPayer.TaxId,
                taxPayer.CompanyName,
                _configuration.PhcCompanies);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedCompany is null)
            {
                _data.ValidationIssues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "PHC",
                    "Associação de empresa",
                    $"O contribuinte {taxPayer.TaxId} do SAF-T ainda não está associado a uma empresa PHC."));
                continue;
            }

            foreach (var company in _configuration.PhcCompanies.Where(company =>
                         company.TaxRegistrationNumber.Equals(taxPayer.TaxId, StringComparison.Ordinal)))
            {
                company.TaxRegistrationNumber = string.Empty;
            }

            dialog.SelectedCompany.TaxRegistrationNumber = taxPayer.TaxId;
            changed = true;
        }

        if (changed)
        {
            _configurationStore.Save(_configuration);
        }
    }

    private void BuildTabs()
    {
        _tabs.SuspendLayout();
        _tabs.TabPages.Clear();
        _tabs.TabPages.Add(CreateTab("Resumo", new SummaryPage(_data, _gridLayouts)));
        _tabs.TabPages.Add(CreateTableTab("Cabeçalho", SaftTableKeys.Header));
        _tabs.TabPages.Add(CreateGroupTab("Tabelas mestre",
            ("Clientes", SaftTableKeys.Customers),
            ("Fornecedores", SaftTableKeys.Suppliers),
            ("Produtos", SaftTableKeys.Products),
            ("Impostos", SaftTableKeys.Taxes)));
        _tabs.TabPages.Add(CreateDocumentTab("Faturação", SaftTableKeys.Invoices, SaftTableKeys.InvoiceLines, "InvoiceNo"));
        _tabs.TabPages.Add(CreateDocumentTab("Transporte", SaftTableKeys.StockMovements, SaftTableKeys.StockMovementLines, "DocumentNumber"));
        _tabs.TabPages.Add(CreateDocumentTab("Conferência", SaftTableKeys.WorkDocuments, SaftTableKeys.WorkDocumentLines, "DocumentNumber"));
        _tabs.TabPages.Add(CreateDocumentTab("Recibos", SaftTableKeys.Payments, SaftTableKeys.PaymentLines, "PaymentRefNo"));
        _tabs.TabPages.Add(CreateAccountingTab());
        _tabs.TabPages.Add(CreateEfaturaTab());
        _tabs.TabPages.Add(CreatePurchaseAuditTab());
        _tabs.TabPages.Add(CreateTab("Validações", new RecordGridPage(
            BuildIssuesTable(),
            layouts: _gridLayouts,
            gridKey: "validacoes")));
        _tabs.ResumeLayout();
    }

    private TabPage CreateTableTab(string title, string tableKey, string? emptyMessage = null)
    {
        _data.Tables.TryGetValue(tableKey, out var table);
        return CreateTab(
            WithCount(title, table?.Rows.Count ?? 0),
            new RecordGridPage(
                table,
                emptyMessage ?? "Sem registos nesta secção.",
                _gridLayouts,
                tableKey));
    }

    private TabPage CreateGroupTab(string title, params (string Title, string Key)[] tables)
    {
        var nested = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            Padding = new Point(14, 5)
        };
        var total = 0;
        foreach (var definition in tables)
        {
            _data.Tables.TryGetValue(definition.Key, out var table);
            var count = table?.Rows.Count ?? 0;
            total += count;
            nested.TabPages.Add(CreateTab(
                WithCount(definition.Title, count),
                new RecordGridPage(table, layouts: _gridLayouts, gridKey: definition.Key)));
        }

        return CreateTab(WithCount(title, total), nested);
    }

    private TabPage CreateDocumentTab(string title, string documentsKey, string linesKey, string documentKey)
    {
        _data.Tables.TryGetValue(documentsKey, out var documents);
        _data.Tables.TryGetValue(linesKey, out var lines);
        return CreateTab(
            WithCount(title, documents?.Rows.Count ?? 0),
            new DocumentLinesPage(documents, lines, documentKey, _gridLayouts, documentsKey));
    }

    private TabPage CreateAccountingTab()
    {
        var nested = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        _data.Tables.TryGetValue(SaftTableKeys.Accounts, out var accounts);
        _data.Tables.TryGetValue(SaftTableKeys.Journals, out var journals);
        _data.Tables.TryGetValue(SaftTableKeys.Transactions, out var transactions);
        _data.Tables.TryGetValue(SaftTableKeys.AccountingLines, out var lines);
        nested.TabPages.Add(CreateTab(
            WithCount("Plano de contas", accounts?.Rows.Count ?? 0),
            new RecordGridPage(accounts, layouts: _gridLayouts, gridKey: SaftTableKeys.Accounts)));
        nested.TabPages.Add(CreateTab(
            WithCount("Diários", journals?.Rows.Count ?? 0),
            new RecordGridPage(journals, layouts: _gridLayouts, gridKey: SaftTableKeys.Journals)));
        nested.TabPages.Add(CreateTab(
            WithCount("Documentos e lançamentos", transactions?.Rows.Count ?? 0),
            new DocumentLinesPage(
                transactions,
                lines,
                "TransactionID",
                _gridLayouts,
                SaftTableKeys.Transactions)));
        var total = (accounts?.Rows.Count ?? 0) + (journals?.Rows.Count ?? 0) +
                    (transactions?.Rows.Count ?? 0) + (lines?.Rows.Count ?? 0);
        return CreateTab(WithCount("Contabilidade", total), nested);
    }

    private TabPage CreateEfaturaTab()
    {
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchases, out var purchases);
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchaseLines, out var lines);
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaCsvBatches, out var batches);
        var nested = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        nested.TabPages.Add(CreateTab(
            WithCount("Compras e linhas", purchases?.Rows.Count ?? 0),
            new DocumentLinesPage(
                purchases,
                lines,
                "DocumentKey",
                _gridLayouts,
                SaftTableKeys.EfaturaPurchases)));
        nested.TabPages.Add(CreateTab(
            WithCount("Lotes CSV", batches?.Rows.Count ?? 0),
            new RecordGridPage(
                batches,
                "Ainda não foram importados ficheiros CSV do e-Fatura.",
                _gridLayouts,
                SaftTableKeys.EfaturaCsvBatches)));
        return CreateTab(WithCount("e-Fatura", purchases?.Rows.Count ?? 0), nested);
    }

    private TabPage CreatePurchaseAuditTab()
    {
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchases, out var purchases);
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchaseLines, out var lines);
        var company = CurrentPhcCompany();
        var options = company is null
            ? PurchaseAuditOptions.Default
            : PurchaseAuditOptions.FromCompany(company);
        var pending = new PhcPurchaseAuditService().CreatePending(purchases, lines, options);
        return CreateTab(
            WithCount("Auditoria Compras PHC", purchases?.Rows.Count ?? 0),
            new PurchaseAuditPage(pending, RunPurchaseAuditAsync, _gridLayouts));
    }

    private async Task<PurchaseAuditResult?> RunPurchaseAuditAsync(DateOnly searchStart, DateOnly searchEnd)
    {
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchases, out var purchases);
        _data.Tables.TryGetValue(SaftTableKeys.EfaturaPurchaseLines, out var lines);
        if (purchases is null || purchases.Rows.Count == 0)
        {
            MessageBox.Show(
                this,
                "Importe primeiro as compras do e-Fatura para efetuar a auditoria.",
                "Auditoria de compras",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        var company = CurrentPhcCompany();
        if (company is null)
        {
            MessageBox.Show(
                this,
                "O contribuinte dos dados carregados não está associado a uma empresa PHC. Abra Configuração > Empresas PHC e faça a associação.",
                "Auditoria de compras",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        var credential = await EnsurePhcCredentialAsync(company);
        if (credential is null)
        {
            _statusLabel.Text = "Auditoria PHC não iniciada: a ligação ODBC não foi validada.";
            return null;
        }

        SetBusy(true, "A consultar documentos de compra e taxas de IVA no PHC…");
        try
        {
            var result = await new PhcPurchaseAuditService().AuditAsync(
                company,
                credential,
                purchases,
                lines,
                searchStart,
                searchEnd,
                PurchaseAuditOptions.FromCompany(company));
            _statusLabel.Text = "Auditoria de compras concluída: " + result.Summary;
            return result;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Falha na auditoria de compras PHC.";
            MessageBox.Show(
                this,
                "Não foi possível consultar os documentos de compra no PHC. A auditoria é apenas de leitura e não alterou dados.\n\n" + exception,
                "Erro na auditoria de compras",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? "Pronto");
        }
    }

    private RecordTable BuildIssuesTable()
    {
        var table = new RecordTable("validation", "Validações");
        foreach (var issue in _data.ValidationIssues.OrderByDescending(issue => issue.Severity))
        {
            var record = new SaftRecord(issue.SourceFile, "Validação")
            {
                ["Severidade"] = issue.Severity switch
                {
                    ValidationSeverity.Error => "Erro",
                    ValidationSeverity.Warning => "Aviso",
                    _ => "Informação"
                },
                ["Área"] = issue.Area,
                ["Mensagem"] = issue.Message
            };
            table.Add(record);
        }

        return table;
    }

    private void ShowWelcomePage()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        var content = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Location = new Point(60, 60),
            Padding = new Padding(24)
        };
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 24F),
            ForeColor = UiTheme.Primary,
            Text = "Accounting AI"
        });
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 11F),
            ForeColor = UiTheme.Text,
            MaximumSize = new Size(760, 0),
            Margin = new Padding(3, 12, 3, 20),
            Text = "Abra um ou vários ficheiros SAF-T (PT). O monitor deteta faturação, contabilidade, ficheiros integrados e secções parciais, apresentando cada conjunto de dados numa grelha pesquisável."
        });
        var openButton = new Button
        {
            AutoSize = true,
            BackColor = UiTheme.Primary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.White,
            Padding = new Padding(16, 8, 16, 8),
            Text = "Abrir ficheiros SAF-T"
        };
        openButton.FlatAppearance.BorderSize = 0;
        openButton.Click += async (_, _) => await SelectFilesAsync();
        content.Controls.Add(openButton);
        panel.Controls.Add(content);
        _tabs.TabPages.Add(CreateTab("Início", panel));
    }

    private void SetBusy(bool busy, string status)
    {
        _openFilesButton.Enabled = !busy;
        _openFolderButton.Enabled = !busy;
        _importEfaturaButton.Enabled = !busy;
        _portalEfaturaButton.Enabled = !busy;
        _readEfaturaButton.Enabled = !busy;
        _launchPhcButton.Enabled = !busy && HasInvoicesLoaded();
        _progress.Visible = busy;
        _statusLabel.Text = status;
        UseWaitCursor = busy;
    }

    private bool HasInvoicesLoaded() =>
        _data.Tables.TryGetValue(SaftTableKeys.Invoices, out var invoices) && invoices.Rows.Count > 0;

    private void UpdatePhcLaunchAvailability() => _launchPhcButton.Enabled = HasInvoicesLoaded();

    private void SelectTab(string startsWith)
    {
        var tab = _tabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(page => page.Text.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase));
        if (tab is not null)
        {
            _tabs.SelectedTab = tab;
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "Accounting AI\n\nLeitor SAF-T (PT) 1.03_01 e 1.04_01.\nA validação apresentada é estrutural e analítica; não substitui o validador oficial da Autoridade Tributária.",
            "Sobre",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnDragEnter(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var paths = ExpandPaths(dropped);
            if (paths.Length > 0)
            {
                await LoadSaftFilesAsync(paths);
            }
        }
    }

    private static string[] ExpandPaths(IEnumerable<string> paths) => paths
        .SelectMany(path => Directory.Exists(path)
            ? Directory.GetFiles(path, "*.xml", SearchOption.TopDirectoryOnly)
            : File.Exists(path) && Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)
                ? [path]
                : [])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private (DateOnly Start, DateOnly End) SuggestedDateRange()
    {
        var starts = _data.SourceFiles
            .Select(file => DateOnly.TryParseExact(file.StartDate, "yyyy-MM-dd", out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        var ends = _data.SourceFiles
            .Select(file => DateOnly.TryParseExact(file.EndDate, "yyyy-MM-dd", out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToList();
        if (starts.Count > 0 && ends.Count > 0)
        {
            return (starts.Min(), ends.Max());
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        return (new DateOnly(today.Year, today.Month, 1), today);
    }

    private string SuggestedTaxId()
    {
        if (!_data.Tables.TryGetValue(SaftTableKeys.Header, out var headers))
        {
            return string.Empty;
        }

        return headers.Rows.Select(record => record["TaxRegistrationNumber"])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private PhcCompanyConfiguration? CurrentPhcCompany()
    {
        var taxId = SuggestedTaxId();
        return taxId.Length == 0
            ? null
            : _configuration.PhcCompanies.FirstOrDefault(company =>
                company.TaxRegistrationNumber.Equals(taxId, StringComparison.Ordinal));
    }

    private static TabPage CreateTab(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = UiTheme.Canvas, Padding = new Padding(3) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static string WithCount(string title, int count) => count > 0 ? $"{title} ({count:N0})" : title;
}
