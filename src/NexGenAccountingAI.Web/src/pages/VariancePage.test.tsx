import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { VariancePage, presetPeriods } from './VariancePage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: {} },
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const report = {
  from: '2026-09-01', to: '2026-09-30', compareFrom: '2026-08-01', compareTo: '2026-08-31',
  totals: { income: 1400, previousIncome: 1000, expenses: 950, previousExpenses: 250, result: 450, previousResult: 750 },
  rows: [
    { accountCode: '6221', accountName: 'Trabalhos especializados', kind: 'expense', current: 900, previous: 200, delta: 700, percent: 350, favorable: false,
      topParties: [{ name: 'Agência X', current: 600, previous: 0, delta: 600 }, { name: 'Advogado Y', current: 300, previous: 200, delta: 100 }] },
    { accountCode: '7211', accountName: 'Serviços', kind: 'income', current: 1400, previous: 1000, delta: 400, percent: 40, favorable: true, topParties: [] },
  ],
}

function serve(calls: { url: string }[], outcome: unknown = { refusal: null, explanation: { text: 'Os trabalhos especializados subiram.', model: 'modelo' } }) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push({ url: `${init?.method ?? 'GET'} ${url}` })
    if (init?.method === 'POST') return Promise.resolve(json(outcome))
    return Promise.resolve(json(report))
  })
}

describe('variações', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('calcula as predefinições a partir de hoje', () => {
    expect(presetPeriods('month', new Date(2026, 9, 7))).toEqual(['2026-09-01', '2026-09-30', '2026-08-01', '2026-08-31'])
    expect(presetPeriods('ytd', new Date(2026, 9, 7))).toEqual(['2026-01-01', '2026-09-30', '2025-01-01', '2025-09-30'])
    expect(presetPeriods('month', new Date(2026, 0, 15))).toEqual(['2025-12-01', '2025-12-31', '2025-11-01', '2025-11-30'])
  })

  it('mostra os totais e as contas, com os terceiros a abrir', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    render(<MemoryRouter><VariancePage /></MemoryRouter>)
    const table = await screen.findByRole('table', { name: 'Variações por conta' })
    expect(within(table).getByText(/6221 · Trabalhos especializados/)).toBeInTheDocument()
    expect(within(table).getByText('350,00 %')).toBeInTheDocument()
    expect(screen.getByLabelText('Totais')).toHaveTextContent('450,00')
    expect(screen.queryByText('Agência X')).not.toBeInTheDocument()
    fireEvent.click(within(table).getByRole('button', { name: /6221/ }))
    expect(screen.getByText('Agência X')).toBeInTheDocument()
    expect(calls[0].url).toMatch(/from=\d{4}-\d{2}-\d{2}&to=.*&compareFrom=.*&compareTo=/)
  })

  it('pede a explicação e mostra o texto com o aviso das hipóteses', async () => {
    vi.stubGlobal('fetch', serve([]))
    render(<MemoryRouter><VariancePage /></MemoryRouter>)
    await screen.findByRole('table', { name: 'Variações por conta' })
    fireEvent.click(screen.getByRole('button', { name: /Explicar com IA/ }))
    const box = await screen.findByRole('region', { name: 'Explicação' })
    expect(box).toHaveTextContent('Os trabalhos especializados subiram.')
    expect(box).toHaveTextContent('hipóteses a confirmar')
  })

  it('mostra a recusa quando a IA está desligada', async () => {
    vi.stubGlobal('fetch', serve([], { refusal: 'A IA está desligada para esta empresa.', explanation: null }))
    render(<MemoryRouter><VariancePage /></MemoryRouter>)
    await screen.findByRole('table', { name: 'Variações por conta' })
    fireEvent.click(screen.getByRole('button', { name: /Explicar com IA/ }))
    expect(await screen.findByText('A IA está desligada para esta empresa.')).toBeInTheDocument()
  })

  it('com períodos à escolha pede os quatro limites', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    render(<MemoryRouter><VariancePage /></MemoryRouter>)
    await screen.findByRole('table', { name: 'Variações por conta' })
    fireEvent.change(screen.getByLabelText('Comparação'), { target: { value: 'custom' } })
    fireEvent.change(screen.getByLabelText('Período, de'), { target: { value: '2026-01-01' } })
    await waitFor(() => expect(calls.some((call) => call.url.includes('from=2026-01-01'))).toBe(true))
  })
})
