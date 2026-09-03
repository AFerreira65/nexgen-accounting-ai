import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { buildTaxonomyReview, demoTaxonomyCatalog, fallbackAccountingSetupsByCompany, fallbackMovementsByCompany, movements as fallbackMovements, parseSaftHeaderFromXml, templateTaxonomySuggestions, type AccountingSetup, type Movement, type SaftCompanyCreation, type SaftCompanyCreationInput, type SaftInspection, type TaxonomyCatalog, type TaxonomyReview } from './demo-data'

export type NewAccountInput = { code: string; name: string; parentCode: string | null; allowsPosting: boolean }
export type AccountEditInput = { name: string; isActive: boolean; taxonomyReference: string | null; taxonomyCode: number | null }
export type NewJournalInput = { code: string; name: string }
export type JournalEditInput = { name: string; isActive: boolean }
export type NewDocumentTypeInput = { code: string; name: string; defaultJournalCode: string }
export type DocumentTypeEditInput = { name: string; defaultJournalCode: string; isActive: boolean }
export type MovementLineInput = { accountCode: string; description: string; debit: number; credit: number }
export type MovementInput = { journalCode: string; entryDate: string; documentReference: string; description: string; reviewed: boolean; lines: MovementLineInput[]; documentTypeCode?: string | null; notes?: string | null }

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
  refreshAccounting: () => void
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
  applyTaxonomySuggestions: () => Promise<number>
  inspectSaft: (file: File) => Promise<SaftInspection>
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
      settings: { culture: 'pt-PT', timeZone: 'Europe/Lisbon', defaultFiscalYear: 2026, theme: 'Light', compactMode: false, pageSize: 50 },
    },
    {
      companyId: '44444444-4444-4444-4444-444444444444',
      legalName: 'Ilha Azul Serviços, Lda. (Demonstração)',
      taxRegistrationNumber: 'DEMO-PT-0002',
      region: 'Azores',
      baseCurrency: 'EUR',
      role: 'Accountant',
      settings: { culture: 'pt-PT', timeZone: 'Atlantic/Azores', defaultFiscalYear: 2026, theme: 'System', compactMode: true, pageSize: 25 },
    },
  ],
}

const Context = createContext<WorkContextValue | null>(null)

function storedCompanyId() {
  try { return window.localStorage.getItem('accounting-ai.active-company') }
  catch { return null }
}

export function WorkContextProvider({ children }: { children: ReactNode }) {
  const [data, setData] = useState(fallbackContext)
  const [source, setSource] = useState<'database' | 'demonstration'>('demonstration')
  const [accountingSource, setAccountingSource] = useState<'database' | 'demonstration'>('demonstration')
  const [setupSource, setSetupSource] = useState<'database' | 'demonstration'>('demonstration')
  const [accountingLoading, setAccountingLoading] = useState(false)
  const [setupLoading, setSetupLoading] = useState(false)
  const [accountingMovements, setAccountingMovements] = useState<Movement[]>(fallbackMovements)
  const [accountingSetup, setAccountingSetup] = useState<AccountingSetup>(fallbackAccountingSetupsByCompany[fallbackContext.companies[0].companyId])
  const [taxonomyReview, setTaxonomyReview] = useState<TaxonomyReview>(() => buildTaxonomyReview(fallbackAccountingSetupsByCompany[fallbackContext.companies[0].companyId]))
  const [taxonomyCatalog, setTaxonomyCatalog] = useState<TaxonomyCatalog>(demoTaxonomyCatalog)
  const [accountingRevision, setAccountingRevision] = useState(0)
  const [activeCompanyId, setActiveCompanyId] = useState(storedCompanyId() ?? fallbackContext.companies[0].companyId)

  useEffect(() => {
    if (import.meta.env.MODE === 'test') return

    const controller = new AbortController()
    void fetch(`/api/v1/development/work-context/${demoUserId}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as WorkContextData
      })
      .then((context) => {
        if (context.companies.length === 0) return
        setData(context)
        setSource('database')
        setActiveCompanyId((current) => context.companies.some((company) => company.companyId === current) ? current : context.companies[0].companyId)
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setSource('demonstration')
      })

    return () => controller.abort()
  }, [])

  const activeCompany = data.companies.find((company) => company.companyId === activeCompanyId) ?? data.companies[0]

  useEffect(() => {
    const fallback = fallbackMovementsByCompany[activeCompany.companyId] ?? []
    if (import.meta.env.MODE === 'test' || source !== 'database') {
      setAccountingMovements(fallback)
      setAccountingSource('demonstration')
      return
    }

    const controller = new AbortController()
    setAccountingLoading(true)
    void fetch(`/api/v1/development/users/${data.user.id}/companies/${activeCompany.companyId}/accounting/movements`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as Movement[]
      })
      .then((items) => {
        setAccountingMovements(items)
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
  }, [activeCompany.settings.compactMode, activeCompany.settings.theme])

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
    refreshAccounting: () => setAccountingRevision((revision) => revision + 1),
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
  }), [accountingLoading, accountingMovements, accountingSetup, accountingSource, activeCompany, data, setupLoading, setupSource, source, taxonomyCatalog, taxonomyReview])

  return <Context.Provider value={value}>{children}</Context.Provider>
}

export function useWorkContext() {
  const value = useContext(Context)
  if (!value) throw new Error('WorkContextProvider em falta.')
  return value
}
