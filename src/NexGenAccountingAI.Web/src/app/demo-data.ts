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
  costCenter?: string | null
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
  source?: 'Manual' | 'SaftImport' | 'Opening' | 'Closing' | 'EFatura' | 'SaftBilling' | 'BankStatement'
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

export type SaftExportReport = {
  fiscalYear: number
  accountCount: number
  partyCount: number
  transactionCount: number
  totalDebit: number
  totalCredit: number
  issues: { blocking: boolean; message: string; count: number }[]
  canExport: boolean
}

export type CostCenterRow = { code: string; name: string; isActive: boolean }

export type AssetDisposalPreview = { assetCode: string; description: string; date: string; acquisitionValue: number; accumulated: number; netBookValue: number; salePrice: number; result: number; kind: 'alienação' | 'abate'; lines: { accountCode: string; accountName: string; debit: number; credit: number }[]; warning: string | null }
export type FixedAssetRow = {
  code: string
  description: string
  acquisitionDate: string
  startOfUseDate: string
  acquisitionValue: number
  residualValue: number
  annualRate: number
  byMonths: boolean
  assetAccountCode: string | null
  accumulatedAccountCode: string | null
  expenseAccountCode: string | null
  disposalDate: string | null
  isActive: boolean
  taxTableCode?: string | null
  taxRate?: number | null
  disposalPrice?: number | null
  monetaryCoefficient?: number | null
}
export type Modelo32Line = { assetCode: string; taxTableCode: string | null; description: string; startMonth: number | null; startYear: number; bookValue: number; taxValue: number; usefulLifeYears: number | null; bookedInPeriod: number; acceptedInPriorPeriods: number; rate: number; correctedRate: number | null; taxLimit: number; acceptedImpairment: number; lostRates: number | null; notAccepted: number; recovered: number; rateFromAccounting: boolean }
export type Modelo32Map = { nature: number; natureLabel: string; method: number; lines: Modelo32Line[]; bookValue: number; taxValue: number; bookedInPeriod: number; acceptedInPriorPeriods: number; taxLimit: number; acceptedImpairment: number; notAccepted: number; recovered: number }
export type Modelo31Line = { assetCode: string; description: string; disposalDate: string; realisationValue: number; taxAcquisitionValue: number; acquisitionYear: number; bookAcquisitionValue: number; bookDepreciation: number; bookGain: number; untaxedGain: number; acceptedDepreciation: number; coefficient: number; coefficientIndicated: boolean; taxGain: number }
export type Modelo31Map = { nature: number; natureLabel: string; lines: Modelo31Line[]; bookGain: number; taxGain: number }
export type DossierMaps = { year: number; modelo32: Modelo32Map[]; ledgerDepreciation: number; fullyDepreciatedOmitted: number; modelo31: Modelo31Map[]; warnings: string[]; sources: string[]; modelo32Booked: number; modelo32NotAccepted: number; modelo31BookGain: number; modelo31TaxGain: number }
export type DepreciationSchedule = {
  year: number
  rows: { code: string; description: string; startOfUseDate: string; disposalDate: string | null; acquisitionValue: number; annualRate: number; accumulatedAtStart: number; depreciation: number; accumulatedAtEnd: number; netValue: number; expenseAccountCode: string | null }[]
  totalAcquisitionValue: number
  totalDepreciation: number
  totalAccumulatedAtEnd: number
  totalNetValue: number
  ledgerDepreciation: number
  difference: number
}

export type StatementLine = { section: string; label: string; amount: number; comparative: number; isTotal: boolean }
export type StatementExcludedAccount = { accountCode: string; accountName: string; balance: number; reason: string }
export type BalanceSheetStatement = {
  date: string
  comparativeDate: string
  model: string
  lines: StatementLine[]
  totalAssets: number
  totalEquity: number
  totalLiabilities: number
  difference: number
  excluded: StatementExcludedAccount[]
  source: string
}
export type RecapitulativeStatement = {
  from: string
  to: string
  period: string | null
  lines: { prefix: string; taxNumber: string; partyName: string; value: number; exactValue: number; operationType: 1 | 4 | 5 }[]
  field10: number
  field17: number
  field18: number
  field19: number
  issues: { entryReference: string; entryDate: string; accountCode: string; amount: number; reason: string }[]
  source: string
  disclaimer: string
}
export type PartyAging = {
  date: string
  kind: string
  bucketLabels: string[]
  rows: { partyId: string; countryCode: string; taxRegistrationNumber: string; name: string; open: number; buckets: number[]; advance: number; oldestDate: string | null }[]
  totalOpen: number
  totalBuckets: number[]
  totalAdvance: number
  disclaimer: string
}
export type FiscalDossier = {
  year: number
  elements: { number: number; title: string; support: string; link: string | null; linkLabel: string | null; evidence: string | null; status: string; note: string | null }[]
  pending: number
  source: string
  disclaimer: string
}
export type WithholdingReport = {
  year: number
  accounts: { accountCode: string; accountName: string; opening: number; withheld: number; delivered: number; balance: number }[]
  months: { month: number; accountCode: string; withheld: number; delivered: number }[]
  beneficiaries: { countryCode: string; taxNumber: string; name: string; accountCode: string; withheld: number; incomeEstimate: number; entries: number }[]
  totalWithheld: number
  totalDelivered: number
  totalBalance: number
  withheldWithoutParty: number
  disclaimer: string
}
export type IesAnnexA = { year: number; norm: string; fields: { code: string; quadro: string; section: string; label: string; amount: number; computed: boolean }[]; warnings: string[]; source: string; disclaimer: string }
export type Modelo22Field = { code: string; section: string; label: string; amount: number; computed: boolean; note: string | null; suggested: number | null; suggestionSource: string | null }
export type Modelo22Return = { year: number; smallOrMediumEnterprise: boolean; municipalSurtaxRate: number; fields: Modelo22Field[]; warnings: string[]; source: string; disclaimer: string }
export type VatSettlementEntryPreview = {
  from: string
  to: string
  period: string | null
  lines: { accountCode: string; accountName: string; debit: number; credit: number }[]
  payable: number
  recoverable: number
  priorCredit: number
  postedReference: string | null
  blocker: string | null
}
export type VatFormField = { number: number; label: string; column: 'base' | 'sujeito-passivo' | 'estado' | 'apuramento'; amount: number; computed: boolean; supported: boolean; sources: { accountCode: string; accountName: string; amount: number }[] }
export type VatDeclaration = {
  from: string
  to: string
  period: string | null
  seat: string
  fields: VatFormField[]
  warnings: string[]
  accountingPayable: number
  accountingCredit: number
  difference: number
  source: string
  disclaimer: string
}
export type OperatingMap = {
  year: number
  lastMonth: number
  model: string
  rows: { label: string; months: number[]; total: number; isTotal: boolean; budget: number[] | null }[]
  excluded: StatementExcludedAccount[]
  hasBudget: boolean
  unassignedBudget: { accountCode: string; costCenterCode: string | null; amount: number; reason: string }[]
}
export type FinancialRatio = { group: string; label: string; value: number | null; comparative: number | null; unit: 'x' | '%' | 'dias' | '€'; formula: string }
export type FinancialRatios = { from: string; to: string; model: string; days: number; ratios: FinancialRatio[]; balanceDifference: number; excludedAccounts: number }
export type BudgetReport = {
  year: number
  month: number
  lines: { accountCode: string; accountName: string; costCenterCode: string | null; kind: 'rendimento' | 'gasto'; annualBudget: number; budget: number; actual: number; deviation: number; deviationPercent: number | null; favourable: boolean; monthlyBudget: number[] }[]
  budgetIncome: number
  actualIncome: number
  budgetExpenses: number
  actualExpenses: number
  budgetResult: number
  actualResult: number
  unbudgetedIncome: number
  unbudgetedExpenses: number
}
export type SuggestedEntryLine = { accountCode: string; accountName: string; description: string; debit: number; credit: number; vatCode: string | null; partyTaxNumber: string | null }
export type SuggestedEntry = {
  purchaseId: string
  supplierTaxId: string
  supplierName: string
  invoiceNo: string
  invoiceDate: string
  invoiceType: string
  isCreditNote: boolean
  taxBase: number
  taxAmount: number
  grossTotal: number
  entryTemplateCode: string | null
  journalCode: string | null
  documentTypeCode: string | null
  description: string
  lines: SuggestedEntryLine[]
  problem: string | null
  warning: string | null
  ready: boolean
}
export type DismissedPurchase = { purchaseId: string; supplierTaxId: string; supplierName: string; invoiceNo: string; invoiceDate: string; grossTotal: number; reason: string }
export type SuggestedEntriesReport = { from: string; to: string; purchaseCount: number; bookedCount: number; cancelledCount: number; readyCount: number; withoutRuleCount: number; items: SuggestedEntry[]; dismissed: DismissedPurchase[] }
export type SuggestedEntryOutcome = { documentId: string; reference: string | null; error: string | null }
export type SalesDocumentsImportResult = { batchId: string | null; read: number; imported: number; alreadyExisted: number; voided: number; from: string | null; to: string | null; warnings: string[] }
export type SalesSuggestedEntry = {
  documentId: string
  invoiceNo: string
  invoiceType: string
  invoiceDate: string
  customerTaxId: string
  customerName: string
  isCreditNote: boolean
  netTotal: number
  taxPayable: number
  grossTotal: number
  percentages: number[]
  journalCode: string | null
  documentTypeCode: string | null
  description: string
  lines: SuggestedEntryLine[]
  problem: string | null
  warning: string | null
  ready: boolean
}
export type SalesSuggestedEntriesReport = { from: string; to: string; documentCount: number; bookedCount: number; voidedCount: number; readyCount: number; percentagesWithoutRule: number[]; items: SalesSuggestedEntry[] }
export type SalesEntryRuleRow = { taxPercentage: number; entryTemplateCode: string; entryTemplateName: string; isActive: boolean }
export type BankAccountRow = { accountCode: string; accountName: string; lineCount: number; unreconciledCount: number; lastMovementDate: string | null; iban: string | null }
export type BankStatementImportResult = { importId: string | null; format: string; iban: string | null; read: number; imported: number; alreadyExisted: number; invalid: number; invalidRows: { lineNumber: number; reason: string }[]; from: string | null; to: string | null; openingBalance: number | null; closingBalance: number | null; warnings: string[] }
export type BankBookEntry = { reference: string; entryDate: string; documentReference: string; description: string; amount: number; status: string }
export type BankStatementLineRow = { id: string; bookingDate: string; valueDate: string | null; description: string; reference: string | null; amount: number; balance: number | null; reconciliationId: string | null; reconciledEntries: BankBookEntry[]; reconciledLineCount: number; reconciledAutomatically: boolean; reconciled: boolean }
export type BankReconciliationReport = {
  accountCode: string
  accountName: string
  date: string
  bookBalance: number
  statementBalance: number | null
  statementBalanceDate: string | null
  statementOnly: BankStatementLineRow[]
  bookOnly: BankBookEntry[]
  reconciledCount: number
  statementOnlyTotal: number
  bookOnlyTotal: number
  expectedStatementBalance: number
  difference: number | null
}
export type BankEntryRuleRow = { pattern: string; entryTemplateCode: string; entryTemplateName: string; isActive: boolean }
export type BankEntrySuggestion = { lineId: string; bookingDate: string; description: string; amount: number; pattern: string; entryTemplateCode: string; journalCode: string | null; documentTypeCode: string | null; entryDescription: string; lines: SuggestedEntryLine[]; problem: string | null; ready: boolean }
export type DocumentAttachmentRow = { id: string; fileName: string; contentType: string; sizeBytes: number; sha256: string; uploadedAtUtc: string; notes: string | null; entryReference: string | null; qrIssuerTaxId: string | null; qrDocumentNo: string | null; qrDocumentDate: string | null; qrGrossTotal: number | null; qrTaxTotal: number | null; canDelete: boolean }
export type InvoiceQrCode = { issuerTaxId: string; buyerTaxId: string | null; documentType: string | null; documentDate: string | null; documentNo: string; atcud: string | null; taxTotal: number; grossTotal: number; netTotal: number; warnings: string[] }
export type InvoiceQrLookup = { qr: InvoiceQrCode; isSale: boolean; eFaturaPurchaseId: string | null; eFaturaPurchaseEntryReference: string | null; salesDocumentId: string | null; entryReferences: string[]; attachmentIds: string[] }
export type ClassificationChoice = { code: string; name: string; count: number }
export type SupplierClassificationProposal = { supplierTaxId: string; supplierName: string; entryCount: number; account: ClassificationChoice; vatCode: ClassificationChoice | null; vatAccount: ClassificationChoice | null; supplierAccount: ClassificationChoice | null; journalCode: string; confidence: number; explanation: string; source: 'historico' | 'ia' }
export type OwnerReportFigure = { label: string; value: number; comparative: number | null; note: string | null }
export type OwnerReport = {
  year: number
  month: number
  monthLabel: string
  monthFigures: OwnerReportFigure[]
  yearFigures: OwnerReportFigure[]
  position: OwnerReportFigure[]
  highlights: string[]
  alerts: { severity: 'aviso' | 'info'; text: string }[]
  evolution: MonthlyEvolution
  receivables: PartyAging
  payables: PartyAging
  budget: BudgetReport | null
}
export type TreasuryForecastItem = { partyName: string; partyTaxNumber: string; documentDate: string; expectedDate: string; amount: number }
export type TreasuryForecast = {
  date: string
  openingCash: number
  collectionDays: number
  paymentDays: number
  fixedMonthly: number
  fixedFromHistory: boolean
  doubtfulAfterDays: number
  months: { year: number; month: number; opening: number; receipts: number; payments: number; fixed: number; closing: number }[]
  doubtfulReceivables: number
  doubtfulPayables: number
  topReceipts: TreasuryForecastItem[]
  topPayments: TreasuryForecastItem[]
  lowestClosing: number
}
export type ObligationRow = { code: string; name: string; periodKey: string; periodLabel: string; legalDueDate: string; dueDate: string; dueDateChanged: boolean; state: 'Pendente' | 'Entregue' | 'Dispensada'; deliveredOn: string | null; note: string | null; reason: string; source: string }
export type ObligationCalendar = { from: string; to: string; vatPeriodicity: string; vatPeriodicityConfigured: boolean; rows: ObligationRow[]; notIncluded: string }
export type FirmDashboardCompany = { companyId: string; legalName: string; taxRegistrationNumber: string; overdueObligations: number; dueSoonObligations: number; nextDueDate: string | null; nextObligation: string | null; pendingReview: number; purchasesToPost: number; unreconciledBankLines: number; entriesLast12Months: number; automaticEntriesLast12Months: number; automaticShare: number; attention: number }
export type FirmDashboard = { date: string; dueSoonDays: number; companies: FirmDashboardCompany[] }
export type SupplierEntryRuleRow = { supplierTaxId: string; supplierName: string | null; entryTemplateCode: string; entryTemplateName: string; description: string | null; isActive: boolean }
export type StructureItem = { label: string; amount: number; share: number }
export type ExerciseStructure = { year: number; expenses: StructureItem[]; income: StructureItem[]; topCustomers: StructureItem[]; topSuppliers: StructureItem[] }
export type MonthlyEvolution = {
  year: number
  lastMonth: number
  months: { month: number; income: number; expenses: number; result: number; previousIncome: number; previousExpenses: number; previousResult: number; cash: number; receivables: number; payables: number }[]
}
export type CashFlowLine = { section: string; label: string; amount: number; comparative: number; isTotal: boolean; accounts: { accountCode: string; accountName: string; amount: number }[] }
export type CashFlowStatement = {
  from: string
  to: string
  lines: CashFlowLine[]
  operating: number
  investing: number
  financing: number
  netChange: number
  openingCash: number
  closingCash: number
}
export type EquityChangesStatement = {
  from: string
  to: string
  model: string
  columns: string[]
  rows: { label: string; amounts: number[]; total: number; isTotal: boolean }[]
  closingEquity: number
}
export type IncomeStatementByNature = {
  from: string
  to: string
  model: string
  lines: StatementLine[]
  netResult: number
  excluded: StatementExcludedAccount[]
  source: string
}

export type AnalyticReport = {
  from: string | null
  to: string | null
  totalExpenses: number
  totalIncome: number
  totalResult: number
  centers: {
    code: string | null
    name: string
    expenses: number
    income: number
    result: number
    accounts: { accountCode: string; accountName: string; debit: number; credit: number }[]
  }[]
}

export type FiscalSource = { reference: string; status: 'Oficial' | 'Secundaria' }

export type FiscalTable = {
  year: number
  carriedForward: boolean
  irc: { generalRate: number; smeRate: number; smeThreshold: number; source: FiscalSource }
  stateSurtax: { from: number; to: number | null; rate: number }[]
  stateSurtaxSource: FiscalSource
  vat: { region: string; reduced: number; intermediate: number; normal: number; source: FiscalSource }[]
  autonomousTaxation: { key: string; description: string; rate: number }[]
  autonomousTaxationSource: FiscalSource
  disclaimer: string
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
  addressDetail?: string | null
  city?: string | null
  postalCode?: string | null
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
