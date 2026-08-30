using AccountingAI.Core.Configuration;
using AccountingAI.Core.Efatura;
using AccountingAI.Core.Localization;
using AccountingAI.Core.Models;
using AccountingAI.Core.Parsing;
using AccountingAI.Core.Phc;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var syntheticOnly = args.Contains("--synthetic-only", StringComparer.OrdinalIgnoreCase);
var folderArgument = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
var integratedPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "integrated-minimal.xml");
var folder = folderArgument is not null
    ? Path.GetFullPath(folderArgument)
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SAFTs Exemplo"));

var paths = syntheticOnly
    ? new[] { integratedPath }
    : Directory.GetFiles(folder, "*.xml", SearchOption.TopDirectoryOnly);
var data = new SaftParser().ParseFiles(paths);

Console.WriteLine($"Ficheiros: {data.SourceFiles.Count}");
foreach (var source in data.SourceFiles)
{
    Console.WriteLine($"- {source.FileName}: {source.TaxAccountingBasis}, {source.DetectedContent}");
}

Console.WriteLine($"Clientes: {data.Count(SaftTableKeys.Customers)}");
Console.WriteLine($"Produtos: {data.Count(SaftTableKeys.Products)}");
Console.WriteLine($"Faturas: {data.Count(SaftTableKeys.Invoices)}");
Console.WriteLine($"Linhas de faturação: {data.Count(SaftTableKeys.InvoiceLines)}");
Console.WriteLine($"Recibos: {data.Count(SaftTableKeys.Payments)}");
Console.WriteLine($"Conferência: {data.Count(SaftTableKeys.WorkDocuments)}");

var errors = data.ValidationIssues.Where(issue => issue.Severity == ValidationSeverity.Error).ToList();
if (paths.Length == 0 || data.SourceFiles.Count != paths.Length || errors.Count > 0)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"ERRO [{error.SourceFile}] {error.Message}");
    }

    return 1;
}

if (data.Count(SaftTableKeys.Invoices) == 0 || data.Count(SaftTableKeys.InvoiceLines) == 0)
{
    Console.Error.WriteLine("ERRO: os exemplos não produziram documentos e linhas de faturação.");
    return 2;
}

var integrated = new SaftParser().ParseFiles([integratedPath]);
if (integrated.SourceFiles.SingleOrDefault()?.TaxAccountingBasis != "I" ||
    integrated.Count(SaftTableKeys.Accounts) != 2 ||
    integrated.Count(SaftTableKeys.Journals) != 1 ||
    integrated.Count(SaftTableKeys.Transactions) != 1 ||
    integrated.Count(SaftTableKeys.AccountingLines) != 2 ||
    integrated.Count(SaftTableKeys.Invoices) != 1 ||
    integrated.Count(SaftTableKeys.InvoiceLines) != 1)
{
    Console.Error.WriteLine("ERRO: o cenário sintético integrado/contabilidade não foi interpretado como esperado.");
    return 3;
}

var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var csv = new EfaturaCsvImporter().ImportDetailed([
    Path.Combine(fixtures, "efatura-1.csv"),
    Path.Combine(fixtures, "efatura-2.csv")
]);
if (csv.Purchases.Rows.Count != 3 || csv.Lines.Rows.Count != 3 || csv.Batches.Rows.Count != 2 || csv.Warnings.Count != 0 ||
    csv.Lines.Rows.Any(line => line["TaxRateStatus"] != "Taxa não discriminada pelo CSV do Portal") ||
    csv.Lines.Rows[0]["TaxBase"] != "100,00" || csv.Lines.Rows[0]["TaxAmount"] != "23,00")
{
    Console.Error.WriteLine("ERRO: a importação de CSV do e-Fatura não produziu os documentos, lotes e linhas fiscais esperados.");
    return 4;
}

var portalCsvPath = Path.Combine(Path.GetTempPath(), $"saft-ia-efatura-portal-{Guid.NewGuid():N}.csv");
try
{
    File.WriteAllText(
        portalCsvPath,
        "\"Setor\";\"Emitente\";\"Nº Fatura / ATCUD\";\"Tipo\";\"Data Emissão\";\"Total\";\"IVA\";\"Base Tributável\"\n" +
        "\"Outros\";\"500000001 - Fornecedor Portal\";\"FT 2026/1 / ABCD-1\";\"Fatura\";\"2026-04-01\";\"123,00 €\";\"23,00 €\";\"100,00 €\"\n",
        Encoding.UTF8);
    var portalCsv = new EfaturaCsvImporter().ImportDetailed([portalCsvPath]);
    var document = portalCsv.Purchases.Rows.Single();
    var line = portalCsv.Lines.Rows.Single();
    if (document["TaxRegistrationNumber"] != "500000001" || document["SupplierName"] != "Fornecedor Portal" ||
        document["InvoiceNo"] != "FT 2026/1" || document["ATCUD"] != "ABCD-1" ||
        line["DocumentKey"] != document["DocumentKey"] || line["TaxBase"] != "100,00 €" || line["TaxAmount"] != "23,00 €")
    {
        Console.Error.WriteLine("ERRO: o formato CSV real do Portal e-Fatura não foi normalizado corretamente.");
        return 19;
    }
}
finally
{
    File.Delete(portalCsvPath);
}

var portalQueries = EfaturaPortalExtractionPlan.CreateDailyQueries(
    new DateOnly(2026, 1, 1),
    new DateOnly(2026, 1, 31));
if (portalQueries.Count != 31 ||
    EfaturaPortalExtractionPlan.FormatPortalDate(new DateOnly(2026, 6, 1)) != "2026-06-01" ||
    portalQueries[0] != new EfaturaPortalDayQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)) ||
    portalQueries[^1] != new EfaturaPortalDayQuery(new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31)) ||
    portalQueries.Any(query => query.StartDate != query.EndDate))
{
    Console.Error.WriteLine("ERRO: o intervalo do Portal e-Fatura não foi dividido em consultas diárias independentes.");
    return 17;
}

if (!EfaturaPortalExtractionPlan.IsEmptyResultText("Não existem faturas para os critérios de pesquisa indicados") ||
    !EfaturaPortalExtractionPlan.IsEmptyResultText("Nenhum documento encontrado") ||
    EfaturaPortalExtractionPlan.IsEmptyResultText("Total: 2 faturas"))
{
    Console.Error.WriteLine("ERRO: a resposta de um dia sem faturas no Portal e-Fatura não foi reconhecida corretamente.");
    return 18;
}

var limitCsvPath = Path.Combine(Path.GetTempPath(), $"saft-ia-efatura-limit-{Guid.NewGuid():N}.csv");
try
{
    var limitCsv = new List<string> { "NIF emitente;Número da fatura;Data de emissão;Valor total" };
    limitCsv.AddRange(Enumerable.Range(1, 300).Select(number => $"500000000;FT {number};2026-07-01;1,00"));
    File.WriteAllLines(limitCsvPath, limitCsv, Encoding.UTF8);
    var limitResult = new EfaturaCsvImporter().ImportDetailed([limitCsvPath]);
    if (limitResult.Purchases.Rows.Count != 300 || limitResult.Warnings.Count != 1 ||
        limitResult.Batches.Rows.Single()["Estado"] != "No limite — confirmar completude")
    {
        Console.Error.WriteLine("ERRO: um CSV com exatamente 300 documentos não foi assinalado como potencialmente incompleto.");
        return 7;
    }
}
finally
{
    File.Delete(limitCsvPath);
}

if (SaftFieldLabels.Get("InvoiceNo") != "Número da fatura" ||
    SaftFieldLabels.Get("BillingAddress/AddressDetail") != "Morada de faturação — Morada detalhada" ||
    SaftFieldLabels.Get("GrossTotal") != "Total do documento")
{
    Console.Error.WriteLine("ERRO: as chaves SAF-T não foram convertidas para Português Europeu.");
    return 8;
}

var invoiceSeries = SaftSeriesExtractor.ExtractInvoices(data, "500584095");
var receiptSeries = SaftSeriesExtractor.ExtractReceipts(data, "500584095");
if (SaftSeriesExtractor.ExtractSeries("FT 2026A1/958", "FT") != "2026A1" ||
    SaftSeriesExtractor.ExtractSeries("1 2025/5", "FT") != "1 2025" ||
    (!syntheticOnly &&
     (!invoiceSeries.Any(item => item.DocumentType == "FT" && item.Series == "2025A1") ||
      !receiptSeries.Any(item => item.DocumentType == "RG" && item.Series == "20251"))))
{
    Console.Error.WriteLine("ERRO: as séries de faturação ou recebimentos não foram extraídas corretamente do SAF-T.");
    Console.Error.WriteLine($"Faturação: {string.Join(", ", invoiceSeries.Select(item => $"{item.DocumentType}/{item.Series}"))}");
    Console.Error.WriteLine($"Recebimentos: {string.Join(", ", receiptSeries.Select(item => $"{item.DocumentType}/{item.Series}"))}");
    return 13;
}

var syntheticVatData = new SaftDataSet();
syntheticVatData.GetOrCreateTable(SaftTableKeys.Header, "Cabeçalho").Add(new SaftRecord("iva.xml", "Cabeçalho")
{
    ["TaxRegistrationNumber"] = "500584095"
});
syntheticVatData.GetOrCreateTable(SaftTableKeys.Taxes, "Impostos").Add(new SaftRecord("iva.xml", "Imposto")
{
    ["TaxType"] = "IVA",
    ["TaxPercentage"] = "23.00"
});
syntheticVatData.GetOrCreateTable(SaftTableKeys.InvoiceLines, "Linhas").Add(new SaftRecord("iva.xml", "Linha")
{
    ["TaxType"] = "IVA",
    ["TaxPercentage"] = "6,00"
});
syntheticVatData.GetOrCreateTable(SaftTableKeys.Taxes, "Impostos").Add(new SaftRecord("iva.xml", "Imposto")
{
    ["TaxType"] = "IS",
    ["TaxPercentage"] = "4.00"
});
var vatRates = SaftVatRateExtractor.Extract(syntheticVatData, "500584095");
if (vatRates.Select(rate => rate.Rate).ToArray() is not [6m, 23m])
{
    Console.Error.WriteLine("ERRO: as taxas de IVA não foram extraídas corretamente do SAF-T.");
    return 14;
}

if (!syntheticOnly)
{
    var actualVatRates = SaftVatRateExtractor.Extract(data, "500584095");
    if (actualVatRates.Count == 0)
    {
        Console.Error.WriteLine("ERRO: não foram detetadas taxas de IVA no SAF-T integrado real.");
        return 15;
    }
    Console.WriteLine($"Taxas de IVA da empresa 500584095: {string.Join(", ", actualVatRates.Select(rate => rate.DisplayName))}");

    var aggregatedDraft = PhcAggregatedInvoiceBuilder.Build(data, "500584095");
    if (aggregatedDraft.DocumentDate != new DateOnly(2025, 7, 31) ||
        aggregatedDraft.DocumentCount != 1_650 ||
        aggregatedDraft.CancelledDocumentCount != 17 ||
        aggregatedDraft.Lines.Select(line => line.VatRate).ToArray() is not [0m, 0m, 0m, 23m] ||
        aggregatedDraft.Lines.Where(line => line.VatRate == 0m).Select(line => line.TaxExemptionCode).ToArray()
            is not ["M16", "M30", "M99"] ||
        aggregatedDraft.NetTotal != 1_808_536.34m ||
        aggregatedDraft.VatTotal != 346_632.49m ||
        aggregatedDraft.GrossTotal != 2_155_168.83m ||
        aggregatedDraft.NetTotal != aggregatedDraft.GrossTotal - aggregatedDraft.VatTotal ||
        aggregatedDraft.CurrencyCode != "EUR" ||
        !aggregatedDraft.ImportReference.StartsWith("SAFTIA-", StringComparison.Ordinal) ||
        aggregatedDraft.ImportReference.Length != 19)
    {
        Console.Error.WriteLine(
            $"ERRO: o documento agregado PHC não preservou a data, as taxas ou os totais do SAF-T. " +
            $"Data={aggregatedDraft.DocumentDate}; documentos={aggregatedDraft.DocumentCount}; " +
            $"anulados={aggregatedDraft.CancelledDocumentCount}; taxas={string.Join(",", aggregatedDraft.Lines.Select(line => line.VatRate))}; " +
            $"isenções={string.Join(",", aggregatedDraft.Lines.Select(line => line.TaxExemptionCode))}; " +
            $"líquido={aggregatedDraft.NetTotal}; IVA={aggregatedDraft.VatTotal}; total={aggregatedDraft.GrossTotal}; " +
            $"referência={aggregatedDraft.ImportReference} ({aggregatedDraft.ImportReference.Length}).");
        return 16;
    }

    var splitDrafts = PhcAggregatedInvoiceBuilder.BuildDocuments(data, "500584095");
    if (splitDrafts.Count != 4 ||
        splitDrafts.Count(draft => draft.Lines.Any(line => line.TaxExemptionCode.Length > 0)) != 3 ||
        splitDrafts.Count(draft => draft.GroupLabel == "Vendas tributadas") != 1 ||
        splitDrafts.Select(draft => draft.ImportReference).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
        splitDrafts.Any(draft => draft.ImportReference.Length > 20) ||
        splitDrafts.Any(draft => draft.CurrencyCode != "EUR") ||
        splitDrafts.Sum(draft => draft.NetTotal) != aggregatedDraft.NetTotal ||
        splitDrafts.Sum(draft => draft.VatTotal) != aggregatedDraft.VatTotal ||
        splitDrafts.Sum(draft => draft.GrossTotal) != aggregatedDraft.GrossTotal)
    {
        Console.Error.WriteLine("ERRO: a divisão por motivos de isenção não produziu documentos independentes e reconciliados.");
        return 21;
    }
}

if (PhcStockPolicy.MovesStock(PhcInvoiceImportMode.OneDocumentForGenericCustomer, 1) ||
    PhcStockPolicy.MovesStock(PhcInvoiceImportMode.DocumentsWithLinesGroupedByVat, 1) ||
    PhcStockPolicy.MovesStock(PhcInvoiceImportMode.DocumentsAsInSaft, 0) ||
    !PhcStockPolicy.MovesStock(PhcInvoiceImportMode.DocumentsAsInSaft, 1))
{
    Console.Error.WriteLine("ERRO: a política de stock PHC não respeita os modos agrupados ou o indicador ST.STNS.");
    return 20;
}

var configurationDirectoryPath = Path.Combine(Path.GetTempPath(), $"saft-ia-config-{Guid.NewGuid():N}");
var configurationPath = Path.Combine(configurationDirectoryPath, "config.dat");
try
{
    var syntheticPhcDirectory = Path.Combine(configurationDirectoryPath, "PHC_LOC");
    Directory.CreateDirectory(syntheticPhcDirectory);
    WriteSyntheticEpDbf(Path.Combine(syntheticPhcDirectory, "EP.DBF"));
    var catalog = new PhcCompanyCatalogReader().Read(syntheticPhcDirectory);
    if (catalog.Companies.Count != 2 || catalog.Companies[0].Name != "Empresa Teste" ||
        catalog.Companies[0].DatabaseName != "BD_TESTE" || catalog.Companies[0].OdbcName != "PHC_TESTE")
    {
        Console.Error.WriteLine("ERRO: o leitor do EP.DBF não devolveu as empresas esperadas.");
        return 10;
    }

    var configuration = new AccountingAiConfiguration
    {
        CustomerTaxId = "500000000",
        WfaUsername = "500000000/1",
        ProducerPfxPath = @"C:\Certificados\produtor.pfx",
        AuthenticationCertificatePath = @"C:\Certificados\autenticacao.cer",
        AtEnvironment = EfaturaAtEnvironment.Test,
        PortalDownloadDirectory = @"C:\Dados\e-Fatura",
        PhcDirectory = syntheticPhcDirectory
    };
    PhcCompanyCatalogSynchronizer.Synchronize(configuration, catalog.Companies, DateTimeOffset.Parse("2026-08-24T12:00:00Z"));
    var configuredCompany = configuration.PhcCompanies[0];
    configuredCompany.TaxRegistrationNumber = "500584095";
    configuredCompany.AtPassword = "senha-at-secreta";
    configuredCompany.WfaUsername = "500584095/3";
    configuredCompany.WfaPassword = "senha-wfa-secreta";
    configuredCompany.CreateCustomers = true;
    configuredCompany.CreateProducts = true;
    configuredCompany.MoveStock = true;
    configuredCompany.GenericCustomerNumber = 99;
    configuredCompany.GenericCustomerName = "Consumidor Final";
    configuredCompany.GenericCustomerTaxId = "999999990";
    configuredCompany.PurchaseAuditTotalTolerance = 0.50m;
    configuredCompany.PurchaseAuditVatTolerance = 0.25m;
    configuredCompany.PurchaseAuditDateToleranceDays = 50;
    configuredCompany.PurchaseAuditDocumentSuffixLength = 7;
    configuredCompany.InvoiceImportMode = PhcInvoiceImportMode.DocumentsWithLinesGroupedByVat;
    configuredCompany.VatArticleMappings.Add(new PhcVatArticleMapping
    {
        VatRate = 23m,
        PhcArticleReference = "SERV23",
        PhcArticleDesignation = "Serviços à taxa normal"
    });
    configuredCompany.InvoiceSeriesMappings.Add(new PhcSeriesMapping
    {
        SaftDocumentType = "FT",
        SaftSeries = "2025A1",
        PhcDocumentNumber = 1,
        PhcDocumentName = "Fatura",
        PhcTableName = "TD"
    });
    configuredCompany.ReceiptSeriesMappings.Add(new PhcSeriesMapping
    {
        SaftDocumentType = "RG",
        SaftSeries = "20251",
        PhcDocumentNumber = 1,
        PhcDocumentName = "Recibo",
        PhcTableName = "TSRE"
    });
    var phcCredential = configuration.PhcOdbcCredentials.Single(item => item.OdbcName == "PHC_TESTE");
    phcCredential.Username = "UtilizadorPHC";
    phcCredential.Password = "senha-odbc-secreta";
    phcCredential.LastTestSucceeded = true;
    phcCredential.LastTestedUtc = DateTimeOffset.Parse("2026-08-24T13:00:00Z");
    configuration.GridColumnWidths["faturacao.documentos"] = new Dictionary<string, int>
    {
        ["InvoiceNo"] = 315,
        ["GrossTotal"] = 170
    };

    var connectionString = PhcOdbcDataSource.BuildConnectionString(configuredCompany, phcCredential);
    var connectionBuilder = new System.Data.Odbc.OdbcConnectionStringBuilder(connectionString);
    if (!Equals(connectionBuilder["DSN"], "PHC_TESTE") ||
        !Equals(connectionBuilder["DATABASE"], "BD_TESTE") ||
        !Equals(connectionBuilder["UID"], "UtilizadorPHC"))
    {
        Console.Error.WriteLine("ERRO: a ligação ODBC não usa o DSN, base de dados e utilizador da empresa.");
        return 12;
    }

    var configurationStore = new ConfigurationStore(configurationPath);
    configurationStore.Save(configuration);
    var savedText = Encoding.UTF8.GetString(File.ReadAllBytes(configurationPath));
    var loadedConfiguration = configurationStore.Load();
    if (loadedConfiguration.CustomerTaxId != "500000000" ||
        loadedConfiguration.WfaUsername != "500000000/1" ||
        loadedConfiguration.AtEnvironment != EfaturaAtEnvironment.Test ||
        loadedConfiguration.SchemaVersion != 8 ||
        loadedConfiguration.GridColumnWidths["faturacao.documentos"]["InvoiceNo"] != 315 ||
        loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE") is not { TaxRegistrationNumber: "500584095", AtPassword: "senha-at-secreta", WfaUsername: "500584095/3", WfaPassword: "senha-wfa-secreta", CreateCustomers: true, CreateProducts: true, MoveStock: true, GenericCustomerNumber: 99, GenericCustomerTaxId: "999999990" } ||
        loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE").InvoiceSeriesMappings.Single() is not { SaftDocumentType: "FT", SaftSeries: "2025A1", PhcDocumentNumber: 1, PhcTableName: "TD" } ||
        loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE").ReceiptSeriesMappings.Single() is not { SaftDocumentType: "RG", SaftSeries: "20251", PhcDocumentNumber: 1, PhcTableName: "TSRE" } ||
        loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE") is not { InvoiceImportMode: PhcInvoiceImportMode.DocumentsWithLinesGroupedByVat, PurchaseAuditTotalTolerance: 0.50m, PurchaseAuditVatTolerance: 0.25m, PurchaseAuditDateToleranceDays: 50, PurchaseAuditDocumentSuffixLength: 7 } ||
        loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE").VatArticleMappings.Single() is not { VatRate: 23m, PhcArticleReference: "SERV23" } ||
        loadedConfiguration.PhcOdbcCredentials.Single(item => item.OdbcName == "PHC_TESTE") is not { Password: "senha-odbc-secreta", LastTestSucceeded: true } ||
        savedText.Contains("senha-odbc-secreta", StringComparison.Ordinal) ||
        savedText.Contains("senha-at-secreta", StringComparison.Ordinal) ||
        savedText.Contains("senha-wfa-secreta", StringComparison.Ordinal) ||
        savedText.Contains("faturacao.documentos", StringComparison.Ordinal) ||
        savedText.Contains("2025A1", StringComparison.Ordinal) ||
        savedText.Contains("SERV23", StringComparison.Ordinal) ||
        savedText.Contains("UtilizadorPHC", StringComparison.Ordinal) ||
        savedText.Contains("Empresa Teste", StringComparison.Ordinal))
    {
        var loadedCompany = loadedConfiguration.PhcCompanies.Single(company => company.OdbcName == "PHC_TESTE");
        Console.Error.WriteLine(
            "ERRO: a configuração PHC não foi restaurada corretamente ou contém dados em texto simples. " +
            $"Schema={loadedConfiguration.SchemaVersion}; total={loadedCompany.PurchaseAuditTotalTolerance}; " +
            $"IVA={loadedCompany.PurchaseAuditVatTolerance}; dias={loadedCompany.PurchaseAuditDateToleranceDays}; " +
            $"sufixo={loadedCompany.PurchaseAuditDocumentSuffixLength}.");
        return 9;
    }

    var syntheticPhcRoot = Path.Combine(configurationDirectoryPath, "PHC");
    var syntheticCompanyDatabase = Path.Combine(syntheticPhcRoot, "BD_TESTE");
    Directory.CreateDirectory(syntheticCompanyDatabase);
    WriteSyntheticParameterDbf(Path.Combine(syntheticCompanyDatabase, "T_Para1.DBF"), "pfx-secreta");
    var syntheticPhcPfxPath = Path.Combine(syntheticPhcDirectory, "certifphc.pfx");
    using (var syntheticPfxRsa = RSA.Create(2048))
    {
        var syntheticPfxRequest = new CertificateRequest(
            "CN=Certificado PHC Sintético",
            syntheticPfxRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var syntheticPfxCertificate = syntheticPfxRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(
            syntheticPhcPfxPath,
            syntheticPfxCertificate.Export(X509ContentType.Pfx, "pfx-secreta"));
    }
    var detectedPfxPassword = PhcAtCertificatePasswordReader.TryRead(
        syntheticPhcDirectory,
        "BD_TESTE",
        syntheticPhcPfxPath);
    if (detectedPfxPassword != "pfx-secreta")
    {
        Console.Error.WriteLine("ERRO: a chave do certificado PHC não foi obtida da T_Para1.DBF.");
        return 13;
    }

    if (Directory.Exists(@"C:\PHC_LOC"))
    {
        var realCatalog = new PhcCompanyCatalogReader().Read(@"C:\PHC_LOC");
        if (realCatalog.Companies.Count == 0)
        {
            Console.Error.WriteLine("ERRO: o EP.DBF real de C:\\PHC_LOC não contém empresas ativas.");
            return 11;
        }

        Console.WriteLine($"Empresas PHC reais: {realCatalog.Companies.Count} ({realCatalog.FilePath})");
    }
}
finally
{
    if (Directory.Exists(configurationDirectoryPath))
    {
        Directory.Delete(configurationDirectoryPath, recursive: true);
    }
}

var responseXml = File.ReadAllText(Path.Combine(fixtures, "efatura-response.xml"));
var response = AtEfaturaPurchaseSource.ParseResponse(responseXml);
var responseVatRates = response.Invoices.Single()
    .Elements()
    .Where(element => element.Name.LocalName == "LineSummary")
    .Select(line => line.Descendants().Single(element => element.Name.LocalName == "TaxPercentage").Value)
    .ToArray();
if (response.TotalPages != 2 || response.Invoices.Count != 1 || responseVatRates is not ["6", "23"])
{
    Console.Error.WriteLine("ERRO: a paginação ou as várias taxas de IVA da resposta oficial e-Fatura não foram interpretadas como esperado.");
    return 5;
}

var securityTestFolder = Path.Combine(Path.GetTempPath(), $"saft-ia-security-{Guid.NewGuid():N}");
Directory.CreateDirectory(securityTestFolder);
try
{
    using var clientRsa = RSA.Create(2048);
    var clientRequest = new CertificateRequest("CN=Accounting AI Teste", clientRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var clientCertificate = clientRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    var pfxPath = Path.Combine(securityTestFolder, "client.pfx");
    File.WriteAllBytes(pfxPath, clientCertificate.Export(X509ContentType.Pfx, "pfx-teste"));

    using var authenticationRsa = RSA.Create(2048);
    var authenticationRequest = new CertificateRequest("CN=AT Autenticação Teste", authenticationRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var authenticationCertificate = authenticationRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    var authenticationPath = Path.Combine(securityTestFolder, "authentication.cer");
    File.WriteAllBytes(authenticationPath, authenticationCertificate.Export(X509ContentType.Cert));

    using var connection = EfaturaAtConnection.Load(
        "500000000/1", "senha-teste", "500000000", pfxPath, "pfx-teste", authenticationPath, EfaturaAtEnvironment.Test);
    using var source = new AtEfaturaPurchaseSource(connection);
    var envelope = source.BuildEnvelope(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 2);
    var passwordCipher = Convert.FromBase64String(envelope.Descendants().Single(element => element.Name.LocalName == "Password").Value);
    var nonceCipher = Convert.FromBase64String(envelope.Descendants().Single(element => element.Name.LocalName == "Nonce").Value);
    var aesKey = authenticationRsa.Decrypt(nonceCipher, RSAEncryptionPadding.Pkcs1);
    using var aes = Aes.Create();
    aes.Key = aesKey;
    aes.Mode = CipherMode.ECB;
    aes.Padding = PaddingMode.PKCS7;
    using var decryptor = aes.CreateDecryptor();
    var password = Encoding.UTF8.GetString(decryptor.TransformFinalBlock(passwordCipher, 0, passwordCipher.Length));
    var pageSize = envelope.Descendants().Single(element => element.Name.LocalName == "nDocsPage").Value;
    var startDate = envelope.Descendants().Single(element => element.Name.LocalName == "StartDate").Value;
    var endDate = envelope.Descendants().Single(element => element.Name.LocalName == "EndDate").Value;
    var intervals = AtEfaturaPurchaseSource.BuildQueryIntervals(
        new DateOnly(2026, 7, 15),
        new DateOnly(2026, 9, 5));
    if (password != "senha-teste" || pageSize != "5000" || startDate != "2026-07-01" || endDate != "2026-07-31" ||
        intervals is not [
        { StartDate: var firstStart, EndDate: var firstEnd },
        { StartDate: var secondStart, EndDate: var secondEnd },
        { StartDate: var thirdStart, EndDate: var thirdEnd }
        ] ||
        firstStart != new DateOnly(2026, 7, 15) || firstEnd != new DateOnly(2026, 7, 31) ||
        secondStart != new DateOnly(2026, 8, 1) || secondEnd != new DateOnly(2026, 8, 31) ||
        thirdStart != new DateOnly(2026, 9, 1) || thirdEnd != new DateOnly(2026, 9, 5))
    {
        Console.Error.WriteLine("ERRO: o pedido seguro do e-Fatura não respeita a cifra, paginação ou segmentação mensal esperada.");
        return 6;
    }
}
finally
{
    Directory.Delete(securityTestFolder, recursive: true);
}

var efaturaAuditDocuments = new List<PurchaseAuditDocument>
{
    new(
        "EF-1", "e-Fatura", "500000001", "Fornecedor A", "FT A/100", "FT",
        new DateOnly(2026, 5, 1), null, 100m, 23m, 123m,
        [new PurchaseAuditTaxTotal(23m, 100m, 23m)]),
    new(
        "EF-2", "e-Fatura", "500000002", "Fornecedor B", "FT B/200", "FT",
        new DateOnly(2026, 5, 31), null, 75m, 8m, 83m,
        [new PurchaseAuditTaxTotal(6m, 50m, 3m), new PurchaseAuditTaxTotal(23m, 25m, 5m)])
};
var phcAuditDocuments = new List<PurchaseAuditDocument>
{
    new(
        "PHC-1", "PHC", "500000001", "Fornecedor A", "FT A/100", "Compra",
        new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30), 100m, 23m, 123m,
        [new PurchaseAuditTaxTotal(23m, 100m, 23m)], "PHC-1"),
    new(
        "PHC-2", "PHC", "500000002", "Fornecedor B", "FT B/200", "Compra",
        new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 1), 75m, 8m, 83m,
        [new PurchaseAuditTaxTotal(6m, 50m, 3m), new PurchaseAuditTaxTotal(23m, 25m, 5m)], "PHC-2")
};
var purchaseAudit = PhcPurchaseAuditService.Compare(
    efaturaAuditDocuments,
    phcAuditDocuments,
    new DateOnly(2026, 4, 1),
    new DateOnly(2026, 6, 30));
if (purchaseAudit is not { Conforming: 2, Different: 0, MissingInPhc: 0, MissingInEfatura: 0 } ||
    purchaseAudit.EfaturaRows.Rows.Count != 2 || purchaseAudit.PhcRows.Rows.Count != 2 ||
    purchaseAudit.PhcRows.Rows[0]["Diferença de datas"] != "-1 dia(s)" ||
    purchaseAudit.PhcRows.Rows[1]["Diferença de datas"] != "+1 dia(s)" ||
    !purchaseAudit.EfaturaRows.Rows[1]["Taxas e-Fatura"].Contains("6 %", StringComparison.Ordinal) ||
    !purchaseAudit.EfaturaRows.Rows[1]["Taxas e-Fatura"].Contains("23 %", StringComparison.Ordinal))
{
    Console.Error.WriteLine("ERRO: a auditoria de compras não conciliou documentos nas fronteiras dos meses nem discriminou as taxas de IVA.");
    return 22;
}

var phcWithWrongVat = phcAuditDocuments.ToList();
phcWithWrongVat[1] = phcWithWrongVat[1] with
{
    TaxTotals = [new PurchaseAuditTaxTotal(6m, 50m, 3m), new PurchaseAuditTaxTotal(23m, 20m, 5m)]
};
var purchaseAuditDifference = PhcPurchaseAuditService.Compare(efaturaAuditDocuments, phcWithWrongVat);
if (purchaseAuditDifference.Different != 1 ||
    !purchaseAuditDifference.EfaturaRows.Rows[1]["Diferenças / observações"].Contains("base a 23 %", StringComparison.Ordinal))
{
    Console.Error.WriteLine("ERRO: a auditoria de compras não detetou uma diferença na base tributável por taxa de IVA.");
    return 23;
}

var approximateEfatura = new PurchaseAuditDocument(
    "EF-APROX", "e-Fatura", "515347531", "Dermalife", "FI F82D/8202125753", "FI",
    new DateOnly(2026, 5, 2), null, 100.20m, 23.10m, 123.30m,
    [new PurchaseAuditTaxTotal(23m, 100.20m, 23.10m)]);
var approximatePhc = new PurchaseAuditDocument(
    "PHC-APROX", "PHC", "515347531", "Dermalife", "2125753", "Compra",
    new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 4), 100m, 23m, 123m,
    [new PurchaseAuditTaxTotal(23m, 100m, 23m)], "PHC-APROX");
var auditOptions = new PurchaseAuditOptions(0.50m, 0.25m, 10, 7);
var approximateAudit = PhcPurchaseAuditService.Compare(
    [approximateEfatura],
    [approximatePhc],
    options: auditOptions);
if (approximateAudit.Conforming != 1 ||
    !approximateAudit.EfaturaRows.Rows[0]["Diferenças / observações"].Contains("sufixo do documento", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("ERRO: a auditoria não conciliou o número longo do e-Fatura com o sufixo guardado no PHC.");
    return 24;
}

var strictAudit = PhcPurchaseAuditService.Compare(
    [approximateEfatura],
    [approximatePhc with { GrossTotal = 124m, VatTotal = 24m }],
    options: auditOptions);
if (strictAudit.MissingInPhc != 1 || strictAudit.MissingInEfatura != 1)
{
    Console.Error.WriteLine("ERRO: as tolerâncias configuradas não limitaram os candidatos automáticos.");
    return 25;
}

var manualAudit = PhcPurchaseAuditService.Compare(
    [approximateEfatura],
    [approximatePhc with { GrossTotal = 124m, VatTotal = 24m }],
    options: auditOptions,
    manualMatches: new Dictionary<string, string> { ["EF-APROX"] = "PHC-APROX" });
if (manualAudit.Different != 1 || manualAudit.ManualMatches.Count != 1 ||
    !manualAudit.EfaturaRows.Rows[0]["Estado da auditoria"].Contains("associação manual", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("ERRO: a associação manual não relacionou documentos fora das tolerâncias.");
    return 26;
}

Console.WriteLine("Smoke test concluído com sucesso.");
return 0;

static void WriteSyntheticEpDbf(string path)
{
    const int headerLength = 129;
    const int recordLength = 251;
    var fields = new[] { (Name: "NOME", Length: 30), (Name: "DATAB", Length: 120), (Name: "ODBC", Length: 100) };
    var records = new[]
    {
        (Deleted: false, Name: "Empresa Teste", Database: "BD_TESTE", Odbc: "PHC_TESTE"),
        (Deleted: true, Name: "Empresa Eliminada", Database: "BD_OLD", Odbc: "PHC_OLD"),
        (Deleted: false, Name: "Empresa Dois", Database: "BD_DOIS", Odbc: "PHC_DOIS")
    };
    var bytes = new byte[headerLength + (records.Length * recordLength) + 1];
    bytes[0] = 0x03;
    bytes[1] = 126;
    bytes[2] = 8;
    bytes[3] = 24;
    BitConverter.GetBytes(records.Length).CopyTo(bytes, 4);
    BitConverter.GetBytes((ushort)headerLength).CopyTo(bytes, 8);
    BitConverter.GetBytes((ushort)recordLength).CopyTo(bytes, 10);
    bytes[29] = 0x03;

    var fieldOffset = 32;
    var dataOffset = 1;
    foreach (var field in fields)
    {
        Encoding.ASCII.GetBytes(field.Name).CopyTo(bytes, fieldOffset);
        bytes[fieldOffset + 11] = (byte)'C';
        BitConverter.GetBytes(dataOffset).CopyTo(bytes, fieldOffset + 12);
        bytes[fieldOffset + 16] = (byte)field.Length;
        dataOffset += field.Length;
        fieldOffset += 32;
    }
    bytes[128] = 0x0D;

    for (var index = 0; index < records.Length; index++)
    {
        var recordOffset = headerLength + (index * recordLength);
        Array.Fill(bytes, (byte)' ', recordOffset, recordLength);
        bytes[recordOffset] = records[index].Deleted ? (byte)'*' : (byte)' ';
        WriteFixed(bytes, recordOffset + 1, 30, records[index].Name);
        WriteFixed(bytes, recordOffset + 31, 120, records[index].Database);
        WriteFixed(bytes, recordOffset + 151, 100, records[index].Odbc);
    }
    bytes[^1] = 0x1A;
    File.WriteAllBytes(path, bytes);
}

static void WriteSyntheticParameterDbf(string path, string certificatePassword)
{
    const int headerLength = 129;
    const int recordLength = 402;
    var fields = new[] { (Name: "TIPO", Length: 1), (Name: "VALOR", Length: 200), (Name: "DESCRICAO", Length: 200) };
    var bytes = new byte[headerLength + recordLength + 1];
    bytes[0] = 0x03;
    BitConverter.GetBytes(1).CopyTo(bytes, 4);
    BitConverter.GetBytes((ushort)headerLength).CopyTo(bytes, 8);
    BitConverter.GetBytes((ushort)recordLength).CopyTo(bytes, 10);

    var fieldOffset = 32;
    var dataOffset = 1;
    foreach (var field in fields)
    {
        Encoding.ASCII.GetBytes(field.Name).CopyTo(bytes, fieldOffset);
        bytes[fieldOffset + 11] = (byte)'C';
        BitConverter.GetBytes(dataOffset).CopyTo(bytes, fieldOffset + 12);
        bytes[fieldOffset + 16] = (byte)field.Length;
        dataOffset += field.Length;
        fieldOffset += 32;
    }
    bytes[128] = 0x0D;

    Array.Fill(bytes, (byte)' ', headerLength, recordLength);
    bytes[headerLength] = (byte)' ';
    WriteFixed(bytes, headerLength + 1, 1, "C");
    WriteFixed(bytes, headerLength + 2, 200, certificatePassword);
    WriteFixed(bytes, headerLength + 202, 200, "ge_atcertpass");
    bytes[^1] = 0x1A;
    File.WriteAllBytes(path, bytes);
}

static void WriteFixed(byte[] target, int offset, int length, string value)
{
    var encoded = Encoding.Latin1.GetBytes(value);
    Array.Copy(encoded, 0, target, offset, Math.Min(encoded.Length, length));
}
