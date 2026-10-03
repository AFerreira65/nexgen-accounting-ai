export type MovementLine = {
  account: string
  accountName: string
  description: string
  debit: number
  credit: number
  partyCountryCode?: string | null
  partyTaxNumber?: string | null
  partyName?: string | null
  vatCode?: string | null
}

export type Movement = {
  id: string
  date: string
  journalId: string
  journalName: string
  documentType?: string | null
  documentTypeName?: string | null
  document: string
  documentDate?: string | null
  description: string
  notes?: string | null
  status: 'Conferido' | 'Por conferir' | 'Anulado'
  source?: 'Manual' | 'SaftImport' | 'Opening' | 'Closing'
  amount: number
  lines: MovementLine[]
}

export type AccountingAccount = {
  code: string
  name: string
  parentCode: string | null
  allowsPosting: boolean
  taxonomyReference: string | null
  taxonomyCode: number | null
  isActive: boolean
  sourceTemplateVersion: string | null
}

export type AccountingJournal = {
  code: string
  name: string
  isActive: boolean
  sourceTemplateVersion: string | null
}

export type AccountingDocumentType = {
  code: string
  name: string
  defaultJournalCode: string
  isActive: boolean
  sourceTemplateVersion: string | null
}

export type AccountingSetup = {
  standard: string
  template: {
    code: string
    name: string
    accountingSetupVersion: string | null
    legalSource: string
    taxonomyReference: string
    taxonomyName: string
    taxonomyLegalSource: string
  }
  accounts: AccountingAccount[]
  journals: AccountingJournal[]
  documentTypes: AccountingDocumentType[]
}

export type TaxonomyReviewStatus = 'Assigned' | 'SuggestionAvailable' | 'Unclassified' | 'ReferenceMismatch'

export type TaxonomyReviewAccount = {
  code: string
  name: string
  taxonomyReference: string | null
  taxonomyCode: number | null
  suggestedCode: number | null
  status: TaxonomyReviewStatus
}

export type TaxonomyReview = {
  reference: string
  taxonomyName: string
  taxonomyLegalSource: string
  postingAccountCount: number
  classifiedCount: number
  accounts: TaxonomyReviewAccount[]
}

// Correspondências de taxonomia inequívocas incluídas no plano de contas base
// versionado (conta 11 = Caixa, conta 12 = Depósitos à ordem). Espelha
// AccountingTemplateCatalog no servidor; as restantes contas ficam por classificar.
export const templateTaxonomySuggestions: Record<string, number> = { '111': 1, '121': 2 }

export type TaxonomyCatalogEntry = { code: number; description: string; sncAccounts: string[] }

export type TaxonomyCatalog = {
  reference: string
  source: string
  disclaimer: string
  codes: TaxonomyCatalogEntry[]
}

// Amostra usada apenas no modo de demonstração; o catálogo completo (Portaria
// 302/2016) chega pela API.
export const demoTaxonomyCatalog: TaxonomyCatalog = {
  reference: 'S',
  source: 'Amostra de demonstração — o catálogo completo chega pela API local.',
  disclaimer: 'Transcrição a confirmar contra o texto oficial.',
  codes: [
    { code: 1, description: 'Caixa', sncAccounts: ['11', '111'] },
    { code: 2, description: 'Depósitos à ordem', sncAccounts: ['12', '121'] },
    { code: 3, description: 'Outros depósitos bancários', sncAccounts: ['13'] },
    { code: 22, description: 'Clientes - Outros Clientes', sncAccounts: ['213', '214', '215', '216', '217'] },
    { code: 357, description: 'Fornecimentos e serviços externos - Serviços especializados - Trabalhos especializados', sncAccounts: ['6221'] },
  ],
}

export function buildTaxonomyReview(setup: AccountingSetup): TaxonomyReview {
  const reference = setup.template.taxonomyReference
  const posting = setup.accounts.filter((account) => account.allowsPosting && account.isActive)
  const accounts: TaxonomyReviewAccount[] = posting
    .map((account) => {
      const suggestedCode = account.code in templateTaxonomySuggestions ? templateTaxonomySuggestions[account.code] : null
      const status: TaxonomyReviewStatus = account.taxonomyCode !== null
        ? (account.taxonomyReference === reference ? 'Assigned' : 'ReferenceMismatch')
        : suggestedCode !== null ? 'SuggestionAvailable' : 'Unclassified'
      return {
        code: account.code,
        name: account.name,
        taxonomyReference: account.taxonomyReference,
        taxonomyCode: account.taxonomyCode,
        suggestedCode,
        status,
      }
    })
    .sort((a, b) => a.code.localeCompare(b.code, 'pt-PT'))
  return {
    reference,
    taxonomyName: setup.template.taxonomyName,
    taxonomyLegalSource: setup.template.taxonomyLegalSource,
    postingAccountCount: posting.length,
    classifiedCount: posting.filter((account) => account.taxonomyCode !== null).length,
    accounts,
  }
}

export const movements: Movement[] = [
  {
    id: 'COMP-2026-00842',
    date: '2026-08-29',
    journalId: 'COMPRAS',
    journalName: 'Diário de Compras',
    document: 'FT 2026/418 · Norte Office, Lda.',
    description: 'Aquisição de material de escritório',
    notes: 'Confirmar condições de pagamento a 30 dias com o fornecedor.',
    status: 'Conferido',
    amount: 1476,
    lines: [
      { account: '6221', accountName: 'Material de escritório', description: 'Base tributável', debit: 1200, credit: 0 },
      { account: '24323', accountName: 'IVA dedutível — Outros bens e serviços', description: 'IVA 23%', debit: 276, credit: 0 },
      { account: '2211001', accountName: 'Norte Office, Lda.', description: 'Conta corrente do fornecedor', debit: 0, credit: 1476 },
    ],
  },
  {
    id: 'VEND-2026-00327',
    date: '2026-08-28',
    journalId: 'VENDAS',
    journalName: 'Diário de Vendas',
    document: 'FT A/327 · Maré Clara, SA',
    description: 'Prestação de serviços de consultoria',
    status: 'Conferido',
    amount: 3075,
    lines: [
      { account: '2111001', accountName: 'Maré Clara, SA', description: 'Conta corrente do cliente', debit: 3075, credit: 0 },
      { account: '7211', accountName: 'Serviços prestados — Continente', description: 'Base tributável', debit: 0, credit: 2500 },
      { account: '24331', accountName: 'IVA liquidado', description: 'IVA 23%', debit: 0, credit: 575 },
    ],
  },
  {
    id: 'BANC-2026-00196',
    date: '2026-08-27',
    journalId: 'BANCOS',
    journalName: 'Diário de Bancos',
    document: 'TRF 908433 · Norte Office, Lda.',
    description: 'Pagamento a fornecedor por transferência',
    status: 'Por conferir',
    amount: 1476,
    lines: [
      { account: '2211001', accountName: 'Norte Office, Lda.', description: 'Liquidação do fornecedor', debit: 1476, credit: 0 },
      { account: '1211', accountName: 'Banco Atlântico — DO', description: 'Saída bancária', debit: 0, credit: 1476 },
    ],
  },
]

export const islandMovements: Movement[] = [
  {
    id: 'VEND-AZO-2026-00014',
    date: '2026-08-26',
    journalId: 'VENDAS',
    journalName: 'Diário de Vendas — Açores',
    document: 'FT AZ/14 · Hotel Bruma, Lda.',
    description: 'Serviços de apoio administrativo',
    status: 'Por conferir',
    amount: 1160,
    lines: [
      { account: '2111001', accountName: 'Hotel Bruma, Lda.', description: 'Conta corrente do cliente', debit: 1160, credit: 0 },
      { account: '7212', accountName: 'Serviços prestados — Açores', description: 'Base tributável', debit: 0, credit: 1000 },
      { account: '24332', accountName: 'IVA liquidado — Açores', description: 'IVA 16%', debit: 0, credit: 160 },
    ],
  },
]

export const fallbackMovementsByCompany: Record<string, Movement[]> = {
  '22222222-2222-2222-2222-222222222222': movements,
  '44444444-4444-4444-4444-444444444444': islandMovements,
}

const templateSource = 'PT-CC-218-2015-NCRF-v1'
const demoJournals: AccountingJournal[] = [
  { code: 'GERAL', name: 'Diário de operações diversas', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'COMPRAS', name: 'Diário de compras', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'VENDAS', name: 'Diário de vendas', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'BANCOS', name: 'Diário de bancos', isActive: true, sourceTemplateVersion: templateSource },
]
const demoDocuments: AccountingDocumentType[] = [
  { code: 'FTF', name: 'Fatura de fornecedor', defaultJournalCode: 'COMPRAS', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'FTV', name: 'Fatura a cliente', defaultJournalCode: 'VENDAS', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'RCB', name: 'Recebimento', defaultJournalCode: 'BANCOS', isActive: true, sourceTemplateVersion: templateSource },
  { code: 'OD', name: 'Operação diversa', defaultJournalCode: 'GERAL', isActive: true, sourceTemplateVersion: templateSource },
]
const demoAccounts: AccountingAccount[] = [
  { code: '1', name: 'Meios financeiros líquidos', parentCode: null, allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '11', name: 'Caixa', parentCode: '1', allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '111', name: 'Caixa — geral', parentCode: '11', allowsPosting: true, taxonomyReference: 'S', taxonomyCode: 1, isActive: true, sourceTemplateVersion: templateSource },
  { code: '12', name: 'Depósitos à ordem', parentCode: '1', allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '121', name: 'Depósito à ordem — geral', parentCode: '12', allowsPosting: true, taxonomyReference: 'S', taxonomyCode: 2, isActive: true, sourceTemplateVersion: templateSource },
  { code: '2', name: 'Contas a receber e a pagar', parentCode: null, allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '21', name: 'Clientes', parentCode: '2', allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '2111001', name: 'Maré Clara, SA', parentCode: '21', allowsPosting: true, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null },
  { code: '22', name: 'Fornecedores', parentCode: '2', allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '2211001', name: 'Norte Office, Lda.', parentCode: '22', allowsPosting: true, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null },
  { code: '6', name: 'Gastos', parentCode: null, allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '62', name: 'Fornecimentos e serviços externos', parentCode: '6', allowsPosting: false, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: templateSource },
  { code: '6221', name: 'Material de escritório', parentCode: '62', allowsPosting: true, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null },
]

export const fallbackAccountingSetupsByCompany: Record<string, AccountingSetup> = {
  '22222222-2222-2222-2222-222222222222': {
    standard: 'SncGeneral',
    template: { code: 'NCRF', name: 'SNC — Regime geral', accountingSetupVersion: templateSource, legalSource: '', taxonomyReference: 'S', taxonomyName: 'Taxonomia S — SNC base', taxonomyLegalSource: '' },
    accounts: demoAccounts,
    journals: demoJournals,
    documentTypes: demoDocuments,
  },
  '44444444-4444-4444-4444-444444444444': {
    standard: 'SncMicroEntities',
    template: { code: 'NCRF-ME', name: 'SNC — Microentidades', accountingSetupVersion: 'PT-CC-218-2015-NCRF-ME-v1', legalSource: '', taxonomyReference: 'M', taxonomyName: 'Taxonomia M — SNC Microentidades', taxonomyLegalSource: '' },
    accounts: demoAccounts.map((account) => ({ ...account, taxonomyReference: account.taxonomyCode ? 'M' : null })),
    journals: demoJournals,
    documentTypes: demoDocuments,
  },
}

export type SaftInspectionHeader = {
  auditFileVersion: string
  taxRegistrationNumber: string
  taxAccountingBasis: string
  companyName: string
  businessName: string | null
  fiscalYear: number
  startDate: string
  endDate: string
  currencyCode: string
  dateCreated: string | null
  taxEntity: string | null
  productId: string | null
  includesAccounting: boolean
}

export type SaftCompanyMatchOutcome = 'ExistingCompany' | 'NewCompanyProposed'

export type SaftCompanyCreationInput = {
  taxRegistrationNumber: string
  legalName: string
  taxRegion: 'Mainland' | 'Azores' | 'Madeira'
  accountingStandard: string
  baseCurrency: string
  fiscalYear: number
}

export type SaftCompanyCreation = {
  companyId: string
  legalName: string
  taxRegistrationNumber: string
  accountingStandard: string
  accountingSetupVersion: string | null
  accountCount: number
  journalCount: number
  documentTypeCount: number
}

export type SaftInspection = {
  header: SaftInspectionHeader
  match: {
    outcome: SaftCompanyMatchOutcome
    companyId: string | null
    legalName: string | null
    accountingStandard: string | null
    accountingSetupVersion: string | null
  }
}

export type SaftValidationSeverity = 'Error' | 'Warning' | 'Information'

export type SaftValidationIssue = {
  ruleId: string | null
  severity: SaftValidationSeverity
  scope: string
  message: string
  line: number | null
  position: number | null
  path: string | null
}

export type SaftValidation = {
  version: string | null
  schemaAvailable: boolean
  schemaValid: boolean
  counts: { error: number; warning: number; information: number }
  issues: SaftValidationIssue[]
  truncated: boolean
}

export type SaftValidationRunSummary = SaftValidation & {
  id: string
  fileName: string
  createdAtUtc: string
}

export type EFaturaImportResult = {
  batchId: string | null
  imported: number
  alreadyExisted: number
  invalid: number
  invalidRows: { lineNumber: number; reason: string }[]
  warnings: string[]
}

export type EFaturaPurchaseRow = {
  id: string
  supplierTaxId: string
  supplierName: string
  invoiceNo: string
  atcud: string | null
  invoiceDate: string
  invoiceType: string
  status: string | null
  sector: string | null
  taxBase: number
  taxAmount: number
  grossTotal: number
  sourceFileName: string
  createdAtUtc: string
}

export type EFaturaPurchasesPage = {
  items: EFaturaPurchaseRow[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export type CrossCheckPurchaseMonth = {
  year: number
  month: number
  efaturaBase: number
  efaturaVat: number
  efaturaCount: number
  accountingBase: number
  accountingVat: number
  accountingEntryCount: number
  efaturaTotal: number
  accountingTotal: number
  baseDelta: number
  vatDelta: number
  totalDelta: number
  balanced: boolean
}

export type CrossCheckUnbookedInvoice = {
  id: string
  supplierTaxId: string
  supplierName: string
  invoiceNo: string
  atcud: string | null
  invoiceDate: string
  taxBase: number
  taxAmount: number
  grossTotal: number
}

export type CrossCheckUnmatchedEntry = {
  reference: string
  entryDate: string
  documentReference: string
  description: string
  purchaseBase: number
  deductibleVat: number
  total: number
}

export type CrossCheckPurchases = {
  efaturaBase: number
  efaturaVat: number
  efaturaCount: number
  accountingBase: number
  accountingVat: number
  accountingEntryCount: number
  matchedCount: number
  months: CrossCheckPurchaseMonth[]
  efaturaWithoutAccounting: CrossCheckUnbookedInvoice[]
  accountingWithoutEfatura: CrossCheckUnmatchedEntry[]
  efaturaTotal: number
  accountingTotal: number
  baseDelta: number
  vatDelta: number
  totalDelta: number
  balanced: boolean
  unbookedCount: number
}

export type CrossCheckSalesMonth = {
  year: number
  month: number
  income: number
  outputVat: number
  total: number
}

export type CrossCheckSales = {
  income: number
  outputVat: number
  months: CrossCheckSalesMonth[]
  total: number
}

export type CrossCheckTotals = {
  periodDebit: number
  periodCredit: number
  balanced: boolean
}

export type CrossCheckReport = {
  from: string
  to: string
  purchases: CrossCheckPurchases
  sales: CrossCheckSales
  totals: CrossCheckTotals
}

export type SaftSalesInvoiceMonth = {
  year: number
  month: number
  count: number
  net: number
  tax: number
  gross: number
}

export type AnalysisCatalogField = {
  key: string
  label: string
  type: 'Text' | 'Number' | 'Money' | 'Date'
  groupable: boolean
  aggregatable: boolean
}

export type AnalysisCatalog = {
  sources: { key: string; fields: AnalysisCatalogField[] }[]
  operatorsByType: Record<string, string[]>
}

export type AnalysisFilter = { field: string; operator: string; value: string }
export type AnalysisSort = { field: string; descending: boolean }
export type AnalysisAggregate = { field: string; function: 'sum' | 'count' | 'average' | 'min' | 'max' }

export type AnalysisDefinition = {
  source: string
  columns: string[]
  filters: AnalysisFilter[]
  groupBy: string[]
  sort: AnalysisSort[]
  aggregates: AnalysisAggregate[]
  includeGrandTotal: boolean
}

export type SavedAnalysisView = {
  id: string
  name: string
  description: string | null
  definition: AnalysisDefinition
  createdAtUtc: string | null
}

export type AnalysisColumn = { key: string; label: string; type: string }
export type AnalysisGroupKey = { field: string; label: string; value: unknown }
export type AnalysisGroup = {
  keys: AnalysisGroupKey[]
  subtotals: Record<string, number>
  rowCount: number
}
export type AnalysisResult = {
  columns: AnalysisColumn[]
  rows: Record<string, unknown>[]
  groups: AnalysisGroup[]
  grandTotal: Record<string, number>
  rowCount: number
  truncated: boolean
}

export type ReportTemplate = {
  reportKey: string
  headerLines: string[]
  showFirmName: boolean
  showCompanyIdentity: boolean
  orientation: 'Portrait' | 'Landscape'
  companyName: string
  companyTaxId: string
  firmName: string
}

export type ManagementReportCatalogEntry = {
  key: string
  label: string
  description: string
  sections: string[]
}

export type AiSettings = {
  enabled: boolean
  autoMonthly: boolean
  narratorConfigured: boolean
  model: string
}

export type ManagementReportReference = { type: string; code: string }

export type ManagementReportSection = {
  title: string
  body: string
  references: ManagementReportReference[]
}

export type ManagementReportIndicator = { name: string; value: string; comment: string | null }

export type ManagementReportResult = {
  summary: string
  sections: ManagementReportSection[]
  alerts: string[]
  indicators: ManagementReportIndicator[]
  aiNotice: string
}

export type ManagementReportView = {
  id: string
  kind: string
  question: string | null
  periodFrom: string
  periodTo: string
  model: string
  promptTokens: number
  outputTokens: number
  createdAtUtc: string | null
  result: ManagementReportResult | null
  dossier: unknown
}

export type VatReturnAccountLine = {
  accountCode: string
  accountName: string
  role: 'liquidado' | 'dedutivel' | 'regularizacoes' | 'reporte' | 'outras'
  debit: number
  credit: number
  net: number
}

export type VatReturn = {
  from: string
  to: string
  outputVat: number
  inputVat: number
  regularisationsForState: number
  regularisationsForTaxpayer: number
  priorPeriodCredit: number
  periodResult: number
  netPayable: number
  creditToCarryForward: number
  priorCreditAssumed: boolean
  lines: VatReturnAccountLine[]
}

export type VatReturnFilingView = {
  saved: boolean
  savedAtUtc: string | null
  priorPeriodCredit: number
  suggestedPriorCredit: number
  priorFromPreviousFiling: boolean
  return: VatReturn
}

export type VatReturnFilingSummary = {
  id: string
  periodFrom: string
  periodTo: string
  netPayable: number
  creditToCarryForward: number
  savedAtUtc: string | null
}

export type AtCertificateStatus = {
  configured: boolean
  valid: boolean
  subject: string | null
  issuer: string | null
  serialNumber: string | null
  thumbprint: string | null
  notBeforeUtc: string | null
  notAfterUtc: string | null
  hasPrivateKey: boolean
  environment: string
  problem: string | null
}

export type AtCredentials = {
  configured: boolean
  subUser: string | null
  periodicity: 'Mensal' | 'Trimestral'
  environment: 'Testes' | 'Producao'
  updatedAtUtc: string | null
}

export type EntryTemplateLineRow = {
  kind: string
  side: string
  accountCode: string
  accountName: string
  vatCode: string | null
  description: string | null
}

export type EntryTemplateRow = {
  code: string
  name: string
  journalCode: string
  documentTypeCode: string | null
  description: string | null
  isActive: boolean
  lines: EntryTemplateLineRow[]
}

export type AppliedTemplate = {
  code: string
  journalCode: string
  documentTypeCode: string | null
  description: string | null
  lines: { kind: string; accountCode: string; description: string; debit: number; credit: number; vatCode: string | null }[]
}

export type VatCodeRow = {
  code: string
  name: string
  rate: string
  percentage: number
  nature: string
  region: string
  isActive: boolean
}

export type PartyRow = {
  id: string
  countryCode: string
  taxRegistrationNumber: string
  name: string
  isCustomer: boolean
  isSupplier: boolean
  customerAccountCode: string | null
  supplierAccountCode: string | null
  isActive: boolean
}

export type PartyPage = { total: number; items: PartyRow[] }

export type SaftPartyLinkResult = {
  applied: boolean
  transactionCount: number
  entriesMatched: number
  entriesNotFound: number
  entriesChanged: number
  linesLinked: number
  linesAlreadyLinked: number
  transactionsWithUnknownParty: number
}

export type PartyBalances = {
  total: number
  totalDebit: number
  totalCredit: number
  totalBalance: number
  items: { partyId: string; countryCode: string; taxRegistrationNumber: string; name: string; debit: number; credit: number; balance: number }[]
}

export type PartyStatement = {
  partyId: string
  countryCode: string
  taxRegistrationNumber: string
  name: string
  from: string | null
  to: string | null
  openingBalance: number
  periodDebit: number
  periodCredit: number
  closingBalance: number
  total: number
  lines: { date: string; reference: string; documentReference: string; accountCode: string; description: string; debit: number; credit: number; balance: number }[]
}

export type PartyImportItem = Omit<PartyRow, 'id' | 'isActive'> & { state: 'New' | 'Updated' | 'Unchanged' }

export type PartyImportResult = {
  applied: boolean
  sourceCount: number
  newCount: number
  updatedCount: number
  unchangedCount: number
  skippedCount: number
  items: PartyImportItem[]
  skipped: { reason: string; count: number }[]
}

export type VatClassificationRow = {
  accountCode: string
  accountName: string
  class: string
  rate: string
  nature: string
  region: string
  classified: boolean
}

export type VatDeclarationField = {
  number: number
  note: string | null
}

export type VatSummaryRow = {
  kind: 'base' | 'imposto' | 'outras'
  nature: string
  rate: string
  region: string
  debit: number
  credit: number
  net: number
  accountCount: number
  field: VatDeclarationField | null
}

export type VatSummaryUnclassifiedRow = {
  accountCode: string
  accountName: string
  debit: number
  credit: number
  net: number
}

export type VatSummary = {
  from: string
  to: string
  rows: VatSummaryRow[]
  unclassified: VatSummaryUnclassifiedRow[]
  fieldMappingSource: string
  fieldMappingDisclaimer: string
}

export type IrcAdjustment = {
  description: string
  amount: number
}

export type IrcEstimate = {
  from: string
  to: string
  netIncome: number
  adjustments: IrcAdjustment[]
  adjustmentsTotal: number
  taxableResult: number
  taxRate: number
  estimatedTax: number
}

export type IrcEstimateFilingView = {
  saved: boolean
  savedAtUtc: string | null
  taxRate: number | null
  adjustments: IrcAdjustment[]
  estimate: IrcEstimate | null
}

export type IrcEstimateFilingSummary = {
  id: string
  periodFrom: string
  periodTo: string
  taxRate: number
  estimatedTax: number
  savedAtUtc: string | null
}

export type RecordInfoParty = { id: string; name: string | null }

export type RecordInfo = {
  table: string
  id: string
  createdBy: RecordInfoParty | null
  createdAtUtc: string | null
  modifiedBy: RecordInfoParty | null
  modifiedAtUtc: string | null
}

export type DataDictionaryColumn = {
  name: string
  clrType: string
  sqlType: string
  nullable: boolean
  maxLength: number | null
  precision: number | null
  scale: number | null
  primaryKey: boolean
  foreignKey: boolean
  references: string | null
  unique: boolean
  indexed: boolean
  description: string
}

export type DataDictionaryTableSummary = {
  schema: string
  name: string
  purpose: string
  columnCount: number
}

export type DataDictionaryTable = {
  schema: string
  name: string
  purpose: string
  columnCount: number
  columns: DataDictionaryColumn[]
}

export type SaftSalesInvoicesSummary = {
  hasSalesInvoices: boolean
  declaredNumberOfEntries: number | null
  declaredTotalDebit: number | null
  declaredTotalCredit: number | null
  invoiceCount: number
  voidedCount: number
  net: number
  tax: number
  gross: number
  fromDate: string | null
  toDate: string | null
  months: SaftSalesInvoiceMonth[]
}

// Extração do cabeçalho no cliente, usada apenas no modo de demonstração quando a
// API local não está ligada. Namespace-agnóstica, como o leitor do servidor.
export function parseSaftHeaderFromXml(text: string): SaftInspectionHeader {
  const doc = new DOMParser().parseFromString(text, 'application/xml')
  if (doc.getElementsByTagName('parsererror').length > 0) {
    throw new Error('O ficheiro não é um XML SAF-T legível.')
  }

  const field = (name: string): string | null => {
    const byNs = doc.getElementsByTagNameNS('*', name)[0]
    const el = byNs ?? Array.from(doc.getElementsByTagName('*')).find((node) => node.localName === name)
    const value = el?.textContent?.trim()
    return value ? value : null
  }

  const required = (name: string): string => {
    const value = field(name)
    if (!value) throw new Error(`O cabeçalho do SAF-T não tem ${name}.`)
    return value
  }

  const startDate = required('StartDate')
  const endDate = required('EndDate')
  if (endDate < startDate) throw new Error('A data final do período é anterior à data inicial.')
  const basis = required('TaxAccountingBasis').toUpperCase()

  return {
    auditFileVersion: required('AuditFileVersion'),
    taxRegistrationNumber: required('TaxRegistrationNumber').toUpperCase(),
    taxAccountingBasis: basis,
    companyName: required('CompanyName'),
    businessName: field('BusinessName'),
    fiscalYear: Number(required('FiscalYear')),
    startDate,
    endDate,
    currencyCode: (field('CurrencyCode') ?? 'EUR').toUpperCase(),
    dateCreated: field('DateCreated'),
    taxEntity: field('TaxEntity'),
    productId: field('ProductID'),
    includesAccounting: basis === 'C' || basis === 'I',
  }
}

export type SaftAccountStatus = { code: string; description: string; state: 'Existing' | 'New' }

export type SaftAccountsPreview = {
  fileAccountCount: number
  existingCount: number
  newCount: number
  companyPostingAccountCount: number
  accounts: SaftAccountStatus[]
}

export type SaftAccountsImport = {
  createdCount: number
  alreadyExistedCount: number
  created: { code: string; name: string; parentCode: string | null; allowsPosting: boolean }[]
}

export type SaftOpeningBalancesImport = {
  lineCount: number
  totalDebit: number
  totalCredit: number
  entryReference: string | null
  alreadyImported: boolean
}

export type SaftEntriesPreview = {
  hasEntries: boolean
  transactionCount: number
  declaredEntryCount: number | null
  totalDebit: number | null
  totalCredit: number | null
  balanced: boolean
  fromDate: string | null
  toDate: string | null
  journals: { code: string; description: string; transactionCount: number; exists: boolean }[]
}

export type SaftEntriesAnalysis = {
  wouldImportCount: number
  wouldSkipCount: number
  journalsToCreate: string[]
  skipped: { transactionId: string; reason: string }[]
  declaredEntryCount: number | null
  declaredTotalDebit: number | null
  declaredTotalCredit: number | null
  filePeriodFrom: string | null
  filePeriodTo: string | null
  projectedPeriodDebit: number
  projectedPeriodCredit: number
  wouldMatchDeclaredTotals: boolean | null
}

export type SaftEntriesImport = {
  importedCount: number
  skippedCount: number
  journalsCreated: string[]
  skipped: { transactionId: string; reason: string }[]
  declaredEntryCount: number | null
  declaredTotalDebit: number | null
  declaredTotalCredit: number | null
  filePeriodFrom: string | null
  filePeriodTo: string | null
  companyPeriodDebit: number
  companyPeriodCredit: number
  matchesDeclaredTotals: boolean | null
  linesWithParty?: number
  transactionsWithUnknownParty?: number
}

// Extração do plano geral de contas no cliente, usada apenas no modo de
// demonstração. Namespace-agnóstica e a parar antes dos movimentos, como o leitor
// do servidor.
export function parseSaftAccountsFromXml(text: string): { code: string; description: string }[] {
  const doc = new DOMParser().parseFromString(text, 'application/xml')
  if (doc.getElementsByTagName('parsererror').length > 0) {
    throw new Error('O ficheiro não é um XML SAF-T legível.')
  }
  const block = doc.getElementsByTagNameNS('*', 'GeneralLedgerAccounts')[0]
    ?? Array.from(doc.getElementsByTagName('*')).find((node) => node.localName === 'GeneralLedgerAccounts')
  if (!block) return []

  const seen = new Set<string>()
  const result: { code: string; description: string }[] = []
  for (const account of Array.from(block.children).filter((node) => node.localName === 'Account')) {
    const child = (name: string) => Array.from(account.children).find((node) => node.localName === name)?.textContent?.trim() ?? ''
    const code = child('AccountID')
    if (!code || seen.has(code.toUpperCase())) continue
    seen.add(code.toUpperCase())
    result.push({ code, description: child('AccountDescription') })
  }
  return result
}

export const euro = new Intl.NumberFormat('pt-PT', { style: 'currency', currency: 'EUR' })

export function formatMovementDate(value: string) {
  return new Intl.DateTimeFormat('pt-PT', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(`${value}T00:00:00Z`))
}
