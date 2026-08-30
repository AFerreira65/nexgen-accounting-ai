using AccountingAI.WinForms.Controls;
using AccountingAI.WinForms.Dialogs;
using AccountingAI.WinForms;
using AccountingAI.Core.Configuration;
using AccountingAI.Core.Efatura;
using AccountingAI.Core.Models;
using AccountingAI.Core.Phc;

var documentTable = new RecordTable("invoices", "Faturas");
documentTable.Add(new SaftRecord("fixture.xml", "Fatura")
{
    ["InvoiceNo"] = "FT TESTE/1",
    ["InvoiceDate"] = "2026-08-22"
});
var lineTable = new RecordTable("invoice-lines", "Linhas de faturação");
lineTable.Add(new SaftRecord("fixture.xml", "Linha")
{
    ["InvoiceNo"] = "FT TESTE/1",
    ["LineNumber"] = "1",
    ["Description"] = "Linha de teste"
});

using var tabs = new TabControl { Size = new Size(320, 240) };
tabs.SuspendLayout();
for (var index = 0; index < 7; index++)
{
    var page = new TabPage($"Documentos {index + 1}");
    page.Controls.Add(new DocumentLinesPage(documentTable, lineTable, "InvoiceNo"));
    tabs.TabPages.Add(page);
}
tabs.TabPages.Add(new TabPage("Registos")
{
    Controls = { new RecordGridPage(documentTable) }
});
tabs.ResumeLayout(performLayout: true);
tabs.CreateControl();
tabs.PerformLayout();
tabs.Size = new Size(1200, 700);
tabs.PerformLayout();

if (tabs.TabPages.Count != 8)
{
    Console.Error.WriteLine("ERRO: nem todos os separadores documentais foram criados.");
    return 1;
}

var configuration = new AccountingAiConfiguration
{
    PhcDirectory = @"C:\PHC_LOC",
    PhcCompanies =
    [
        new PhcCompanyConfiguration
        {
            Name = "Empresa Teste",
            OdbcName = "PHC_TESTE",
            DatabaseName = "BD_TESTE",
            TaxRegistrationNumber = "500584095",
            AtPassword = "senha-at",
            WfaUsername = "500584095/3",
            WfaPassword = "senha-wfa",
            CreateCustomers = true,
            CreateProducts = true,
            MoveStock = true,
            GenericCustomerNumber = 99,
            GenericCustomerName = "Consumidor Final",
            GenericCustomerTaxId = "999999990",
            PurchaseAuditTotalTolerance = 0.50m,
            PurchaseAuditVatTolerance = 0.25m,
            PurchaseAuditDateToleranceDays = 50,
            PurchaseAuditDocumentSuffixLength = 7,
            InvoiceImportMode = PhcInvoiceImportMode.OneDocumentForGenericCustomer,
            VatArticleMappings =
            [
                new PhcVatArticleMapping
                {
                    VatRate = 23m,
                    PhcArticleReference = "SERV23",
                    PhcArticleDesignation = "Serviços à taxa normal"
                }
            ]
        }
    ],
    PhcOdbcCredentials =
    [
        new PhcOdbcCredential
        {
            OdbcName = "PHC_TESTE",
            Username = "utilizador",
            Password = "segredo",
            LastTestSucceeded = true,
            LastTestedUtc = DateTimeOffset.UtcNow
        }
    ]
};
using var configurationDialog = new ConfigurationDialog(configuration);
configurationDialog.CreateControl();
configurationDialog.PerformLayout();
var configurationControls = Descendants(configurationDialog).ToList();
var configurationLabels = configurationControls.OfType<Label>().Select(label => label.Text).ToHashSet(StringComparer.Ordinal);
var launchMode = configurationControls.OfType<ComboBox>().FirstOrDefault(combo =>
    combo.Items.Cast<object>().Any(item => item.ToString() == "Agrupa 1 documento no Cliente genérico") &&
    combo.Items.Cast<object>().Any(item => item.ToString() == "Lança todos os documentos com as linhas agrupadas pelo IVA") &&
    combo.Items.Cast<object>().Any(item => item.ToString() == "Lança os documentos como estão no SAF-T"));
var vatGrid = configurationControls.OfType<DataGridView>().FirstOrDefault(grid =>
    grid.Columns.Contains("VatRate") && grid.Columns.Contains("PhcArticle"));
var auditNumericValues = configurationControls.OfType<NumericUpDown>()
    .Select(control => control.Value)
    .ToList();
if (!configurationLabels.Contains("Utilizador WFA") || !configurationLabels.Contains("Senha WFA") ||
    !configurationLabels.Contains("Auditoria e-Fatura") ||
    !auditNumericValues.Contains(0.50m) || !auditNumericValues.Contains(0.25m) ||
    !auditNumericValues.Contains(50m) || !auditNumericValues.Contains(7m) ||
    launchMode is null || vatGrid is null || vatGrid.Rows.Count != 1 ||
    Convert.ToString(vatGrid.Rows[0].Cells["PhcArticle"].Value) != "SERV23")
{
    Console.Error.WriteLine(
        $"ERRO: a configuração por empresa não apresenta os três modos e a grelha IVA/artigo PHC. " +
        $"Combo={launchMode is not null}; grelha={vatGrid is not null}; linhas={vatGrid?.Rows.Count ?? -1}; " +
        $"artigo={(vatGrid?.Rows.Count > 0 ? Convert.ToString(vatGrid.Rows[0].Cells["PhcArticle"].Value) : "<sem linha>")}.");
    return 5;
}

var preferredConfiguration = configuration.Clone();
preferredConfiguration.PhcCompanies.Insert(0, new PhcCompanyConfiguration
{
    Name = "Outra empresa",
    OdbcName = "PHC_OUTRA",
    DatabaseName = "BD_OUTRA",
    TaxRegistrationNumber = "500000000"
});
using var preferredCompanyDialog = new ConfigurationDialog(
    preferredConfiguration,
    preferredCompanyTaxId: "500584095");
preferredCompanyDialog.CreateControl();
preferredCompanyDialog.PerformLayout();
var companyGrid = Descendants(preferredCompanyDialog).OfType<DataGridView>()
    .Single(grid => grid.Columns.Contains("Nif") && grid.Columns.Contains("Odbc"));
if (Convert.ToString(companyGrid.CurrentRow?.Cells["Nif"].Value) != "500584095")
{
    Console.Error.WriteLine("ERRO: a configuração não selecionou automaticamente a empresa correspondente ao SAF-T aberto.");
    return 6;
}

var previewTarget = new PhcAggregatedInvoiceTarget(
    configuration.PhcCompanies[0],
    new PhcAggregatedInvoiceDraft(
        "500584095",
        new DateOnly(2025, 7, 31),
        1_650,
        17,
        [new PhcAggregatedVatLine(23m, 100m, 23m, string.Empty, string.Empty)],
        "SAFTIA-123456789ABC"),
    99,
    0,
    "Consumidor Final",
    "999999990",
    string.Empty,
    string.Empty,
    string.Empty,
    string.Empty,
    1,
    "Fatura",
    1,
    1,
    "FT",
    [new PhcAggregatedTargetLine(23m, 100m, 23m, string.Empty, string.Empty, "SERV23", "Serviços à taxa normal", "UN", 71, 3, 23m, true, false)]);
using var previewDialog = new PhcLaunchPreviewDialog(previewTarget);
previewDialog.CreateControl();
previewDialog.PerformLayout();
var previewControls = Descendants(previewDialog).ToList();
var confirmLaunch = previewControls.OfType<Button>()
    .SingleOrDefault(button => button.Text == "Confirmar e lançar no PHC");
var previewGrid = previewControls.OfType<DataGridView>()
    .SingleOrDefault(grid => grid.Columns.Contains("CPOC") && grid.Columns.Contains("TabIVA"));
if (confirmLaunch is null || previewGrid?.Rows.Count != 1 ||
    !previewGrid.Columns.Contains("DocumentoGrupo") ||
    !previewGrid.Columns.Contains("CodIsencao") ||
    Convert.ToInt32(previewGrid.Rows[0].Cells["CPOC"].Value) != 71 ||
    Convert.ToInt32(previewGrid.Rows[0].Cells["TabIVA"].Value) != 3 ||
    Convert.ToString(previewGrid.Rows[0].Cells["Stock"].Value) != "Não")
{
    Console.Error.WriteLine("ERRO: a pré-visualização PHC não apresenta a confirmação e o mapeamento CPOC/IVA esperados.");
    return 7;
}
using var seriesDialog = new PhcSeriesMappingsDialog(
    "Empresa Teste",
    [new AccountingAI.Core.Phc.SaftDocumentSeries("FT", "2026A1", 12)],
    [new AccountingAI.Core.Phc.SaftDocumentSeries("RG", "20261", 8)],
    new AccountingAI.Core.Phc.PhcDocumentCatalog(
        "TD",
        [new AccountingAI.Core.Phc.PhcDocumentType(1, "Fatura", "FT", false, true, "TD")]),
    new AccountingAI.Core.Phc.PhcDocumentCatalog(
        "TSRE",
        [new AccountingAI.Core.Phc.PhcDocumentType(1, "Recibo", "RG", false, true, "TSRE")]),
    [],
    []);
seriesDialog.CreateControl();
seriesDialog.PerformLayout();
if (seriesDialog.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single().TabPages.Count != 2)
{
    Console.Error.WriteLine("ERRO: a configuração de séries não contém as grelhas de faturação e recebimentos.");
    return 4;
}

var layoutDirectory = Path.Combine(Path.GetTempPath(), $"saft-ia-grid-layout-{Guid.NewGuid():N}");
var layoutPath = Path.Combine(layoutDirectory, "config.dat");
try
{
    var layoutConfiguration = new AccountingAiConfiguration();
    var layoutStore = new ConfigurationStore(layoutPath);
    using (var layoutManager = new GridLayoutManager(layoutConfiguration, layoutStore))
    using (var firstGrid = UiTheme.CreateGrid())
    {
        firstGrid.Columns.Add("InvoiceNo", "Número da fatura");
        layoutManager.Attach(firstGrid, "teste.faturas");
        firstGrid.Columns["InvoiceNo"]!.Width = 347;
        if (!layoutManager.Flush())
        {
            Console.Error.WriteLine("ERRO: não foi possível guardar a largura de teste.");
            return 2;
        }
    }

    var restoredConfiguration = layoutStore.Load();
    using var restoredManager = new GridLayoutManager(restoredConfiguration, layoutStore);
    using var restoredGrid = UiTheme.CreateGrid();
    restoredGrid.Columns.Add("InvoiceNo", "Número da fatura");
    restoredManager.Attach(restoredGrid, "teste.faturas");
    if (restoredGrid.Columns["InvoiceNo"]!.Width != 347 || !restoredGrid.AllowUserToResizeColumns)
    {
        Console.Error.WriteLine("ERRO: a largura da coluna não foi restaurada ou a coluna não é redimensionável.");
        return 3;
    }
}
finally
{
    if (Directory.Exists(layoutDirectory))
    {
        Directory.Delete(layoutDirectory, recursive: true);
    }
}

using var associationDialog = new PhcCompanyAssociationDialog(
    "500584095",
    "Empresa Teste",
    configuration.PhcCompanies);
associationDialog.CreateControl();
associationDialog.PerformLayout();

using var portalDialog = new EfaturaPortalDialog(
    new DateOnly(2026, 1, 1),
    new DateOnly(2026, 1, 31),
    Path.GetTempPath(),
    "500584095",
    "palavra-passe-teste",
    _ => 0);
portalDialog.CreateControl();
portalDialog.PerformLayout();
var portalControls = Descendants(portalDialog).ToList();
var portalDates = portalControls.OfType<DateTimePicker>().ToList();
var portalExtract = portalControls.OfType<Button>()
    .SingleOrDefault(button => button.Text == "Entrar e extrair intervalo");
if (portalDates.Count != 2 ||
    DateOnly.FromDateTime(portalDates.MinBy(field => field.Value)!.Value) != new DateOnly(2026, 1, 1) ||
    DateOnly.FromDateTime(portalDates.MaxBy(field => field.Value)!.Value) != new DateOnly(2026, 1, 31) ||
    portalExtract is null)
{
    Console.Error.WriteLine("ERRO: o Portal e-Fatura não apresenta as datas inicial/final e o comando de extração automática.");
    return 8;
}

var auditResult = PhcPurchaseAuditService.Compare(
    [new PurchaseAuditDocument(
        "EF-1", "e-Fatura", "500000001", "Fornecedor", "FT 1", "FT",
        new DateOnly(2026, 5, 1), null, 100m, 23m, 123m,
        [new PurchaseAuditTaxTotal(23m, 100m, 23m)])],
    [new PurchaseAuditDocument(
        "PHC-1", "PHC", "500000001", "Fornecedor", "FT 1", "Compra",
        new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30), 100m, 23m, 123m,
        [new PurchaseAuditTaxTotal(23m, 100m, 23m)], "PHC-1")],
    new DateOnly(2026, 4, 1),
    new DateOnly(2026, 6, 30));
using var purchaseAuditPage = new PurchaseAuditPage(
    auditResult,
    (_, _) => Task.FromResult<PurchaseAuditResult?>(auditResult));
purchaseAuditPage.CreateControl();
purchaseAuditPage.PerformLayout();
var auditControls = Descendants(purchaseAuditPage).ToList();
var auditGrids = auditControls.OfType<DataGridView>().ToList();
if (auditGrids.Count != 2 || auditGrids.Any(grid => grid.Rows.Count != 1) ||
    auditControls.OfType<DateTimePicker>().Count() != 2 ||
    auditControls.OfType<Label>().All(label => label.Text != "Documentos do e-Fatura") ||
    auditControls.OfType<Label>().All(label => label.Text != "Documentos registados no PHC") ||
    auditControls.OfType<Button>().All(button => button.Text != "Atualizar auditoria PHC"))
{
    Console.Error.WriteLine("ERRO: o separador de auditoria não apresenta as duas grelhas lado a lado e o comando de atualização.");
    return 9;
}

Console.WriteLine("Smoke test da interface concluído com sucesso.");
return 0;

static IEnumerable<Control> Descendants(Control parent)
{
    foreach (Control child in parent.Controls)
    {
        yield return child;
        foreach (var descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}
