import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

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
  selectCompany: (companyId: string) => void
  saveSettings: (settings: CompanySettings) => Promise<void>
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
    document.documentElement.dataset.theme = activeCompany.settings.theme.toLowerCase()
    document.documentElement.dataset.compact = String(activeCompany.settings.compactMode)
  }, [activeCompany.settings.compactMode, activeCompany.settings.theme])

  const value = useMemo<WorkContextValue>(() => ({
    ...data,
    activeCompany,
    source,
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
  }), [activeCompany, data, source])

  return <Context.Provider value={value}>{children}</Context.Provider>
}

export function useWorkContext() {
  const value = useContext(Context)
  if (!value) throw new Error('WorkContextProvider em falta.')
  return value
}
