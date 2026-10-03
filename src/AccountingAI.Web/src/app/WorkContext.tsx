import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { buildTaxonomyReview, demoTaxonomyCatalog, fallbackAccountingSetupsByCompany, fallbackMovementsByCompany, movements as fallbackMovements, parseSaftAccountsFromXml, parseSaftHeaderFromXml, templateTaxonomySuggestions, type AccountingSetup, type AiSettings, type AtCertificateStatus, type AtCredentials, type VatClassificationRow, type AppliedTemplate, type EntryTemplateRow, type VatCodeRow, type PartyBalances, type PartyStatement, type PartyImportResult, type PartyPage, type PartyRow, type VatSummary, type IrcAdjustment, type IrcEstimate, type IrcEstimateFilingView, type IrcEstimateFilingSummary, type Movement, type SaftAccountsImport, type SaftAccountsPreview, type SaftCompanyCreation, type SaftCompanyCreationInput, type SaftEntriesAnalysis, type SaftEntriesImport, type SaftEntriesPreview, type AnalysisCatalog, type AnalysisDefinition, type AnalysisResult, type ManagementReportCatalogEntry, type ManagementReportView, type ReportTemplate, type VatReturn, type VatReturnFilingSummary, type VatReturnFilingView, type CrossCheckReport, type DataDictionaryTable, type DataDictionaryTableSummary, type EFaturaImportResult, type EFaturaPurchasesPage, type RecordInfo, type SavedAnalysisView, type SaftInspection, type SaftOpeningBalancesImport, type SaftSalesInvoicesSummary, type SaftValidation, type SaftValidationRunSummary, type TaxonomyCatalog, type TaxonomyReview } from './demo-data'
import { EMPTY_MOVEMENTS_SUMMARY, summaryFromMovements, type MovementsSummary } from './movementsSummary'

export type NewAccountInput = { code: string; name: string; parentCode: string | null; allowsPosting: boolean }
export type AccountEditInput = { name: string; isActive: boolean; taxonomyReference: string | null; taxonomyCode: number | null }
export type NewJournalInput = { code: string; name: string }
export type JournalEditInput = { name: string; isActive: boolean }
export type NewDocumentTypeInput = { code: string; name: string; defaultJournalCode: string }
export type DocumentTypeEditInput = { name: string; defaultJournalCode: string; isActive: boolean }
export type EntryTemplateInput = { name: string; journalCode: string; documentTypeCode: string | null; description: string | null; isActive: boolean; lines: { kind: string; side: string; accountCode: string; vatCode: string | null; description: string | null }[] }
export type VatCodeInput = { code: string; name: string; rate: string; percentage: number; nature: string; region: string; isActive: boolean }
export type PartyListParams = { search: string; role: 'Todos' | 'Clientes' | 'Fornecedores'; includeInactive: boolean; page: number; pageSize: number; accountCode?: string }
export type PartyEditInput = { name: string; isCustomer: boolean; isSupplier: boolean; customerAccountCode: string | null; supplierAccountCode: string | null; isActive: boolean }
export type NewPartyInput = PartyEditInput & { countryCode: string; taxRegistrationNumber: string }
export type MovementLineInput = { accountCode: string; description: string; debit: number; credit: number; partyTaxNumber?: string | null; partyCountryCode?: string | null; vatCode?: string | null }
export type MovementInput = { journalCode: string; entryDate: string; documentReference: string; documentDate?: string | null; description: string; reviewed: boolean; lines: MovementLineInput[]; documentTypeCode?: string | null; notes?: string | null }

export async function problemMessage(response: Response) {
  try {
    const body = await response.json() as { errors?: Record<string, string[]>; detail?: string; title?: string }
    const first = body.errors ? Object.values(body.errors).flat()[0] : undefined
    return first ?? body.detail ?? body.title ?? `HTTP ${response.status}`
  } catch {
    return `HTTP ${response.status}`
  }
}

export type CompanySettings = {
  culture: string
  timeZone: string
  defaultFiscalYear: number | null
  theme: 'System' | 'Light' | 'Dark'
  compactMode: boolean
  pageSize: number
  textSize: 'Normal' | 'Large' | 'Larger'
  updatedAtUtc?: string
}

export type WorkCompany = {
  companyId: string
  legalName: string
  taxRegistrationNumber: string
  region: 'Mainland' | 'Azores' | 'Madeira'
  baseCurrency: string
  role: string
  settings: CompanySettings
}

type WorkContextData = {
  user: { id: string; displayName: string; email: string }
  companies: WorkCompany[]
}

type WorkContextValue = WorkContextData & {
  activeCompany: WorkCompany
  source: 'database' | 'demonstration'
  accountingSource: 'database' | 'demonstration'
  setupSource: 'database' | 'demonstration'
  accountingLoading: boolean
  setupLoading: boolean
  accountingSetup: AccountingSetup
  taxonomyReview: TaxonomyReview
  taxonomyCatalog: TaxonomyCatalog
  movements: Movement[]
  movementsSummary: MovementsSummary
  refreshAccounting: () => void
  refreshWorkContext: (selectCompanyId?: string) => Promise<void>
  selectCompany: (companyId: string) => void
  saveSettings: (settings: CompanySettings) => Promise<void>
  createAccount: (input: NewAccountInput) => Promise<void>
  updateAccount: (code: string, input: AccountEditInput) => Promise<void>
  createJournal: (input: NewJournalInput) => Promise<void>
  updateJournal: (code: string, input: JournalEditInput) => Promise<void>
  createDocumentType: (input: NewDocumentTypeInput) => Promise<void>
  updateDocumentType: (code: string, input: DocumentTypeEditInput) => Promise<void>
  createMovement: (input: MovementInput) => Promise<void>
  reviseMovement: (reference: string, input: MovementInput) => Promise<void>
  reviewMovement: (reference: string) => Promise<void>
  voidMovement: (reference: string) => Promise<void>
  reopenMovement: (reference: string) => Promise<void>
  applyTaxonomySuggestions: () => Promise<number>
  inspectSaft: (file: File) => Promise<SaftInspection>
  validateSaft: (file: File, companyId?: string | null) => Promise<SaftValidation>
  saftValidationReports: (companyId: string) => Promise<SaftValidationRunSummary[]>
  importEFaturaCsv: (companyId: string, file: File, dryRun: boolean) => Promise<EFaturaImportResult>
  eFaturaPurchases: (companyId: string, params: { from?: string; to?: string; page: number; pageSize: number }) => Promise<EFaturaPurchasesPage>
  crossCheck: (companyId: string, params: { from: string; to: string }) => Promise<CrossCheckReport>
  saftSalesInvoicesSummary: (companyId: string, file: File) => Promise<SaftSalesInvoicesSummary>
  recordInfo: (companyId: string, table: string, id: string) => Promise<RecordInfo>
  dataDictionary: () => Promise<DataDictionaryTableSummary[]>
  dataDictionaryTable: (schema: string, name: string) => Promise<DataDictionaryTable>
  analysisCatalog: () => Promise<AnalysisCatalog>
  savedAnalyses: (companyId: string) => Promise<SavedAnalysisView[]>
  saveAnalysis: (companyId: string, input: { name: string; description?: string; definition: AnalysisDefinition }) => Promise<SavedAnalysisView>
  updateAnalysis: (companyId: string, id: string, input: { name: string; description?: string; definition: AnalysisDefinition }) => Promise<SavedAnalysisView>
  deleteAnalysis: (companyId: string, id: string) => Promise<void>
  runAnalysis: (companyId: string, target: { definition: AnalysisDefinition } | { savedId: string }, period: { from?: string; to?: string }) => Promise<AnalysisResult>
  reportTemplate: (companyId: string, reportKey: string) => Promise<ReportTemplate>
  saveReportTemplate: (companyId: string, reportKey: string, input: { headerLines: string[]; showFirmName: boolean; showCompanyIdentity: boolean; orientation: 'Portrait' | 'Landscape' }) => Promise<ReportTemplate>
  managementReportCatalog: () => Promise<ManagementReportCatalogEntry[]>
  aiSettings: (companyId: string) => Promise<AiSettings>
  saveAiSettings: (companyId: string, input: { enabled: boolean; autoMonthly: boolean }) => Promise<AiSettings>
  generateManagementReport: (companyId: string, target: { kind?: string; question?: string }, period: { from: string; to: string }) => Promise<ManagementReportView>
  managementReports: (companyId: string) => Promise<ManagementReportView[]>
  managementReport: (companyId: string, id: string) => Promise<ManagementReportView>
  vatReturn: (companyId: string, params: { from: string; to: string }) => Promise<VatReturn>
  savedVatReturn: (companyId: string, params: { from: string; to: string }) => Promise<VatReturnFilingView>
  saveVatReturn: (companyId: string, params: { from: string; to: string }, priorPeriodCredit: number) => Promise<VatReturnFilingView>
  vatReturnHistory: (companyId: string) => Promise<VatReturnFilingSummary[]>
  atCertificateStatus: () => Promise<AtCertificateStatus>
  atCredentials: (companyId: string) => Promise<AtCredentials>
  vatClassification: (companyId: string) => Promise<VatClassificationRow[]>
  setVatClassification: (companyId: string, accountCode: string, input: { rate: string; nature: string; region: string }) => Promise<void>
  bulkVatClassification: (companyId: string, input: { prefix: string; rate: string; nature: string; region: string }) => Promise<number>
  entryTemplates: (companyId: string, includeInactive: boolean) => Promise<EntryTemplateRow[]>
  saveEntryTemplate: (companyId: string, code: string, input: EntryTemplateInput) => Promise<EntryTemplateRow>
  applyEntryTemplate: (companyId: string, code: string, amount: number, amountIncludesVat: boolean) => Promise<AppliedTemplate>
  vatCodes: (companyId: string, includeInactive: boolean) => Promise<VatCodeRow[]>
  saveVatCode: (companyId: string, input: VatCodeInput, isNew: boolean) => Promise<VatCodeRow>
  createDefaultVatCodes: (companyId: string) => Promise<number>
  parties: (companyId: string, params: PartyListParams) => Promise<PartyPage>
  partyBalances: (companyId: string, params: { to: string; search: string; page: number; pageSize: number }) => Promise<PartyBalances>
  partyStatement: (companyId: string, partyId: string, params: { from: string; to: string; page: number; pageSize: number }) => Promise<PartyStatement>
  createParty: (companyId: string, input: NewPartyInput) => Promise<PartyRow>
  updateParty: (companyId: string, partyId: string, input: PartyEditInput) => Promise<PartyRow>
  importPartiesFromSaft: (companyId: string, file: File, apply: boolean) => Promise<PartyImportResult>
  importPartiesFromEFatura: (companyId: string, apply: boolean) => Promise<PartyImportResult>
  vatSummary: (companyId: string, params: { from: string; to: string }) => Promise<VatSummary>
  ircEstimate: (companyId: string, params: { from: string; to: string }, input: { taxRate: number; adjustments: IrcAdjustment[] }) => Promise<IrcEstimate>
  savedIrcEstimate: (companyId: string, params: { from: string; to: string }) => Promise<IrcEstimateFilingView>
  saveIrcEstimate: (companyId: string, params: { from: string; to: string }, input: { taxRate: number; adjustments: IrcAdjustment[] }) => Promise<IrcEstimateFilingView>
  ircEstimateHistory: (companyId: string) => Promise<IrcEstimateFilingSummary[]>
  saveAtCredentials: (companyId: string, input: { subUser: string; password: string; periodicity: string; environment: string }) => Promise<AtCredentials>
  deleteAtCredentials: (companyId: string) => Promise<void>
  previewSaftAccounts: (companyId: string, file: File) => Promise<SaftAccountsPreview>
  importSaftAccounts: (companyId: string, file: File, fiscalYear?: number) => Promise<SaftAccountsImport>
  importSaftOpeningBalances: (companyId: string, fiscalYear: number, file: File) => Promise<SaftOpeningBalancesImport>
  previewSaftEntries: (companyId: string, file: File) => Promise<SaftEntriesPreview>
  analyzeSaftEntries: (companyId: string, file: File) => Promise<SaftEntriesAnalysis>
  importSaftEntries: (companyId: string, file: File, fiscalYear?: number) => Promise<SaftEntriesImport>
  createSaftCompany: (input: SaftCompanyCreationInput) => Promise<SaftCompanyCreation>
}

export const demoUserId = '33333333-3333-3333-3333-333333333333'

const fallbackContext: WorkContextData = {
  user: { id: demoUserId, displayName: 'António Ferreira (Demonstração)', email: 'antonio.demo@accountingai.local' },
  companies: [
    {
      companyId: '22222222-2222-2222-2222-222222222222',
      legalName: 'Almeida & Costa, Lda. (Demonstração)',
      taxRegistrationNumber: 'DEMO-PT-0001',
      region: 'Mainland',
      baseCurrency: 'EUR',
      role: 'Owner',
      settings: { culture: 'pt-PT', timeZone: 'Europe/Lisbon', defaultFiscalYear: 2026, theme: 'Light', compactMode: false, pageSize: 50, textSize: 'Normal' },
    },
    {
      companyId: '44444444-4444-4444-4444-444444444444',
      legalName: 'Ilha Azul Serviços, Lda. (Demonstração)',
      taxRegistrationNumber: 'DEMO-PT-0002',
      region: 'Azores',
      baseCurrency: 'EUR',
      role: 'Accountant',
      settings: { culture: 'pt-PT', timeZone: 'Atlantic/Azores', defaultFiscalYear: 2026, theme: 'System', compactMode: true, pageSize: 25, textSize: 'Large' },
    },
  ],
}

const Context = createContext<WorkContextValue | null>(null)

function storedCompanyId() {
  try { return window.localStorage.getItem('accounting-ai.active-company') }
  catch { return null }
}

export function WorkContextProvider({ children, userId = demoUserId }: { children: ReactNode; userId?: string }) {
  const [data, setData] = useState(fallbackContext)
  const [source, setSource] = useState<'database' | 'demonstration'>('demonstration')
  const [accountingSource, setAccountingSource] = useState<'database' | 'demonstration'>('demonstration')
  const [setupSource, setSetupSource] = useState<'database' | 'demonstration'>('demonstration')
  const [accountingLoading, setAccountingLoading] = useState(false)
  const [setupLoading, setSetupLoading] = useState(false)
  const [accountingMovements, setAccountingMovements] = useState<Movement[]>(fallbackMovements)
  const [movementsSummary, setMovementsSummary] = useState<MovementsSummary>(EMPTY_MOVEMENTS_SUMMARY)
  const [accountingSetup, setAccountingSetup] = useState<AccountingSetup>(fallbackAccountingSetupsByCompany[fallbackContext.companies[0].companyId])
  const [taxonomyReview, setTaxonomyReview] = useState<TaxonomyReview>(() => buildTaxonomyReview(fallbackAccountingSetupsByCompany[fallbackContext.companies[0].companyId]))
  const [taxonomyCatalog, setTaxonomyCatalog] = useState<TaxonomyCatalog>(demoTaxonomyCatalog)
  const [accountingRevision, setAccountingRevision] = useState(0)
  const [activeCompanyId, setActiveCompanyId] = useState(storedCompanyId() ?? fallbackContext.companies[0].companyId)

  const loadWorkContext = useCallback((signal?: AbortSignal, selectCompanyId?: string) => {
    if (import.meta.env.MODE === 'test') return Promise.resolve()
    return fetch(`/api/v1/development/work-context/${userId}`, { signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as WorkContextData
      })
      .then((context) => {
        if (context.companies.length === 0) return
        // Uma empresa sem preferências gravadas vem sem `settings`: garante o objeto.
        context.companies = context.companies.map((company) => ({
          ...company,
          settings: company.settings ?? { culture: 'pt-PT', timeZone: 'Europe/Lisbon', defaultFiscalYear: null, theme: 'System', compactMode: false, pageSize: 50, textSize: 'Normal' },
        }))
        setData(context)
        setSource('database')
        setActiveCompanyId((current) => {
          // Depois de criar uma empresa a partir do SAF-T, seleciona-se logo a
          // nova — o contexto acabou de a trazer, por isso já é uma escolha válida.
          if (selectCompanyId && context.companies.some((company) => company.companyId === selectCompanyId)) {
            try { window.localStorage.setItem('accounting-ai.active-company', selectCompanyId) } catch { /* storage is optional */ }
            return selectCompanyId
          }
          return context.companies.some((company) => company.companyId === current) ? current : context.companies[0].companyId
        })
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setSource('demonstration')
      })
  }, [userId])

  useEffect(() => {
    const controller = new AbortController()
    void loadWorkContext(controller.signal)
    return () => controller.abort()
  }, [loadWorkContext])

  const activeCompany = data.companies.find((company) => company.companyId === activeCompanyId) ?? data.companies[0]

  useEffect(() => {
    const fallback = fallbackMovementsByCompany[activeCompany.companyId] ?? []
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setAccountingMovements(fallback)
      setAccountingSource('demonstration')
      return
    }

    // Os ecrãs em modo de base de dados leem os movimentos dos endpoints
    // dedicados (lista paginada, detalhe por referência, resumo, contagens).
    // O contexto só guarda uma amostra dos mais recentes, como recurso dos
    // relatórios se o endpoint agregado falhar — nunca as dezenas de milhar
    // de linhas de uma empresa de SAF-T.
    const controller = new AbortController()
    setAccountingLoading(true)
    void fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements?page=1&pageSize=500`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as { items: Movement[] }
      })
      .then((body) => {
        setAccountingMovements(body.items)
        setAccountingSource('database')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setAccountingMovements(fallback)
        setAccountingSource('demonstration')
      })
      .finally(() => {
        if (!controller.signal.aborted) setAccountingLoading(false)
      })

    return () => controller.abort()
  }, [activeCompany.companyId, data.user.id, source, accountingRevision])

  // O resumo da fila de trabalho (contagens, posição de IVA, antevisão) é
  // carregado aqui uma vez e partilhado pelo sino, pelas Tarefas e pelo
  // painel — antes cada um fazia o seu pedido igual. Em demonstração
  // calcula-se sobre o array; ligado à base de dados vem do endpoint.
  useEffect(() => {
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setMovementsSummary(summaryFromMovements(accountingMovements))
    }
  }, [source, accountingMovements])

  useEffect(() => {
    if (import.meta.env.MODE === 'test' || source !== 'database') return

    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements/summary`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as MovementsSummary
      })
      .then((body) => setMovementsSummary(body))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setMovementsSummary(EMPTY_MOVEMENTS_SUMMARY)
      })

    return () => controller.abort()
  }, [activeCompany.companyId, data.user.id, source, accountingRevision])

  useEffect(() => {
    const fallback = fallbackAccountingSetupsByCompany[activeCompany.companyId] ?? fallbackAccountingSetupsByCompany[fallbackContext.companies[0].companyId]
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setAccountingSetup(fallback)
      setSetupSource('demonstration')
      return
    }

    const controller = new AbortController()
    setSetupLoading(true)
    void fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/setup`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as AccountingSetup
      })
      .then((setup) => {
        setAccountingSetup(setup)
        setSetupSource('database')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setAccountingSetup(fallback)
        setSetupSource('demonstration')
      })
      .finally(() => {
        if (!controller.signal.aborted) setSetupLoading(false)
      })

    return () => controller.abort()
  }, [activeCompany.companyId, data.user.id, source, accountingRevision])

  useEffect(() => {
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setTaxonomyReview(buildTaxonomyReview(accountingSetup))
      return
    }

    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/taxonomy-review`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as TaxonomyReview
      })
      .then((review) => setTaxonomyReview(review))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setTaxonomyReview(buildTaxonomyReview(accountingSetup))
      })

    return () => controller.abort()
  }, [accountingSetup, activeCompany.companyId, data.user.id, source])

  const taxonomyReference = accountingSetup.template.taxonomyReference
  useEffect(() => {
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setTaxonomyCatalog(demoTaxonomyCatalog)
      return
    }

    const controller = new AbortController()
    void fetch(`/api/v1/development/accounting/taxonomies/${encodeURIComponent(taxonomyReference)}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as TaxonomyCatalog
      })
      .then((catalog) => setTaxonomyCatalog(catalog))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setTaxonomyCatalog(demoTaxonomyCatalog)
      })

    return () => controller.abort()
  }, [taxonomyReference, source])

  useEffect(() => {
    document.documentElement.dataset.theme = activeCompany.settings.theme.toLowerCase()
    document.documentElement.dataset.compact = String(activeCompany.settings.compactMode)
    document.documentElement.dataset.textSize = activeCompany.settings.textSize.toLowerCase()
  }, [activeCompany.settings.compactMode, activeCompany.settings.textSize, activeCompany.settings.theme])

  const value = useMemo<WorkContextValue>(() => ({
    ...data,
    activeCompany,
    source,
    accountingSource,
    setupSource,
    accountingLoading,
    setupLoading,
    accountingSetup,
    taxonomyReview,
    taxonomyCatalog,
    movements: accountingMovements,
    movementsSummary,
    refreshAccounting: () => setAccountingRevision((revision) => revision + 1),
    refreshWorkContext: (selectCompanyId?: string) => loadWorkContext(undefined, selectCompanyId) ?? Promise.resolve(),
    selectCompany: (companyId) => {
      if (!data.companies.some((company) => company.companyId === companyId)) return
      setActiveCompanyId(companyId)
      try { window.localStorage.setItem('accounting-ai.active-company', companyId) } catch { /* storage is optional */ }
    },
    saveSettings: async (settings) => {
      if (source === 'database') {
        const response = await fetch(`/api/v1/development/work-context/${data.user.id}/companies/${activeCompany.companyId}/settings`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(settings),
        })
        if (!response.ok) throw new Error('Não foi possível guardar as preferências na base de dados.')
        settings = await response.json() as CompanySettings
      }

      setData((current) => ({
        ...current,
        companies: current.companies.map((company) => company.companyId === activeCompany.companyId ? { ...company, settings } : company),
      }))
    },
    createAccount: async (input) => {
      const code = input.code.trim().toUpperCase()
      const name = input.name.trim()
      if (!code || !name) throw new Error('Indique o código e a descrição da conta.')
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/accounts`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...input, code, name }),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      if (accountingSetup.accounts.some((account) => account.code === code)) throw new Error('Já existe uma conta com esse código na empresa.')
      const parent = input.parentCode ? accountingSetup.accounts.find((account) => account.code === input.parentCode) : null
      if (input.parentCode && (!parent || parent.allowsPosting)) throw new Error('A conta-mãe tem de ser uma conta agregadora existente.')
      setAccountingSetup((current) => ({
        ...current,
        accounts: [...current.accounts, { code, name, parentCode: input.parentCode, allowsPosting: input.allowsPosting, taxonomyReference: null, taxonomyCode: null, isActive: true, sourceTemplateVersion: null }],
      }))
    },
    updateAccount: async (code, input) => {
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/accounts/${encodeURIComponent(code)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      setAccountingSetup((current) => ({
        ...current,
        accounts: current.accounts.map((account) => account.code === code
          ? { ...account, name: input.name.trim(), isActive: input.isActive, taxonomyReference: input.taxonomyReference, taxonomyCode: input.taxonomyCode }
          : account),
      }))
    },
    createJournal: async (input) => {
      const code = input.code.trim().toUpperCase()
      const name = input.name.trim()
      if (!code || !name) throw new Error('Indique o código e a descrição do diário.')
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/journals`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code, name }),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      if (accountingSetup.journals.some((journal) => journal.code === code)) throw new Error('Já existe um diário com esse código na empresa.')
      setAccountingSetup((current) => ({ ...current, journals: [...current.journals, { code, name, isActive: true, sourceTemplateVersion: null }] }))
    },
    updateJournal: async (code, input) => {
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/journals/${encodeURIComponent(code)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      setAccountingSetup((current) => ({
        ...current,
        journals: current.journals.map((journal) => journal.code === code ? { ...journal, name: input.name.trim(), isActive: input.isActive } : journal),
      }))
    },
    createDocumentType: async (input) => {
      const code = input.code.trim().toUpperCase()
      const name = input.name.trim()
      const defaultJournalCode = input.defaultJournalCode.trim().toUpperCase()
      if (!code || !name) throw new Error('Indique o código e a descrição do documento.')
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/documents`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code, name, defaultJournalCode }),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      if (accountingSetup.documentTypes.some((document) => document.code === code)) throw new Error('Já existe um tipo de documento com esse código na empresa.')
      const journal = accountingSetup.journals.find((item) => item.code === defaultJournalCode)
      if (!journal || !journal.isActive) throw new Error('O diário predefinido indicado não existe na empresa ou está inativo.')
      setAccountingSetup((current) => ({ ...current, documentTypes: [...current.documentTypes, { code, name, defaultJournalCode, isActive: true, sourceTemplateVersion: null }] }))
    },
    updateDocumentType: async (code, input) => {
      const defaultJournalCode = input.defaultJournalCode.trim().toUpperCase()
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/documents/${encodeURIComponent(code)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...input, defaultJournalCode }),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      setAccountingSetup((current) => ({
        ...current,
        documentTypes: current.documentTypes.map((document) => document.code === code
          ? { ...document, name: input.name.trim(), defaultJournalCode, isActive: input.isActive }
          : document),
      }))
    },
    createMovement: async (input) => {
      const totalDebit = input.lines.reduce((sum, line) => sum + line.debit, 0)
      const totalCredit = input.lines.reduce((sum, line) => sum + line.credit, 0)
      if (input.lines.length < 2) throw new Error('Um lançamento precisa de pelo menos duas linhas.')
      if (input.lines.some((line) => (line.debit > 0) === (line.credit > 0))) throw new Error('Cada linha tem de ter um débito ou um crédito.')
      if (Math.round((totalDebit - totalCredit) * 100) !== 0) throw new Error('O total de débito tem de ser igual ao total de crédito.')
      if (!input.journalCode) throw new Error('Escolha um diário.')

      if (accountingSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }

      const documentType = input.documentTypeCode ? accountingSetup.documentTypes.find((item) => item.code === input.documentTypeCode) : undefined
      const journalCode = documentType?.defaultJournalCode ?? input.journalCode
      const journal = accountingSetup.journals.find((item) => item.code === journalCode)
      const year = input.entryDate.slice(0, 4)
      const prefix = input.documentTypeCode || journalCode
      const sequence = String(accountingMovements.filter((item) => item.id.startsWith(`${prefix}-${year}-`)).length + 1).padStart(5, '0')
      const accountName = (code: string) => accountingSetup.accounts.find((item) => item.code === code)?.name ?? code
      setAccountingMovements((current) => [{
        id: `${prefix}-${year}-${sequence}`,
        date: input.entryDate,
        journalId: journalCode,
        journalName: journal?.name ?? journalCode,
        documentType: documentType?.code ?? null,
        documentTypeName: documentType?.name ?? null,
        document: input.documentReference,
        documentDate: input.documentDate ?? null,
        description: input.description,
        notes: input.notes?.trim() || null,
        status: input.reviewed ? 'Conferido' : 'Por conferir',
        amount: totalDebit,
        lines: input.lines.map((line) => ({ account: line.accountCode, accountName: accountName(line.accountCode), description: line.description, debit: line.debit, credit: line.credit })),
      }, ...current])
    },
    reviseMovement: async (reference, input) => {
      const totalDebit = input.lines.reduce((sum, line) => sum + line.debit, 0)
      const totalCredit = input.lines.reduce((sum, line) => sum + line.credit, 0)
      if (input.lines.length < 2 || Math.round((totalDebit - totalCredit) * 100) !== 0) throw new Error('O lançamento tem de ter pelo menos duas linhas equilibradas.')
      if (accountingSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(reference)}`, {
          method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }
      const accountName = (code: string) => accountingSetup.accounts.find((item) => item.code === code)?.name ?? code
      const journalName = accountingSetup.journals.find((item) => item.code === input.journalCode)?.name ?? input.journalCode
      setAccountingMovements((current) => current.map((movement) => movement.id === reference ? {
        ...movement,
        date: input.entryDate,
        journalId: input.journalCode,
        journalName,
        document: input.documentReference,
        documentDate: input.documentDate ?? null,
        description: input.description,
        notes: input.notes?.trim() || null,
        status: input.reviewed ? 'Conferido' : 'Por conferir',
        amount: totalDebit,
        lines: input.lines.map((line) => ({ account: line.accountCode, accountName: accountName(line.accountCode), description: line.description, debit: line.debit, credit: line.credit })),
      } : movement))
    },
    reviewMovement: async (reference) => {
      if (accountingSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(reference)}/review`, { method: 'POST' })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }
      setAccountingMovements((current) => current.map((movement) => movement.id === reference ? { ...movement, status: 'Conferido' } : movement))
    },
    voidMovement: async (reference) => {
      if (accountingSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(reference)}/void`, { method: 'POST' })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }
      setAccountingMovements((current) => current.map((movement) => movement.id === reference ? { ...movement, status: 'Anulado' } : movement))
    },
    reopenMovement: async (reference) => {
      if (accountingSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(reference)}/reopen`, { method: 'POST' })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return
      }
      setAccountingMovements((current) => current.map((movement) => movement.id === reference ? { ...movement, status: 'Por conferir' } : movement))
    },
    applyTaxonomySuggestions: async () => {
      if (setupSource === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/accounts/apply-taxonomy-suggestions`, { method: 'POST' })
        if (!response.ok) throw new Error(await problemMessage(response))
        setAccountingRevision((revision) => revision + 1)
        return (await response.json() as { applied: number }).applied
      }

      let applied = 0
      setAccountingSetup((current) => ({
        ...current,
        accounts: current.accounts.map((account) => {
          if (account.allowsPosting && account.isActive && account.taxonomyCode === null && account.code in templateTaxonomySuggestions) {
            applied += 1
            return { ...account, taxonomyReference: current.template.taxonomyReference, taxonomyCode: templateTaxonomySuggestions[account.code] }
          }
          return account
        }),
      }))
      return applied
    },
    inspectSaft: async (file) => {
      if (source === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/saft/inspect`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/xml' },
          body: file,
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        return await response.json() as SaftInspection
      }

      const header = parseSaftHeaderFromXml(await file.text())
      const matched = data.companies.find((company) => company.taxRegistrationNumber.toUpperCase() === header.taxRegistrationNumber)
      return {
        header,
        match: matched
          ? { outcome: 'ExistingCompany', companyId: matched.companyId, legalName: matched.legalName, accountingStandard: null, accountingSetupVersion: null }
          : { outcome: 'NewCompanyProposed', companyId: null, legalName: null, accountingStandard: null, accountingSetupVersion: null },
      }
    },
    validateSaft: async (file, companyId) => {
      const params = new URLSearchParams({ fileName: file.name })
      if (companyId) params.set('companyId', companyId)
      const response = await fetch(`/api/v1/development/users/${data.user.id}/saft/validate?${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftValidation
    },
    saftValidationReports: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/validation-reports`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftValidationRunSummary[]
    },
    importEFaturaCsv: async (companyId, file, dryRun) => {
      const params = new URLSearchParams({ fileName: file.name })
      if (dryRun) params.set('dryRun', 'true')
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/efatura/purchases-import?${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'text/csv' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as EFaturaImportResult
    },
    eFaturaPurchases: async (companyId, { from, to, page, pageSize }) => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
      if (from) params.set('from', from)
      if (to) params.set('to', to)
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/efatura/purchases?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as EFaturaPurchasesPage
    },
    crossCheck: async (companyId, { from, to }) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/cross-check?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as CrossCheckReport
    },
    saftSalesInvoicesSummary: async (companyId, file) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/sales-invoices-summary`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftSalesInvoicesSummary
    },
    recordInfo: async (companyId, table, id) => {
      const params = new URLSearchParams({ table, id })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/record-info?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as RecordInfo
    },
    dataDictionary: async () => {
      const response = await fetch('/api/v1/development/data-dictionary')
      if (!response.ok) throw new Error(await problemMessage(response))
      return (await response.json() as { tables: DataDictionaryTableSummary[] }).tables
    },
    dataDictionaryTable: async (schema, name) => {
      const response = await fetch(`/api/v1/development/data-dictionary/${encodeURIComponent(schema)}/${encodeURIComponent(name)}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as DataDictionaryTable
    },
    analysisCatalog: async () => {
      const response = await fetch('/api/v1/development/analyses/catalog')
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AnalysisCatalog
    },
    savedAnalyses: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SavedAnalysisView[]
    },
    saveAnalysis: async (companyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SavedAnalysisView
    },
    updateAnalysis: async (companyId, id, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SavedAnalysisView
    },
    deleteAnalysis: async (companyId, id) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses/${id}`, { method: 'DELETE' })
      if (!response.ok && response.status !== 204) throw new Error(await problemMessage(response))
    },
    runAnalysis: async (companyId, target, period) => {
      const params = new URLSearchParams()
      if (period.from) params.set('from', period.from)
      if (period.to) params.set('to', period.to)
      const query = params.toString() ? `?${params}` : ''
      const response = 'savedId' in target
        ? await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses/${target.savedId}/run${query}`)
        : await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/analyses/run${query}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ definition: target.definition }),
          })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AnalysisResult
    },
    reportTemplate: async (companyId, reportKey) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/report-templates/${encodeURIComponent(reportKey)}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as ReportTemplate
    },
    saveReportTemplate: async (companyId, reportKey, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/report-templates/${encodeURIComponent(reportKey)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as ReportTemplate
    },
    managementReportCatalog: async () => {
      const response = await fetch('/api/v1/development/management-reports/catalog')
      if (!response.ok) throw new Error(await problemMessage(response))
      const body = await response.json() as { kinds: ManagementReportCatalogEntry[] }
      return body.kinds
    },
    aiSettings: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/ai-settings`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AiSettings
    },
    saveAiSettings: async (companyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/ai-settings`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AiSettings
    },
    generateManagementReport: async (companyId, target, period) => {
      const params = new URLSearchParams({ from: period.from, to: period.to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/management-reports/generate?${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ kind: target.kind ?? null, question: target.question ?? null }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as ManagementReportView
    },
    managementReports: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/management-reports`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as ManagementReportView[]
    },
    managementReport: async (companyId, id) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/management-reports/${id}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as ManagementReportView
    },
    vatReturn: async (companyId, { from, to }) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-return?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatReturn
    },
    savedVatReturn: async (companyId, { from, to }) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-return/saved?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatReturnFilingView
    },
    saveVatReturn: async (companyId, { from, to }, priorPeriodCredit) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-return/saved?${params}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ priorPeriodCredit }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatReturnFilingView
    },
    vatReturnHistory: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-return/history`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatReturnFilingSummary[]
    },
    atCertificateStatus: async () => {
      const response = await fetch('/api/v1/development/at/certificate')
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AtCertificateStatus
    },
    atCredentials: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/at-credentials`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AtCredentials
    },
    saveAtCredentials: async (companyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/at-credentials`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AtCredentials
    },
    deleteAtCredentials: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/at-credentials`, { method: 'DELETE' })
      if (!response.ok && response.status !== 204) throw new Error(await problemMessage(response))
    },
    vatClassification: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-classification`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatClassificationRow[]
    },
    setVatClassification: async (companyId, accountCode, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-classification/${encodeURIComponent(accountCode)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok && response.status !== 204) throw new Error(await problemMessage(response))
    },
    bulkVatClassification: async (companyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-classification/bulk`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      const body = await response.json() as { updated: number }
      return body.updated
    },
    entryTemplates: async (companyId, includeInactive) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/entry-templates?includeInactive=${includeInactive}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as EntryTemplateRow[]
    },
    saveEntryTemplate: async (companyId, code, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/entry-templates/${encodeURIComponent(code.trim())}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as EntryTemplateRow
    },
    applyEntryTemplate: async (companyId, code, amount, amountIncludesVat) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/entry-templates/${encodeURIComponent(code)}/apply`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ amount, amountIncludesVat }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as AppliedTemplate
    },
    vatCodes: async (companyId, includeInactive) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-codes?includeInactive=${includeInactive}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatCodeRow[]
    },
    saveVatCode: async (companyId, input, isNew) => {
      const base = `/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-codes`
      const response = await fetch(isNew ? base : `${base}/${encodeURIComponent(input.code)}`, {
        method: isNew ? 'POST' : 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatCodeRow
    },
    createDefaultVatCodes: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-codes/defaults`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      const body = await response.json() as { created: number }
      return body.created
    },
    parties: async (companyId, { search, role, includeInactive, page, pageSize, accountCode }) => {
      const params = new URLSearchParams({ role, includeInactive: String(includeInactive), page: String(page), pageSize: String(pageSize) })
      if (search) params.set('search', search)
      if (accountCode) params.set('accountCode', accountCode)
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/parties?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyPage
    },
    partyBalances: async (companyId, { to, search, page, pageSize }) => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
      if (to) params.set('to', to)
      if (search) params.set('search', search)
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/parties/balances?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyBalances
    },
    partyStatement: async (companyId, partyId, { from, to, page, pageSize }) => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
      if (from) params.set('from', from)
      if (to) params.set('to', to)
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/parties/${partyId}/statement?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyStatement
    },
    createParty: async (companyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/parties`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyRow
    },
    updateParty: async (companyId, partyId, input) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/parties/${partyId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyRow
    },
    importPartiesFromSaft: async (companyId, file, apply) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/${apply ? 'parties-import' : 'parties-preview'}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyImportResult
    },
    importPartiesFromEFatura: async (companyId, apply) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/efatura/${apply ? 'parties-import' : 'parties-preview'}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as PartyImportResult
    },
    vatSummary: async (companyId, { from, to }) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/vat-summary?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as VatSummary
    },
    ircEstimate: async (companyId, { from, to }, input) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/irc-estimate?${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as IrcEstimate
    },
    savedIrcEstimate: async (companyId, { from, to }) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/irc-estimate/saved?${params}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as IrcEstimateFilingView
    },
    saveIrcEstimate: async (companyId, { from, to }, input) => {
      const params = new URLSearchParams({ from, to })
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/irc-estimate/saved?${params}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as IrcEstimateFilingView
    },
    ircEstimateHistory: async (companyId) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/irc-estimate/history`)
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as IrcEstimateFilingSummary[]
    },
    previewSaftAccounts: async (companyId, file) => {
      if (source === 'database') {
        const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/accounts-preview`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/xml' },
          body: file,
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        return await response.json() as SaftAccountsPreview
      }

      const fileAccounts = parseSaftAccountsFromXml(await file.text())
      const chart = new Set(accountingSetup.accounts.map((account) => account.code.toUpperCase()))
      const accounts = fileAccounts
        .map((account) => ({
          code: account.code,
          description: account.description,
          state: (chart.has(account.code.toUpperCase()) ? 'Existing' : 'New') as 'Existing' | 'New',
        }))
        .sort((a, b) => a.code.localeCompare(b.code))
      return {
        fileAccountCount: accounts.length,
        existingCount: accounts.filter((account) => account.state === 'Existing').length,
        newCount: accounts.filter((account) => account.state === 'New').length,
        companyPostingAccountCount: accountingSetup.accounts.filter((account) => account.allowsPosting).length,
        accounts,
      }
    },
    importSaftAccounts: async (companyId, file, fiscalYear) => {
      const params = fiscalYear ? `?fiscalYear=${fiscalYear}` : ''
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/accounts-import${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setAccountingRevision((revision) => revision + 1)
      // O exercício do ficheiro pode ter passado a ser o exercício do
      // utilizador nesta empresa (ver EnsureDefaultFiscalYearAsync na API);
      // recarrega o contexto para os filtros de período o refletirem já.
      if (fiscalYear) void loadWorkContext()
      return await response.json() as SaftAccountsImport
    },
    importSaftOpeningBalances: async (companyId, fiscalYear, file) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/opening-balances-import?fiscalYear=${fiscalYear}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setAccountingRevision((revision) => revision + 1)
      void loadWorkContext()
      return await response.json() as SaftOpeningBalancesImport
    },
    previewSaftEntries: async (companyId, file) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/entries-preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftEntriesPreview
    },
    analyzeSaftEntries: async (companyId, file) => {
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/entries-analyze`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftEntriesAnalysis
    },
    importSaftEntries: async (companyId, file, fiscalYear) => {
      const params = fiscalYear ? `?fiscalYear=${fiscalYear}` : ''
      const response = await fetch(`/api/v1/development/users/${data.user.id}/companies/${companyId}/saft/entries-import${params}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/xml' },
        body: file,
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setAccountingRevision((revision) => revision + 1)
      if (fiscalYear) void loadWorkContext()
      return await response.json() as SaftEntriesImport
    },
    createSaftCompany: async (input) => {
      if (source !== 'database') {
        throw new Error('A criação de empresa a partir do SAF-T requer a ligação à API local.')
      }
      const response = await fetch(`/api/v1/development/users/${data.user.id}/saft/companies`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      return await response.json() as SaftCompanyCreation
    },
  }), [accountingLoading, accountingMovements, accountingSetup, accountingSource, activeCompany, data, loadWorkContext, movementsSummary, setupLoading, setupSource, source, taxonomyCatalog, taxonomyReview])

  return <Context.Provider value={value}>{children}</Context.Provider>
}

export function useWorkContext() {
  const value = useContext(Context)
  if (!value) throw new Error('WorkContextProvider em falta.')
  return value
}

// O resumo da fila de trabalho é carregado uma vez pelo `WorkContext`; este
// seletor mantém a assinatura antiga para os ecrãs que já o usavam.
export function useMovementsSummary() {
  return useWorkContext().movementsSummary
}
