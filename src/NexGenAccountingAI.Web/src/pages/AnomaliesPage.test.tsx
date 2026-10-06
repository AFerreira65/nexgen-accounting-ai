import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AnomaliesPage } from './AnomaliesPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const finding = (extra: Record<string, unknown>) => ({
  key: 'k1', rule: 'duplicado', severity: 'high', title: '2 linhas iguais na conta 211 em 05/03/2026', detail: 'Valor de 1 500,00 €.', entryReferences: ['L-13', 'L-14'],
  accountCode: '211', partyName: 'Cliente A', amount: 1500, date: '2026-03-05', dismissed: false, dismissReason: null, ...extra,
})
const rules = [
  { rule: 'duplicado', label: 'Possíveis lançamentos duplicados', description: 'Linhas iguais.', count: 1 },
  { rule: 'sem-terceiro', label: 'Clientes e fornecedores sem terceiro', description: 'Sem terceiro.', count: 1 },
]
const report = { from: '2026-01-01', to: '2026-12-31', truncated: false, rules, findings: [finding({}), finding({ key: 'k2', rule: 'sem-terceiro', severity: 'medium', title: 'Linha de 300,00 € na conta 221 sem terceiro', entryReferences: ['L-18'], amount: 300 })] }

function serve(calls: { url: string; body?: unknown }[]) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    if (url.includes('/anomalies?')) return Promise.resolve(json(report))
    if (url.endsWith('/anomalies/dismiss') || url.endsWith('/anomalies/restore')) return Promise.resolve(new Response(null, { status: 204 }))
    return Promise.resolve(json({}, 404))
  })
}

const renderPage = () => render(<MemoryRouter><AnomaliesPage /></MemoryRouter>)

describe('anomalias', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('procura no exercício por omissão e mostra os achados por regra, com ligação ao lançamento', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Procurar anomalias/ }))
    const group = await screen.findByRole('region', { name: 'Possíveis lançamentos duplicados' })
    expect(within(group).getByText(/2 linhas iguais/)).toBeInTheDocument()
    expect(within(group).getByRole('link', { name: 'L-13' })).toHaveAttribute('href', '/movimentos/L-13')
    expect(within(group).getByText('Alta')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Clientes e fornecedores sem terceiro' })).toBeInTheDocument()
    expect(calls[0].url).toContain('from=2026-01-01')
    expect(calls[0].url).toContain('to=2026-12-31')
    expect(screen.getByText(/2 achado\(s\) por tratar/)).toBeInTheDocument()
  })

  it('dispensa um achado com a razão e procura de novo', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Procurar anomalias/ }))
    fireEvent.click(await screen.findByLabelText(/Dispensar 2 linhas iguais/))
    fireEvent.change(await screen.findByLabelText('Razão para dispensar'), { target: { value: 'Duas vendas iguais' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dispensar' }))
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/anomalies/dismiss'))).toBe(true))
    expect(calls.find((call) => call.url.endsWith('/anomalies/dismiss'))!.body).toEqual({ key: 'k1', reason: 'Duas vendas iguais' })
    await waitFor(() => expect(calls.filter((call) => call.url.includes('/anomalies?')).length).toBe(2))
  })

  it('mostra os dispensados à parte e permite repô-los', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body: init?.body ? JSON.parse(String(init.body)) : undefined })
      if (url.includes('/anomalies?')) return Promise.resolve(json({ ...report, findings: [finding({ dismissed: true, dismissReason: 'Já conferido' })] }))
      return Promise.resolve(new Response(null, { status: 204 }))
    }))
    renderPage()
    fireEvent.click(screen.getByLabelText(/Mostrar os dispensados/))
    fireEvent.click(screen.getByRole('button', { name: /Procurar anomalias/ }))
    expect(await screen.findByText(/Dispensado: Já conferido/)).toBeInTheDocument()
    expect(calls[0].url).toContain('includeDismissed=true')
    fireEvent.click(screen.getByLabelText(/Repor 2 linhas iguais/))
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/anomalies/restore'))).toBe(true))
  })

  it('diz quando não há nada a assinalar', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ ...report, findings: [], rules: rules.map((rule) => ({ ...rule, count: 0 })) }))))
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Procurar anomalias/ }))
    expect(await screen.findByText('Nada a assinalar neste período.')).toBeInTheDocument()
  })
})
