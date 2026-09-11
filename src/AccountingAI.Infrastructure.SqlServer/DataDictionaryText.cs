namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>
/// Texto em pt-PT do dicionário de dados: o propósito de cada tabela e a
/// descrição de cada coluna. É mantido à mão (o EF dá a estrutura; isto dá o
/// significado). A procura de uma descrição tenta primeiro <c>Tabela.Coluna</c>
/// e depois uma descrição genérica por nome de coluna.
/// </summary>
internal static class DataDictionaryText
{
    public static string PurposeOf(string table) =>
        Purposes.GetValueOrDefault(table, string.Empty);

    public static string DescriptionOf(string table, string column) =>
        Columns.GetValueOrDefault($"{table}.{column}")
        ?? Generic.GetValueOrDefault(column)
        ?? string.Empty;

    private static readonly Dictionary<string, string> Purposes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AccountingFirms"] = "Gabinete de contabilidade — a organização que presta o serviço às empresas.",
        ["Companies"] = "Empresa cuja contabilidade é feita na plataforma; pertence a um gabinete.",
        ["UserAccounts"] = "Utilizador da plataforma (contabilista ou colaborador do gabinete).",
        ["CompanyUsers"] = "Liga um utilizador a uma empresa, com o papel que tem nela.",
        ["UserCompanySettings"] = "Preferências de um utilizador numa empresa (idioma, exercício, tema, tamanho de texto).",
        ["Accounts"] = "Plano de contas da empresa: contas de razão (integração) e de movimento (com taxonomia).",
        ["Journals"] = "Diários da contabilidade (compras, vendas, bancos, abertura, apuramento, …).",
        ["JournalEntries"] = "Lançamentos contabilísticos da empresa, com estado de conferência e origem.",
        ["JournalEntryLines"] = "Linhas de um lançamento: uma conta movimentada a débito ou a crédito.",
        ["AccountingDocumentTypes"] = "Tipos de documento (FT, NC, RC, …) e o diário por omissão de cada um.",
        ["DocumentNumberSeries"] = "Séries de numeração de documentos, por exercício.",
        ["SaftValidationRun"] = "Registo de uma validação de ficheiro SAF-T (esquema + regras analíticas).",
        ["EFaturaPurchase"] = "Fatura de compra conhecida pelo Portal e-Fatura, importada de um CSV, para o cruzamento.",
        ["SavedAnalysis"] = "Definição de uma análise configurável guardada por uma empresa (origem, colunas, filtros, agrupamentos, totais).",
        ["ReportTemplate"] = "Configuração de impressão de um relatório fixo numa empresa (linhas de cabeçalho, identificação, orientação).",
        ["ManagementReport"] = "Relatório de gestão gerado por IA: o tipo, o período, o modelo, e o dossiê e o resultado guardados.",
        ["CompanyAiSettings"] = "Interruptor dos relatórios de gestão com IA de uma empresa (ativo, geração mensal automática).",
        ["VatReturnFiling"] = "Apuramento periódico de IVA guardado por uma empresa (período, excesso reportado do período anterior, fotografia do cálculo).",
        ["CompanyAtCredentials"] = "Credenciais e definições da AT de uma empresa: o subutilizador e a senha (cifrada) do perfil e-Fatura, a periodicidade da declaração periódica e o ambiente da AT.",
        ["AccountVatClassification"] = "Classificação de IVA de uma conta (taxa, natureza da operação, região) para a desagregação da declaração periódica.",
        ["IrcEstimateFiling"] = "Estimativa de IRC guardada por uma empresa (período, taxa e correções fiscais indicadas, fotografia do cálculo).",
        ["FoundationState"] = "Estado interno da fundação da plataforma; não é de uso contabilístico.",
    };

    private static readonly Dictionary<string, string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Id"] = "Identificador único do registo (GUID).",
        ["CompanyId"] = "Empresa a que o registo pertence.",
        ["RowVersion"] = "Carimbo de concorrência otimista (gerido pela base de dados).",
        ["IsActive"] = "Indica se o registo está ativo.",
        ["CreatedAtUtc"] = "Data e hora (UTC) em que o registo foi criado.",
        ["CreatedBy"] = "Utilizador que criou o registo (nulo para registos históricos ou de importação).",
        ["ModifiedAtUtc"] = "Data e hora (UTC) da última alteração (nulo se nunca foi alterado).",
        ["ModifiedBy"] = "Utilizador que fez a última alteração.",
    };

    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Companies.FirmId"] = "Gabinete a que a empresa pertence.",
        ["Companies.LegalName"] = "Denominação social da empresa.",
        ["Companies.TaxRegistrationNumber"] = "NIF da empresa.",
        ["Companies.TaxRegion"] = "Região fiscal (Continente, Açores, Madeira).",
        ["Companies.BaseCurrency"] = "Moeda base da contabilidade (EUR).",
        ["Companies.AccountingStandard"] = "Normativo contabilístico (SNC geral, micro, ESNL, …).",
        ["Companies.AccountingSetupVersion"] = "Versão do plano/taxonomias provisionados.",
        ["Companies.PostingLockDate"] = "Data até à qual não se aceitam lançamentos.",

        ["AccountingFirms.LegalName"] = "Denominação do gabinete.",
        ["AccountingFirms.Slug"] = "Identificador curto do gabinete para URLs.",

        ["UserAccounts.DisplayName"] = "Nome do utilizador tal como aparece na interface.",
        ["UserAccounts.Email"] = "Email do utilizador.",
        ["UserAccounts.NormalizedEmail"] = "Email em maiúsculas, para procura e unicidade.",

        ["CompanyUsers.UserId"] = "Utilizador associado.",
        ["CompanyUsers.Role"] = "Papel do utilizador na empresa (Owner, Accountant, …).",

        ["UserCompanySettings.UserId"] = "Utilizador a que as preferências pertencem.",
        ["UserCompanySettings.Language"] = "Idioma da interface (pt-PT).",
        ["UserCompanySettings.TimeZone"] = "Fuso horário do utilizador.",
        ["UserCompanySettings.DefaultFiscalYear"] = "Exercício ativo do utilizador nesta empresa.",
        ["UserCompanySettings.Theme"] = "Tema da interface (claro/escuro).",
        ["UserCompanySettings.CompactMode"] = "Modo compacto da interface.",
        ["UserCompanySettings.PageSize"] = "Tamanho de página das listas.",
        ["UserCompanySettings.TextSize"] = "Tamanho do texto da interface.",

        ["Accounts.Code"] = "Código da conta (SNC).",
        ["Accounts.Name"] = "Descrição da conta.",
        ["Accounts.ParentCode"] = "Código da conta-mãe (nulo nas de topo).",
        ["Accounts.AllowsPosting"] = "Verdadeiro nas contas de movimento; falso nas de integração.",
        ["Accounts.SourceTemplateVersion"] = "Versão do plano-modelo de onde a conta veio (nulo se criada à mão).",
        ["Accounts.TaxonomyReference"] = "Referencial de taxonomia (S, M, N, O).",
        ["Accounts.TaxonomyCode"] = "Código de taxonomia atribuído à conta.",

        ["Journals.Code"] = "Código do diário.",
        ["Journals.Name"] = "Nome do diário.",

        ["JournalEntries.JournalId"] = "Diário do lançamento.",
        ["JournalEntries.DocumentTypeId"] = "Tipo de documento do lançamento (nulo se não classificado).",
        ["JournalEntries.Reference"] = "Referência única do lançamento na empresa.",
        ["JournalEntries.EntryDate"] = "Data contabilística do lançamento.",
        ["JournalEntries.DocumentReference"] = "Referência do documento de origem (nº de fatura, etc.).",
        ["JournalEntries.Description"] = "Descritivo do lançamento.",
        ["JournalEntries.Notes"] = "Observações internas livres.",
        ["JournalEntries.Status"] = "Estado: por conferir, conferido ou anulado.",
        ["JournalEntries.Source"] = "Origem: manual, SAF-T, abertura ou apuramento.",
        ["JournalEntries.TotalAmount"] = "Valor total do lançamento (soma dos débitos).",

        ["JournalEntryLines.JournalEntryId"] = "Lançamento a que a linha pertence.",
        ["JournalEntryLines.AccountId"] = "Conta movimentada.",
        ["JournalEntryLines.Sequence"] = "Ordem da linha dentro do lançamento.",
        ["JournalEntryLines.Description"] = "Descritivo da linha.",
        ["JournalEntryLines.Debit"] = "Valor a débito (zero se for uma linha de crédito).",
        ["JournalEntryLines.Credit"] = "Valor a crédito (zero se for uma linha de débito).",

        ["AccountingDocumentTypes.Code"] = "Código do tipo de documento (FT, NC, …).",
        ["AccountingDocumentTypes.Name"] = "Nome do tipo de documento.",
        ["AccountingDocumentTypes.DefaultJournalId"] = "Diário por omissão para documentos deste tipo.",

        ["DocumentNumberSeries.Series"] = "Prefixo da série.",
        ["DocumentNumberSeries.Year"] = "Exercício da série.",
        ["DocumentNumberSeries.Width"] = "Nº de dígitos do contador.",
        ["DocumentNumberSeries.NextNumber"] = "Próximo número a atribuir.",

        ["SaftValidationRun.UserId"] = "Utilizador que executou a validação.",
        ["SaftValidationRun.FileName"] = "Nome do ficheiro validado.",
        ["SaftValidationRun.AuditFileVersion"] = "Versão do SAF-T declarada no ficheiro.",
        ["SaftValidationRun.SchemaAvailable"] = "Se o XSD oficial estava disponível na validação.",
        ["SaftValidationRun.SchemaValid"] = "Se o ficheiro é conforme ao XSD.",
        ["SaftValidationRun.ErrorCount"] = "Nº de ocorrências de nível erro.",
        ["SaftValidationRun.WarningCount"] = "Nº de ocorrências de nível aviso.",
        ["SaftValidationRun.InformationCount"] = "Nº de ocorrências informativas.",
        ["SaftValidationRun.Truncated"] = "Se a lista de ocorrências foi cortada no limite.",
        ["SaftValidationRun.IssuesJson"] = "Ocorrências da validação, em JSON.",

        ["EFaturaPurchase.ImportBatchId"] = "Agrupa as faturas importadas na mesma chamada.",
        ["EFaturaPurchase.SupplierTaxId"] = "NIF do fornecedor emitente.",
        ["EFaturaPurchase.SupplierName"] = "Nome do fornecedor.",
        ["EFaturaPurchase.InvoiceNo"] = "Número da fatura.",
        ["EFaturaPurchase.Atcud"] = "ATCUD da fatura (nulo se não vier no CSV).",
        ["EFaturaPurchase.InvoiceDate"] = "Data de emissão da fatura.",
        ["EFaturaPurchase.InvoiceType"] = "Tipo de documento (FT, FS, …).",
        ["EFaturaPurchase.Status"] = "Situação no Portal e-Fatura.",
        ["EFaturaPurchase.Sector"] = "Setor declarado no Portal.",
        ["EFaturaPurchase.TaxBase"] = "Valor tributável.",
        ["EFaturaPurchase.TaxAmount"] = "Valor do IVA.",
        ["EFaturaPurchase.GrossTotal"] = "Valor total da fatura.",
        ["EFaturaPurchase.SourceFileName"] = "Nome do CSV de onde a fatura foi importada.",
        ["EFaturaPurchase.DocumentKey"] = "Chave de idempotência por empresa (NIF|nº|data).",

        ["FoundationState.SchemaVersion"] = "Versão do esquema registada na fundação.",

        ["SavedAnalysis.Name"] = "Nome da análise (único por empresa).",
        ["SavedAnalysis.Description"] = "Descrição livre da análise.",
        ["SavedAnalysis.DefinitionJson"] = "A definição da análise (origem, colunas, filtros, agrupamentos, ordenação, agregados) em JSON.",

        ["ReportTemplate.ReportKey"] = "Chave do relatório a que o modelo se aplica (balancete, razao, …).",
        ["ReportTemplate.HeaderLinesJson"] = "Linhas de cabeçalho livres, em JSON (lista de texto).",
        ["ReportTemplate.ShowFirmName"] = "Se a folha impressa mostra o nome do gabinete.",
        ["ReportTemplate.ShowCompanyIdentity"] = "Se a folha impressa mostra a identificação da empresa (nome e NIF).",
        ["ReportTemplate.Orientation"] = "Orientação da folha: retrato ou paisagem.",

        ["ManagementReport.Kind"] = "O tipo pré-definido (sintese-mensal, …) ou «pergunta» quando é uma pergunta livre.",
        ["ManagementReport.Question"] = "A pergunta livre do utilizador, quando aplicável.",
        ["ManagementReport.PeriodFrom"] = "Data inicial do período do relatório.",
        ["ManagementReport.PeriodTo"] = "Data final do período do relatório.",
        ["ManagementReport.Model"] = "O modelo de IA usado na geração.",
        ["ManagementReport.DossierJson"] = "O dossiê de dados (retrato determinístico do período) em JSON, guardado para reproduzir o relatório.",
        ["ManagementReport.ResultJson"] = "O resultado redigido (resumo, secções, alertas, indicadores) em JSON.",
        ["ManagementReport.PromptTokens"] = "Tokens de entrada consumidos na chamada ao modelo.",
        ["ManagementReport.OutputTokens"] = "Tokens de saída produzidos pelo modelo.",

        ["CompanyAiSettings.Enabled"] = "Se os relatórios de gestão com IA estão disponíveis para a empresa.",
        ["CompanyAiSettings.AutoMonthly"] = "Se a síntese mensal é gerada automaticamente.",

        ["VatReturnFiling.PeriodFrom"] = "Data inicial do período do apuramento.",
        ["VatReturnFiling.PeriodTo"] = "Data final do período do apuramento.",
        ["VatReturnFiling.PriorPeriodCredit"] = "Excesso a reportar do período anterior, indicado pelo utilizador.",
        ["VatReturnFiling.SnapshotJson"] = "A fotografia do apuramento calculado (liquidado, dedutível, regularizações, posição) em JSON.",

        ["CompanyAtCredentials.SubUser"] = "O subutilizador da AT no formato NIF/n (ex.: 500584095/1).",
        ["CompanyAtCredentials.EncryptedPassword"] = "A senha do subutilizador, guardada cifrada (valor enc:v1:…).",
        ["CompanyAtCredentials.Periodicity"] = "A periodicidade da declaração periódica de IVA desta empresa (Mensal / Trimestral).",
        ["CompanyAtCredentials.Environment"] = "O ambiente da AT (Testes / Producao) em que as chamadas desta empresa devem correr.",

        ["AccountVatClassification.AccountId"] = "A conta a que a classificação se aplica.",
        ["AccountVatClassification.Rate"] = "A taxa de IVA (isento / reduzida / intermédia / normal).",
        ["AccountVatClassification.Nature"] = "A natureza da operação de IVA (transmissões internas, prestações de serviços, aquisições intracomunitárias, …).",
        ["AccountVatClassification.Region"] = "A região (Continente / Açores / Madeira).",

        ["IrcEstimateFiling.PeriodFrom"] = "Data inicial do período da estimativa.",
        ["IrcEstimateFiling.PeriodTo"] = "Data final do período da estimativa.",
        ["IrcEstimateFiling.TaxRate"] = "A taxa de IRC indicada pelo utilizador (fração, ex.: 0.21 para 21%).",
        ["IrcEstimateFiling.AdjustmentsJson"] = "As correções fiscais indicadas pelo utilizador, em JSON.",
        ["IrcEstimateFiling.SnapshotJson"] = "A fotografia da estimativa calculada (resultado líquido, correções, resultado fiscal, coleta) em JSON.",
    };
}
