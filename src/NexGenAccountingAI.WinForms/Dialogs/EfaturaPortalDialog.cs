using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NexGenAccountingAI.Core.Efatura;

namespace NexGenAccountingAI.WinForms.Dialogs;

internal sealed class EfaturaPortalDialog : Form
{
    private const string PortalUrl = "https://faturas.portaldasfinancas.gov.pt/";
    private const string DocumentsUrl = "https://faturas.portaldasfinancas.gov.pt/consultarDocumentosAdquirente.action";
    private readonly WebView2 _browser;
    private readonly DateTimePicker _startDate;
    private readonly DateTimePicker _endDate;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _extract;
    private readonly Button _cancelExtraction;
    private readonly Func<string, int> _onCsvDownloaded;
    private readonly string _downloadDirectory;
    private readonly string _taxId;
    private readonly string _password;
    private CancellationTokenSource? _extractionCancellation;
    private TaskCompletionSource<PortalDownloadResult>? _downloadCompletion;
    private DateOnly? _activeDay;
    private bool _browserReady;
    private bool _running;

    public EfaturaPortalDialog(
        DateOnly suggestedStart,
        DateOnly suggestedEnd,
        string downloadDirectory,
        string taxId,
        string password,
        Func<string, int> onCsvDownloaded)
    {
        ArgumentNullException.ThrowIfNull(onCsvDownloaded);
        _onCsvDownloaded = onCsvDownloaded;
        _downloadDirectory = Path.GetFullPath(downloadDirectory);
        _taxId = taxId.Trim();
        _password = password;

        Text = "NexGen Accounting AI — Portal e-Fatura (automático)";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1150, 740);
        Size = new Size(1500, 920);
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 9F);

        _browser = new WebView2 { Dock = DockStyle.Fill, BackColor = Color.White };
        _startDate = DateField(suggestedStart);
        _endDate = DateField(suggestedEnd);
        _status = new Label
        {
            AutoEllipsis = true,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            Padding = new Padding(10, 3, 10, 5),
            Text = $"Intervalo sugerido pelo SAF-T: {suggestedStart:dd/MM/yyyy} a {suggestedEnd:dd/MM/yyyy}."
        };
        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 18,
            Margin = new Padding(12, 3, 12, 5),
            Style = ProgressBarStyle.Continuous
        };
        _extract = CreateButton("Entrar e extrair intervalo", async (_, _) => await ExtractRangeAsync(), primary: true);
        _extract.Enabled = false;
        _cancelExtraction = CreateButton("Cancelar extração", (_, _) => _extractionCancellation?.Cancel());
        _cancelExtraction.Enabled = false;
        var home = CreateButton("Início e-Fatura", (_, _) => NavigateHome());
        var downloads = CreateButton("Abrir downloads", (_, _) => OpenDownloads());
        var clearSession = CreateButton("Limpar sessão", async (_, _) => await ClearSessionAsync());

        var commands = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false
        };
        commands.Controls.Add(Caption("Data inicial:"));
        commands.Controls.Add(_startDate);
        commands.Controls.Add(Caption("Data final:"));
        commands.Controls.Add(_endDate);
        commands.Controls.Add(_extract);
        commands.Controls.Add(_cancelExtraction);
        commands.Controls.Add(home);
        commands.Controls.Add(downloads);
        commands.Controls.Add(clearSession);

        var header = new TableLayoutPanel
        {
            AutoSize = true,
            BackColor = UiTheme.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(6),
            RowCount = 4
        };
        header.Controls.Add(commands);
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Padding = new Padding(10, 2, 10, 4),
            Text = "A aplicação inicia sessão com o NIF e a palavra-passe guardados e consulta um dia de cada vez. " +
                   "Cada CSV é importado antes de avançar. Se a AT pedir CMD, código adicional ou CAPTCHA, a extração para sem contornar essa proteção."
        });
        header.Controls.Add(_progress);
        header.Controls.Add(_status);

        Controls.Add(_browser);
        Controls.Add(header);
        Shown += async (_, _) => await InitializeBrowserAsync();
        FormClosing += (_, _) => _extractionCancellation?.Cancel();
    }

    private static DateTimePicker DateField(DateOnly value) => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        Value = value.ToDateTime(TimeOnly.MinValue),
        Width = 118
    };

    private static Label Caption(string text) => new()
    {
        AutoSize = true,
        Margin = new Padding(9, 9, 3, 0),
        Text = text
    };

    private static Button CreateButton(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            AutoSize = true,
            BackColor = primary ? UiTheme.Primary : Color.White,
            FlatStyle = FlatStyle.Flat,
            ForeColor = primary ? Color.White : UiTheme.Primary,
            Margin = new Padding(5, 2, 0, 2),
            Padding = new Padding(8, 3, 8, 3),
            Text = text
        };
        button.FlatAppearance.BorderColor = primary ? UiTheme.Primary : UiTheme.Border;
        button.Click += click;
        return button;
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            Directory.CreateDirectory(_downloadDirectory);
            var userDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexGen Accounting AI",
                "e-Fatura",
                "BrowserProfile");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDirectory);
            await _browser.EnsureCoreWebView2Async(environment);
            _browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            _browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            _browser.CoreWebView2.DownloadStarting += OnDownloadStarting;
            _browserReady = true;
            _extract.Enabled = true;
            _browser.CoreWebView2.Navigate(PortalUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                this,
                "Não foi encontrado o Microsoft Edge WebView2 Runtime. Instale o runtime oficial da Microsoft e volte a tentar.",
                "Portal e-Fatura",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "Portal e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task ExtractRangeAsync()
    {
        if (!_browserReady || _running || _browser.CoreWebView2 is null)
        {
            return;
        }

        if (_taxId.Length != 9 || !_taxId.All(char.IsDigit) || _password.Length == 0)
        {
            MessageBox.Show(
                this,
                "A empresa do SAF-T não tem um NIF e uma palavra-passe do e-Fatura válidos guardados na configuração.",
                "Portal e-Fatura",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        IReadOnlyList<EfaturaPortalDayQuery> days;
        try
        {
            days = EfaturaPortalExtractionPlan.CreateDailyQueries(
                DateOnly.FromDateTime(_startDate.Value),
                DateOnly.FromDateTime(_endDate.Value));
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "Intervalo e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        _extract.Enabled = false;
        _cancelExtraction.Enabled = true;
        _startDate.Enabled = false;
        _endDate.Enabled = false;
        _progress.Minimum = 0;
        _progress.Maximum = days.Count;
        _progress.Value = 0;
        _extractionCancellation = new CancellationTokenSource();
        var cancellationToken = _extractionCancellation.Token;
        var downloadedDays = 0;
        var emptyDays = 0;
        var daysAtPortalLimit = new List<DateOnly>();

        try
        {
            _status.Text = "A abrir a autenticação da AT…";
            _browser.CoreWebView2.Navigate(DocumentsUrl);
            await EnsureAuthenticatedAsync(cancellationToken);

            for (var index = 0; index < days.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = days[index];
                var day = query.StartDate;
                _activeDay = day;
                _progress.Value = index;
                _status.Text = $"Dia {index + 1:N0} de {days.Count:N0}: a consultar {day:dd/MM/yyyy}…";

                await EnsureAuthenticatedAsync(cancellationToken);
                var search = await FillAndSearchDayAsync(day, cancellationToken);
                if (!search)
                {
                    throw new InvalidOperationException(
                        "A página da AT não apresentou os dois campos de data e o botão Pesquisar esperados.");
                }

                var result = await WaitForResultsAsync(cancellationToken);
                if (result == "empty")
                {
                    emptyDays++;
                    _progress.Value = index + 1;
                    _status.Text = $"{day:dd/MM/yyyy}: sem documentos. A avançar…";
                    continue;
                }

                _downloadCompletion = new TaskCompletionSource<PortalDownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!await ClickExportAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"A AT apresentou resultados para {day:dd/MM/yyyy}, mas não foi encontrado o comando 'Obter dados para Excel'.");
                }

                _status.Text = $"{day:dd/MM/yyyy}: a aguardar o CSV…";
                var download = await _downloadCompletion.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
                if (download.ImportedDocuments >= 300)
                {
                    daysAtPortalLimit.Add(day);
                }
                downloadedDays++;
                _downloadCompletion = null;
                _progress.Value = index + 1;
            }

            _activeDay = null;
            var limitWarning = daysAtPortalLimit.Count == 0
                ? string.Empty
                : $"\nAtenção: {daysAtPortalLimit.Count:N0} dia(s) atingiram 300 documentos e devem ser confirmados.";
            _status.Text = $"Extração concluída: {days.Count:N0} dia(s), {downloadedDays:N0} CSV, {emptyDays:N0} dia(s) sem documentos." + limitWarning;
            MessageBox.Show(
                this,
                $"Extração concluída.\n\nDias consultados: {days.Count:N0}\nCSV importados: {downloadedDays:N0}\nDias sem documentos: {emptyDays:N0}" + limitWarning,
                "Portal e-Fatura",
                MessageBoxButtons.OK,
                daysAtPortalLimit.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            _downloadCompletion?.TrySetCanceled();
            _status.Text = "Extração cancelada. Os CSV já concluídos permanecem importados.";
        }
        catch (TimeoutException)
        {
            _status.Text = "A AT não respondeu dentro do tempo esperado. A extração foi interrompida.";
            MessageBox.Show(this, _status.Text, "Portal e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException)
        {
            _status.Text = $"Extração interrompida: {exception.Message}";
            MessageBox.Show(this, exception.Message, "Portal e-Fatura", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _downloadCompletion = null;
            _activeDay = null;
            _extractionCancellation?.Dispose();
            _extractionCancellation = null;
            _running = false;
            _extract.Enabled = _browserReady;
            _cancelExtraction.Enabled = false;
            _startDate.Enabled = true;
            _endDate.Enabled = true;
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? loginSubmittedAt = null;
        await Task.Delay(500, cancellationToken);
        for (var attempt = 0; attempt < 240; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _browser.Source?.ToString() ?? _browser.CoreWebView2?.Source ?? string.Empty;
            if (source.Contains("faturas.portaldasfinancas.gov.pt/consultarDocumentosAdquirente.action", StringComparison.OrdinalIgnoreCase))
            {
                if (await IsDocumentPageReadyAsync())
                {
                    return;
                }
            }

            if (source.Contains("acesso.gov.pt", StringComparison.OrdinalIgnoreCase))
            {
                if (loginSubmittedAt is null)
                {
                    var login = await SubmitLoginAsync(cancellationToken);
                    if (login.State == "submitted")
                    {
                        loginSubmittedAt = DateTimeOffset.UtcNow;
                        _status.Text = "Credenciais submetidas à AT; a aguardar autenticação…";
                    }
                    else if (login.State == "switching")
                    {
                        _status.Text = "A selecionar a autenticação por NIF…";
                    }
                    else if (login.State == "challenge")
                    {
                        throw new InvalidOperationException(
                            "A AT está a pedir CMD, autenticação multifator ou CAPTCHA. A autenticação automática foi interrompida sem contornar essa proteção.");
                    }
                    else if (login.State == "failed")
                    {
                        throw new InvalidOperationException(login.Message.Length > 0
                            ? login.Message
                            : "Não foi possível preencher automaticamente o formulário de autenticação da AT.");
                    }
                }
                else if (DateTimeOffset.UtcNow - loginSubmittedAt > TimeSpan.FromSeconds(60))
                {
                    var state = await ReadLoginStateAsync();
                    throw new InvalidOperationException(state.Message.Length > 0
                        ? $"A autenticação na AT não foi aceite: {state.Message}"
                        : "A autenticação na AT não avançou. Confirme o NIF e a palavra-passe guardados.");
                }
            }
            else if (source.Contains("faturas.portaldasfinancas.gov.pt", StringComparison.OrdinalIgnoreCase) &&
                     !source.Contains("consultarDocumentosAdquirente.action", StringComparison.OrdinalIgnoreCase))
            {
                _browser.CoreWebView2?.Navigate(DocumentsUrl);
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("A autenticação da AT excedeu o tempo limite.");
    }

    private async Task<bool> IsDocumentPageReadyAsync()
    {
        const string script = """
            (() => {
              if (document.readyState !== 'complete') return { state: 'loading', message: '' };
              const descriptor = element => `${element.name} ${element.id} ${element.placeholder} ${element.getAttribute('aria-label')}`.toLowerCase();
              const inputs = Array.from(document.querySelectorAll('input'));
              const start = inputs.some(element => /datainicio|data.inicio|data inicial|desde|startdate/.test(descriptor(element)));
              const end = inputs.some(element => /datafim|data.fim|data final|enddate/.test(descriptor(element)));
              return { state: start && end ? 'ready' : 'loading', message: '' };
            })();
            """;
        try
        {
            return (await ExecutePortalScriptAsync(script)).State == "ready";
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<PortalScriptResult> SubmitLoginAsync(CancellationToken cancellationToken)
    {
        const string prepareUserScript = """
            (() => {
              if (document.readyState !== 'complete') return { state: 'waiting', message: '' };
              const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
              const inputs = Array.from(document.querySelectorAll('input')).filter(visible);
              const user = inputs.find(element => element.name === 'username') || inputs.find(element =>
                /nif|contribuinte|utilizador/i.test(`${element.name} ${element.id} ${element.placeholder} ${element.getAttribute('aria-label')}`));
              const password = inputs.find(element => element.name === 'password' || element.type === 'password');
              if (!user || !password) {
                const message = (document.body.innerText || '').replace(/\s+/g, ' ').trim();
                const nifOption = Array.from(document.querySelectorAll('button,a,[role="tab"],[role="button"],li,div,span')).find(element =>
                  visible(element) && !element.disabled && (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim().toUpperCase() === 'NIF');
                if (nifOption) {
                  const clickable = nifOption.closest('button,a,[role="tab"],[role="button"]') || nifOption.parentElement || nifOption;
                  const bounds = clickable.getBoundingClientRect();
                  return {
                    state: 'nif-ready',
                    message: 'Separador de autenticação por NIF encontrado.',
                    x: bounds.left + bounds.width / 2,
                    y: bounds.top + bounds.height / 2
                  };
                }
                const actualChallengeControl = document.querySelector(
                  'iframe[src*="captcha" i], .g-recaptcha, input[autocomplete="one-time-code"], input[name*="otp" i], input[id*="otp" i]');
                const challenge = !!actualChallengeControl ||
                  /introduza.{0,40}(código|chave móvel)|código.{0,40}(enviado|recebido)|resolva.{0,40}captcha/i.test(message);
                return { state: challenge ? 'challenge' : 'waiting', message: message.slice(0, 300) };
              }
              user.focus();
              user.select();
              return { state: 'ready', message: '' };
            })();
            """;

        var prepared = await ExecutePortalScriptAsync(prepareUserScript);
        if (prepared.State == "nif-ready")
        {
            await ClickWithDevToolsAsync(prepared.X, prepared.Y, cancellationToken);
            await Task.Delay(350, cancellationToken);
            return new PortalScriptResult
            {
                State = "switching",
                Message = "Autenticação por NIF selecionada."
            };
        }

        if (prepared.State != "ready")
        {
            return prepared;
        }

        await InsertTextWithDevToolsAsync(_taxId, cancellationToken);
        var taxIdJson = JsonSerializer.Serialize(_taxId);
        var preparePasswordScript = $$"""
            (() => {
              const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
              const inputs = Array.from(document.querySelectorAll('input')).filter(visible);
              const user = inputs.find(element => element.name === 'username') || inputs.find(element =>
                /nif|contribuinte|utilizador/i.test(`${element.name} ${element.id} ${element.placeholder} ${element.getAttribute('aria-label')}`));
              const password = inputs.find(element => element.name === 'password' || element.type === 'password');
              if (!user || !password) return { state: 'waiting', message: '' };
              if (user.value !== {{taxIdJson}}) {
                return { state: 'failed', message: 'O NIF não ficou preenchido no formulário de autenticação da AT.' };
              }
              password.focus();
              password.select();
              return { state: 'ready', message: '' };
            })();
            """;
        prepared = await ExecutePortalScriptAsync(preparePasswordScript);
        if (prepared.State != "ready")
        {
            return prepared;
        }

        await InsertTextWithDevToolsAsync(_password, cancellationToken);
        await Task.Delay(150, cancellationToken);
        var passwordJson = JsonSerializer.Serialize(_password);
        var submitScript = $$"""
            (() => {
              const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
              const inputs = Array.from(document.querySelectorAll('input')).filter(visible);
              const user = inputs.find(element => element.name === 'username') || inputs.find(element =>
                /nif|contribuinte|utilizador/i.test(`${element.name} ${element.id} ${element.placeholder} ${element.getAttribute('aria-label')}`));
              const password = inputs.find(element => element.name === 'password' || element.type === 'password');
              if (!user || !password) return { state: 'waiting', message: '' };
              if (user.value !== {{taxIdJson}} || password.value !== {{passwordJson}}) {
                return { state: 'failed', message: 'As credenciais não ficaram preenchidas no formulário de autenticação da AT.' };
              }
              user.dispatchEvent(new Event('change', { bubbles: true }));
              password.dispatchEvent(new Event('change', { bubbles: true }));
              const form = password.closest('form') || user.closest('form');
              const submitCandidates = Array.from((form || document).querySelectorAll('button,input[type="submit"]')).filter(visible);
              const submit = submitCandidates.find(element => element.type === 'submit') || submitCandidates.find(element =>
                /autenticar|entrar|iniciar/i.test(element.innerText || element.value || element.title || ''));
              if (form && typeof form.requestSubmit === 'function') {
                form.requestSubmit(submit || undefined);
              } else if (submit) {
                submit.click();
              } else if (form) {
                form.submit();
              } else {
                return { state: 'failed', message: 'O formulário de autenticação por NIF não foi encontrado.' };
              }
              return { state: 'submitted', message: '' };
            })();
            """;
        return await ExecutePortalScriptAsync(submitScript);
    }

    private async Task<PortalScriptResult> ReadLoginStateAsync()
    {
        const string script = """
            (() => {
              const text = (document.body.innerText || '').replace(/\s+/g, ' ').trim();
              const messages = Array.from(document.querySelectorAll('[role="alert"], .alert, .error, .invalid-feedback'))
                .map(element => element.innerText.trim()).filter(Boolean).join(' ');
              return { state: 'login', message: (messages || text).slice(0, 300) };
            })();
            """;
        return await ExecutePortalScriptAsync(script);
    }

    private async Task<bool> FillAndSearchDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var portalDate = EfaturaPortalExtractionPlan.FormatPortalDate(day);
        var isoDateJson = JsonSerializer.Serialize(portalDate);
        var script = $$"""
            (() => {
              const startFields = Array.from(document.querySelectorAll('[name="dataInicioFilter"], #dataInicioFilter'));
              const endFields = Array.from(document.querySelectorAll('[name="dataFimFilter"], #dataFimFilter'));
              const form = startFields.map(element => element.closest('form')).find(Boolean) ||
                endFields.map(element => element.closest('form')).find(Boolean);
              if (!form) return { state: 'missing', message: 'Formulário de pesquisa do e-Fatura não encontrado.' };
              const candidates = Array.from((form || document).querySelectorAll('button,input[type="submit"],a'));
              const search = candidates.find(element => /pesquisar|consultar|procurar/.test((element.innerText || element.value || element.title || '').toLowerCase()));
              if (!search) return { state: 'missing', message: 'Botão Pesquisar não encontrado.' };

              const replaceField = (name, value) => {
                Array.from(form.querySelectorAll(`[name="${name}"]`)).forEach(element => element.disabled = true);
                const hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = name;
                hidden.value = value;
                hidden.setAttribute('data-saft-ia', 'true');
                form.appendChild(hidden);
              };
              replaceField('dataInicioFilter', {{isoDateJson}});
              replaceField('dataFimFilter', {{isoDateJson}});
              if (search.name) {
                const command = document.createElement('input');
                command.type = 'hidden';
                command.name = search.name;
                command.value = search.value || search.innerText || 'Pesquisar';
                form.appendChild(command);
              }
              form.noValidate = true;
              HTMLFormElement.prototype.submit.call(form);
              return { state: 'submitted', message: '', changed: 2 };
            })();
            """;

        if (_browser.CoreWebView2 is null)
        {
            throw new InvalidOperationException("O navegador e-Fatura ainda não está disponível.");
        }

        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (eventArgs.IsSuccess)
            {
                navigation.TrySetResult(true);
            }
            else
            {
                navigation.TrySetException(new InvalidOperationException(
                    $"A navegação da pesquisa e-Fatura falhou ({eventArgs.WebErrorStatus})."));
            }
        }

        _browser.CoreWebView2.NavigationCompleted += NavigationCompleted;
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecutePortalScriptAsync(script);
                if (result.State == "submitted")
                {
                    await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    return true;
                }

                if (result.State == "invalid")
                {
                    throw new InvalidOperationException(result.Message);
                }

                await Task.Delay(500, cancellationToken);
            }

            return false;
        }
        finally
        {
            _browser.CoreWebView2.NavigationCompleted -= NavigationCompleted;
        }
    }

    private async Task<string> WaitForResultsAsync(CancellationToken cancellationToken)
    {
        const string script = """
            (() => {
              const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
              const text = (document.body.innerText || '').replace(/\s+/g, ' ');
              if (/por favor, corrija os erros nos campos indicados|valor inválido/i.test(text)) {
                return { state: 'invalid', message: 'O Portal e-Fatura rejeitou as datas da pesquisa.' };
              }
              const exportControl = Array.from(document.querySelectorAll('a,button,input')).find(element => {
                const label = `${element.innerText || ''} ${element.value || ''} ${element.title || ''} ${element.getAttribute('aria-label') || ''}`;
                return visible(element) && /obter dados para excel|excel|exportar.*csv|descarregar.*csv/i.test(label);
              });
              return exportControl
                ? { state: 'ready', message: '' }
                : { state: 'waiting', message: text.slice(0, 12000) };
            })();
            """;

        await Task.Delay(800, cancellationToken);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _browser.Source?.ToString() ?? string.Empty;
            if (source.Contains("acesso.gov.pt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A sessão da AT expirou durante a extração.");
            }

            PortalScriptResult result;
            try
            {
                result = await ExecutePortalScriptAsync(script);
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }
            if (result.State is "ready" or "empty")
            {
                return result.State;
            }

            if (EfaturaPortalExtractionPlan.IsEmptyResultText(result.Message))
            {
                return "empty";
            }

            if (result.State == "invalid")
            {
                throw new InvalidOperationException(result.Message);
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("A pesquisa diária da AT excedeu o tempo limite.");
    }

    private async Task<bool> ClickExportAsync(CancellationToken cancellationToken)
    {
        const string script = """
            (() => {
              const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
              const exportControl = Array.from(document.querySelectorAll('a,button,input')).find(element => {
                const label = `${element.innerText || ''} ${element.value || ''} ${element.title || ''} ${element.getAttribute('aria-label') || ''}`;
                return visible(element) && /obter dados para excel|excel|exportar.*csv|descarregar.*csv/i.test(label);
              });
              if (!exportControl) return { state: 'missing', message: '' };
              exportControl.click();
              return { state: 'clicked', message: '' };
            })();
            """;
        cancellationToken.ThrowIfCancellationRequested();
        var result = await ExecutePortalScriptAsync(script);
        return result.State == "clicked";
    }

    private async Task InsertTextWithDevToolsAsync(string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_browser.CoreWebView2 is null)
        {
            throw new InvalidOperationException("O navegador e-Fatura ainda não está disponível.");
        }

        try
        {
            var parameters = JsonSerializer.Serialize(new { text = value });
            await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText", parameters);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "O navegador não conseguiu introduzir automaticamente os dados no formulário da AT.",
                exception);
        }
    }

    private async Task ClickWithDevToolsAsync(double x, double y, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_browser.CoreWebView2 is null || !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0)
        {
            throw new InvalidOperationException("O separador de autenticação por NIF não apresentou uma posição válida.");
        }

        try
        {
            var pressed = JsonSerializer.Serialize(new
            {
                type = "mousePressed",
                x,
                y,
                button = "left",
                clickCount = 1
            });
            var released = JsonSerializer.Serialize(new
            {
                type = "mouseReleased",
                x,
                y,
                button = "left",
                clickCount = 1
            });
            await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", pressed);
            await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", released);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "O navegador não conseguiu selecionar automaticamente a autenticação por NIF.",
                exception);
        }
    }

    private async Task<PortalScriptResult> ExecutePortalScriptAsync(string script)
    {
        if (_browser.CoreWebView2 is null)
        {
            throw new InvalidOperationException("O navegador e-Fatura ainda não está disponível.");
        }

        var json = await _browser.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<PortalScriptResult>(json) ?? new PortalScriptResult();
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs eventArgs)
    {
        var originalName = Path.GetFileName(eventArgs.ResultFilePath);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = "efatura.csv";
        }

        var safeName = string.Concat(originalName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var dayPrefix = _activeDay?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var destination = Path.Combine(
            _downloadDirectory,
            $"{dayPrefix}-{DateTime.Now:HHmmss}-{Guid.NewGuid():N}-{safeName}");
        eventArgs.ResultFilePath = destination;
        var operation = eventArgs.DownloadOperation;
        operation.StateChanged += OnDownloadStateChanged;
        _status.Text = $"{_activeDay:dd/MM/yyyy}: a descarregar {originalName}…";
    }

    private void OnDownloadStateChanged(object? sender, object eventArgs)
    {
        if (sender is not CoreWebView2DownloadOperation operation ||
            operation.State == CoreWebView2DownloadState.InProgress)
        {
            return;
        }

        operation.StateChanged -= OnDownloadStateChanged;
        if (operation.State == CoreWebView2DownloadState.Completed)
        {
            var extension = Path.GetExtension(operation.ResultFilePath);
            var isTabularDownload = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
                                    operation.MimeType.Contains("csv", StringComparison.OrdinalIgnoreCase) ||
                                    operation.MimeType.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
                                    operation.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
            if (!isTabularDownload)
            {
                _downloadCompletion?.TrySetException(new InvalidDataException(
                    $"A AT devolveu um ficheiro que não é CSV/Excel: {Path.GetFileName(operation.ResultFilePath)}."));
                return;
            }

            try
            {
                var importedDocuments = _onCsvDownloaded(operation.ResultFilePath);
                if (importedDocuments < 0)
                {
                    throw new InvalidDataException(
                        $"O CSV de {_activeDay:dd/MM/yyyy} foi descarregado, mas não pôde ser importado.");
                }
                _status.Text = $"{_activeDay:dd/MM/yyyy}: CSV descarregado e importado.";
                _downloadCompletion?.TrySetResult(new PortalDownloadResult(operation.ResultFilePath, importedDocuments));
            }
            catch (Exception exception)
            {
                _downloadCompletion?.TrySetException(exception);
            }
        }
        else
        {
            _downloadCompletion?.TrySetException(new IOException(
                $"O download não foi concluído ({operation.InterruptReason})."));
        }
    }

    private void NavigateHome()
    {
        if (_browser.CoreWebView2 is not null && !_running)
        {
            _browser.CoreWebView2.Navigate(PortalUrl);
        }
    }

    private void OpenDownloads()
    {
        Directory.CreateDirectory(_downloadDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _downloadDirectory,
            UseShellExecute = true
        });
    }

    private async Task ClearSessionAsync()
    {
        if (_browser.CoreWebView2 is null || _running)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Pretende eliminar os cookies da sessão e-Fatura guardados neste perfil?",
            "Limpar sessão",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        await _browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
        _status.Text = "Sessão local eliminada.";
        NavigateHome();
    }

    private sealed class PortalScriptResult
    {
        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("changed")]
        public int Changed { get; init; }

        [JsonPropertyName("x")]
        public double X { get; init; }

        [JsonPropertyName("y")]
        public double Y { get; init; }
    }

    private sealed record PortalDownloadResult(string FilePath, int ImportedDocuments);
}
