namespace AccountingAI.Core.Configuration;

public sealed class PhcCompanyConfiguration
{
    public string Name { get; set; } = string.Empty;

    public string OdbcName { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = string.Empty;

    public string TaxRegistrationNumber { get; set; } = string.Empty;

    public string AtPassword { get; set; } = string.Empty;

    public string WfaUsername { get; set; } = string.Empty;

    public string WfaPassword { get; set; } = string.Empty;

    public bool CreateCustomers { get; set; }

    public bool CreateProducts { get; set; }

    public bool MoveStock { get; set; }

    public long? GenericCustomerNumber { get; set; }

    public string GenericCustomerName { get; set; } = string.Empty;

    public string GenericCustomerTaxId { get; set; } = string.Empty;

    public List<PhcSeriesMapping> InvoiceSeriesMappings { get; set; } = [];

    public List<PhcSeriesMapping> ReceiptSeriesMappings { get; set; } = [];

    public PhcInvoiceImportMode InvoiceImportMode { get; set; } = PhcInvoiceImportMode.DocumentsAsInSaft;

    public List<PhcVatArticleMapping> VatArticleMappings { get; set; } = [];

    public decimal PurchaseAuditTotalTolerance { get; set; } = 0.50m;

    public decimal PurchaseAuditVatTolerance { get; set; } = 0.50m;

    public int PurchaseAuditDateToleranceDays { get; set; } = 45;

    public int PurchaseAuditDocumentSuffixLength { get; set; } = 6;

    public bool PresentInEp { get; set; } = true;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public PhcCompanyConfiguration Clone() => new()
    {
        Name = Name,
        OdbcName = OdbcName,
        DatabaseName = DatabaseName,
        TaxRegistrationNumber = TaxRegistrationNumber,
        AtPassword = AtPassword,
        WfaUsername = WfaUsername,
        WfaPassword = WfaPassword,
        CreateCustomers = CreateCustomers,
        CreateProducts = CreateProducts,
        MoveStock = MoveStock,
        GenericCustomerNumber = GenericCustomerNumber,
        GenericCustomerName = GenericCustomerName,
        GenericCustomerTaxId = GenericCustomerTaxId,
        InvoiceSeriesMappings = InvoiceSeriesMappings.Select(mapping => mapping.Clone()).ToList(),
        ReceiptSeriesMappings = ReceiptSeriesMappings.Select(mapping => mapping.Clone()).ToList(),
        InvoiceImportMode = InvoiceImportMode,
        VatArticleMappings = VatArticleMappings.Select(mapping => mapping.Clone()).ToList(),
        PurchaseAuditTotalTolerance = PurchaseAuditTotalTolerance,
        PurchaseAuditVatTolerance = PurchaseAuditVatTolerance,
        PurchaseAuditDateToleranceDays = PurchaseAuditDateToleranceDays,
        PurchaseAuditDocumentSuffixLength = PurchaseAuditDocumentSuffixLength,
        PresentInEp = PresentInEp,
        LastSeenUtc = LastSeenUtc
    };
}

public enum PhcInvoiceImportMode
{
    DocumentsAsInSaft = 0,
    OneDocumentForGenericCustomer = 1,
    DocumentsWithLinesGroupedByVat = 2
}

public sealed class PhcVatArticleMapping
{
    public decimal VatRate { get; set; }

    public string PhcArticleReference { get; set; } = string.Empty;

    public string PhcArticleDesignation { get; set; } = string.Empty;

    public PhcVatArticleMapping Clone() => new()
    {
        VatRate = VatRate,
        PhcArticleReference = PhcArticleReference,
        PhcArticleDesignation = PhcArticleDesignation
    };
}

public sealed class PhcSeriesMapping
{
    public string SaftDocumentType { get; set; } = string.Empty;

    public string SaftSeries { get; set; } = string.Empty;

    public long? PhcDocumentNumber { get; set; }

    public string PhcDocumentName { get; set; } = string.Empty;

    public string PhcTableName { get; set; } = string.Empty;

    public PhcSeriesMapping Clone() => new()
    {
        SaftDocumentType = SaftDocumentType,
        SaftSeries = SaftSeries,
        PhcDocumentNumber = PhcDocumentNumber,
        PhcDocumentName = PhcDocumentName,
        PhcTableName = PhcTableName
    };
}

public sealed class PhcOdbcCredential
{
    public string OdbcName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool LastTestSucceeded { get; set; }

    public DateTimeOffset? LastTestedUtc { get; set; }

    public PhcOdbcCredential Clone() => new()
    {
        OdbcName = OdbcName,
        Username = Username,
        Password = Password,
        LastTestSucceeded = LastTestSucceeded,
        LastTestedUtc = LastTestedUtc
    };
}
