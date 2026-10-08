import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ClosingPage } from './ClosingPage'

let role = 'Accountant'
vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    can: () => true,
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    companies: [{ companyId: 'c1', legalName: 'Empresa', role, settings: {} }],
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const auto = (extra: Record<string, unknown>) => ({ group: 'Lançamentos', kind: 'auto', count: 0, link: '/movimentos', note: null, checkedByName: null, checkedAtUtc: null, ...extra })
const checklist = {
  year: 2026, month: 9, periodKey: '2026-09', attention: 1, pendingManual: 1, ready: false,
  items: [
    auto({ key: 'lancamentos-por-conferir', label: 'Lançamentos por conferir', state: 'attention', count: 2, detail: '2 lançamento(s) do mês ainda por rever.' }),
    auto({ key: 'bancos-por-conciliar', group: 'Bancos', label: 'Movimentos bancários por conciliar', state: 'ok', detail: 'Nada por tratar.', link: '/bancos' }),
    auto({ key: 'iva-do-periodo', group: 'Fiscal', label: 'Declaração periódica de IVA do período', state: 'info', count: null, detail: 'Não há declaração.' }),
    { key: 'bancos-conferidos', group: 'Conferências', label: 'Conferi os saldos bancários com os extratos', kind: 'manual', state: 'pending', count: null, detail: 'O saldo bate.', link: null, note: null, checkedByName: null, checkedAtUtc: null },
    { key: 'depreciacoes', group: 'Conferências', label: 'Confirmei as depreciações do mês', kind: 'manual', state: 'done', count: null, detail: 'Lançado.', link: null, note: 'Mapa de setembro', checkedByName: 'Rui', checkedAtUtc: '2026-10-07T09:00:00Z' },
  ],
}

function serve(calls: { url: string; body?: unknown }[]) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    if (init?.method === 'PUT') return Promise.resolve(new Response(null, { status: 204 }))
    return Promise.resolve(json(checklist))
  })
}

const renderPage = () => render(<MemoryRouter><ClosingPage /></MemoryRouter>)

describe('fecho do mês', () => {
  afterEach(() => { vi.unstubAllGlobals(); role = 'Accountant' })

  it('mostra as verificações por grupo, o que falta e a ligação para tratar', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    expect(await screen.findByText('Lançamentos por conferir')).toBeInTheDocument()
    expect(screen.getByText('1 por tratar · 1 conferência(s) por fazer')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Tratar' })).toHaveAttribute('href', '/movimentos')
    expect(screen.getByText('Em ordem')).toBeInTheDocument()
    expect(screen.getByText('Não se aplica')).toBeInTheDocument()
    expect(screen.getByText(/Feito por Rui/)).toBeInTheDocument()
    expect(calls[0].url).toMatch(/year=2026&month=\d+/)
  })

  it('marca uma conferência com a nota e desmarca outra', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    await screen.findByText('Lançamentos por conferir')
    fireEvent.change(screen.getByLabelText('Nota: Conferi os saldos bancários com os extratos'), { target: { value: 'Bate' } })
    fireEvent.click(screen.getByLabelText('Conferi os saldos bancários com os extratos'))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('PUT') && call.url.includes('/closing/bancos-conferidos'))).toBe(true))
    expect(calls.find((call) => call.url.includes('/closing/bancos-conferidos'))!.body).toEqual({ checked: true, note: 'Bate' })
    fireEvent.click(screen.getByLabelText('Confirmei as depreciações do mês'))
    await waitFor(() => expect(calls.some((call) => call.url.includes('/closing/depreciacoes'))).toBe(true))
    expect(calls.find((call) => call.url.includes('/closing/depreciacoes'))!.body).toMatchObject({ checked: false })
  })

  it('só de consulta vê o papel mas não o altera', async () => {
    role = 'Viewer'
    vi.stubGlobal('fetch', serve([]))
    renderPage()
    await screen.findByText('Lançamentos por conferir')
    expect(screen.getByLabelText('Conferi os saldos bancários com os extratos')).toBeDisabled()
  })
})
