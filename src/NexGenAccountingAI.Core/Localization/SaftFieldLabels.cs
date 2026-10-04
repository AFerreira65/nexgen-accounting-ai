using System.Text.RegularExpressions;

namespace NexGenAccountingAI.Core.Localization;

public static class SaftFieldLabels
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ficheiro"] = "Ficheiro",
            ["DocumentKey"] = "Chave interna do documento",
            ["RecordType"] = "Tipo de registo",
            ["AuditFileVersion"] = "Versão do ficheiro SAF-T",
            ["CompanyID"] = "Identificação da empresa",
            ["TaxRegistrationNumber"] = "Número de identificação fiscal",
            ["TaxAccountingBasis"] = "Tipo de ficheiro SAF-T",
            ["CompanyName"] = "Denominação social",
            ["BusinessName"] = "Nome comercial",
            ["CompanyAddress"] = "Morada da empresa",
            ["AddressDetail"] = "Morada detalhada",
            ["City"] = "Localidade",
            ["PostalCode"] = "Código postal",
            ["Region"] = "Região",
            ["Country"] = "País",
            ["FiscalYear"] = "Exercício fiscal",
            ["StartDate"] = "Data inicial",
            ["EndDate"] = "Data final",
            ["CurrencyCode"] = "Código da moeda",
            ["DateCreated"] = "Data de criação",
            ["TaxEntity"] = "Estabelecimento",
            ["ProductCompanyTaxID"] = "NIF do produtor do software",
            ["SoftwareCertificateNumber"] = "Número do certificado do software",
            ["ProductID"] = "Nome do produto",
            ["ProductVersion"] = "Versão do produto",
            ["HeaderComment"] = "Observações do cabeçalho",
            ["Telephone"] = "Telefone",
            ["Fax"] = "Fax",
            ["Email"] = "Correio eletrónico",
            ["Website"] = "Sítio Web",
            ["AccountID"] = "Código da conta",
            ["AccountDescription"] = "Descrição da conta",
            ["OpeningDebitBalance"] = "Saldo de abertura a débito",
            ["OpeningCreditBalance"] = "Saldo de abertura a crédito",
            ["ClosingDebitBalance"] = "Saldo de fecho a débito",
            ["ClosingCreditBalance"] = "Saldo de fecho a crédito",
            ["GroupingCategory"] = "Categoria de agregação",
            ["GroupingCode"] = "Código de agregação",
            ["TaxonomyCode"] = "Código de taxonomia",
            ["CustomerID"] = "Código do cliente",
            ["SupplierID"] = "Código do fornecedor",
            ["CustomerTaxID"] = "NIF do cliente",
            ["SupplierTaxID"] = "NIF do fornecedor",
            ["SupplierName"] = "Nome do fornecedor",
            ["Contact"] = "Contacto",
            ["BillingAddress"] = "Morada de faturação",
            ["ShipToAddress"] = "Morada de destino",
            ["ShipFromAddress"] = "Morada de origem",
            ["SelfBillingIndicator"] = "Indicador de autofaturação",
            ["ProductType"] = "Tipo de produto",
            ["ProductCode"] = "Código do produto",
            ["ProductGroup"] = "Grupo do produto",
            ["ProductDescription"] = "Descrição do produto",
            ["ProductNumberCode"] = "Código de barras do produto",
            ["CustomsDetails"] = "Informação aduaneira",
            ["CNCode"] = "Código da Nomenclatura Combinada",
            ["UNNumber"] = "Número ONU",
            ["TaxType"] = "Tipo de imposto",
            ["TaxCountryRegion"] = "País ou região do imposto",
            ["TaxCode"] = "Código do imposto",
            ["Description"] = "Descrição",
            ["TaxPercentage"] = "Taxa de imposto (%)",
            ["TaxAmount"] = "Montante do imposto",
            ["JournalID"] = "Código do diário",
            ["TransactionID"] = "Identificação do movimento",
            ["Period"] = "Período",
            ["TransactionDate"] = "Data do movimento",
            ["SourceID"] = "Utilizador de origem",
            ["SystemEntryDate"] = "Data de registo no sistema",
            ["GLPostingDate"] = "Data de lançamento contabilístico",
            ["DebitAmount"] = "Montante a débito",
            ["CreditAmount"] = "Montante a crédito",
            ["InvoiceNo"] = "Número da fatura",
            ["ATCUD"] = "ATCUD",
            ["ATDocCodeID"] = "Código de identificação AT do documento",
            ["InvoiceStatus"] = "Estado da fatura",
            ["InvoiceStatusDate"] = "Data do estado da fatura",
            ["SourceBilling"] = "Origem da faturação",
            ["Hash"] = "Assinatura do documento",
            ["HashControl"] = "Versão da chave de assinatura",
            ["InvoiceDate"] = "Data da fatura",
            ["InvoiceType"] = "Tipo de fatura",
            ["CashVATSchemeIndicator"] = "Indicador do regime de IVA de caixa",
            ["ThirdPartiesBillingIndicator"] = "Indicador de faturação por terceiros",
            ["LineNumber"] = "Número da linha",
            ["OrderReferences"] = "Referência à encomenda",
            ["OriginatingON"] = "Número do documento de origem",
            ["OrderDate"] = "Data do documento de origem",
            ["DeliveryID"] = "Identificação da entrega",
            ["Quantity"] = "Quantidade",
            ["UnitOfMeasure"] = "Unidade de medida",
            ["UnitPrice"] = "Preço unitário",
            ["TaxBase"] = "Base tributável",
            ["TaxPointDate"] = "Data do facto tributário",
            ["References"] = "Referências",
            ["Reference"] = "Documento de referência",
            ["Reason"] = "Motivo",
            ["TaxExemptionReason"] = "Motivo da isenção de imposto",
            ["TaxExemptionCode"] = "Código do motivo de isenção",
            ["SettlementAmount"] = "Montante do desconto",
            ["TaxPayable"] = "Imposto a pagar",
            ["TaxRateStatus"] = "Discriminação da taxa de IVA",
            ["FiscalDataSource"] = "Origem dos dados fiscais",
            ["NetTotal"] = "Total líquido",
            ["GrossTotal"] = "Total do documento",
            ["CurrencyAmount"] = "Montante em moeda estrangeira",
            ["ExchangeRate"] = "Taxa de câmbio",
            ["PaymentTerms"] = "Condições de pagamento",
            ["PaymentMechanism"] = "Meio de pagamento",
            ["PaymentAmount"] = "Montante pago",
            ["PaymentDate"] = "Data de pagamento",
            ["DocumentNumber"] = "Número do documento",
            ["DocArchivalNumber"] = "Número de arquivo do documento",
            ["MovementStatus"] = "Estado do documento de transporte",
            ["MovementStatusDate"] = "Data do estado do documento de transporte",
            ["MovementDate"] = "Data do documento de transporte",
            ["MovementStartTime"] = "Data e hora de início do transporte",
            ["MovementEndTime"] = "Data e hora de fim do transporte",
            ["MovementType"] = "Tipo de documento de transporte",
            ["SystemID"] = "Identificação do sistema",
            ["DocumentStatus"] = "Estado do documento",
            ["DocumentStatusDate"] = "Data do estado do documento",
            ["WorkStatus"] = "Estado do documento de conferência",
            ["WorkStatusDate"] = "Data do estado do documento de conferência",
            ["WorkDate"] = "Data do documento de conferência",
            ["WorkType"] = "Tipo de documento de conferência",
            ["PaymentRefNo"] = "Número do recibo",
            ["PaymentStatus"] = "Estado do recibo",
            ["PaymentStatusDate"] = "Data do estado do recibo",
            ["SourcePayment"] = "Origem do pagamento",
            ["PaymentType"] = "Tipo de recibo",
            ["NumberOfEntries"] = "Número de documentos",
            ["NumberOfMovementLines"] = "Número de documentos de transporte",
            ["TotalDebit"] = "Total a débito",
            ["TotalCredit"] = "Total a crédito",
            ["TotalQuantityIssued"] = "Quantidade total emitida",
            ["SerialNumber"] = "Número de série",
            ["EACCode"] = "Código da atividade económica",
            ["RecordID"] = "Identificação do registo",
            ["SourceDocumentID"] = "Identificação do documento de origem",
            ["TaxonomyReference"] = "Referencial de taxonomia",
            ["TransactionType"] = "Tipo de movimento contabilístico",
            ["DocumentTotals"] = "Totais do documento",
            ["Tax"] = "Imposto",
            ["Currency"] = "Moeda",
            ["Settlement"] = "Desconto",
            ["SpecialRegimes"] = "Regimes especiais",
            ["ShipTo"] = "Destino",
            ["ShipFrom"] = "Origem",
            ["WithholdingTax"] = "Retenção na fonte",
            ["Lote CSV"] = "Lote CSV",
            ["Linha no ficheiro"] = "Linha no ficheiro",
            ["Lote"] = "Lote",
            ["Registos"] = "Registos",
            ["Limite esperado"] = "Limite esperado",
            ["Estado"] = "Estado",
            ["Severidade"] = "Severidade",
            ["Área"] = "Área",
            ["Mensagem"] = "Mensagem"
        };

    private static readonly IReadOnlyDictionary<string, string> Words =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Account"] = "Conta",
            ["Address"] = "Morada",
            ["Amount"] = "Montante",
            ["Balance"] = "Saldo",
            ["Billing"] = "Faturação",
            ["Code"] = "Código",
            ["Company"] = "Empresa",
            ["Country"] = "País",
            ["Credit"] = "Crédito",
            ["Currency"] = "Moeda",
            ["Customer"] = "Cliente",
            ["Date"] = "Data",
            ["Debit"] = "Débito",
            ["Description"] = "Descrição",
            ["Document"] = "Documento",
            ["Entry"] = "Registo",
            ["Exchange"] = "Câmbio",
            ["Gross"] = "Bruto",
            ["ID"] = "Identificador",
            ["Indicator"] = "Indicador",
            ["Invoice"] = "Fatura",
            ["Line"] = "Linha",
            ["Movement"] = "Movimento",
            ["Net"] = "Líquido",
            ["Number"] = "Número",
            ["Payment"] = "Pagamento",
            ["Percentage"] = "Percentagem",
            ["Price"] = "Preço",
            ["Product"] = "Produto",
            ["Quantity"] = "Quantidade",
            ["Reason"] = "Motivo",
            ["Reference"] = "Referência",
            ["Registration"] = "Registo",
            ["Source"] = "Origem",
            ["Status"] = "Estado",
            ["Supplier"] = "Fornecedor",
            ["System"] = "Sistema",
            ["Tax"] = "Imposto",
            ["Total"] = "Total",
            ["Type"] = "Tipo",
            ["Unit"] = "Unidade",
            ["Version"] = "Versão",
            ["Work"] = "Trabalho"
        };

    public static string Get(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return fieldName;
        }

        if (Labels.TryGetValue(fieldName, out var label))
        {
            return label;
        }

        if (fieldName.Contains(' ') && !fieldName.Contains('/'))
        {
            return fieldName;
        }

        return string.Join(" — ", fieldName.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(TranslateSegment));
    }

    private static string TranslateSegment(string segment)
    {
        if (Labels.TryGetValue(segment, out var label))
        {
            return label;
        }

        var words = Regex.Replace(segment.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word => Words.TryGetValue(word, out var translated) ? translated : word));
    }
}
