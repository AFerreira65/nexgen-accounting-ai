export type MovementLine = {
  account: string
  accountName: string
  description: string
  debit: number
  credit: number
}

export type Movement = {
  id: string
  date: string
  journalId: string
  journalName: string
  document: string
  description: string
  status: 'Conferido' | 'Por conferir'
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
export const templateTaxonomySuggestions: Record<string, number> = { '11': 1, '12': 2 }

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
  { code: '11', name: 'Caixa', parentCode: '1', allowsPosting: true, taxonomyReference: 'S', taxonomyCode: 1, isActive: true, sourceTemplateVersion: templateSource },
  { code: '12', name: 'Depósitos à ordem', parentCode: '1', allowsPosting: true, taxonomyReference: 'S', taxonomyCode: 2, isActive: true, sourceTemplateVersion: templateSource },
  { code: '2111001', name: 'Maré Clara, SA', parentCode: '21', allowsPosting: true, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null },
  { code: '2211001', name: 'Norte Office, Lda.', parentCode: '22', allowsPosting: true, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null },
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

export const euro = new Intl.NumberFormat('pt-PT', { style: 'currency', currency: 'EUR' })

export function formatMovementDate(value: string) {
  return new Intl.DateTimeFormat('pt-PT', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(`${value}T00:00:00Z`))
}
