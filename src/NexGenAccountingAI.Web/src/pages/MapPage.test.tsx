import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { MapPage } from './MapPage'
import { MapsPage } from './MapsPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    can: () => true,
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    source: 'database',
    user: { id: 'u1' },
  }),
}))
vi.mock('../app/ReportChrome', () => ({ ReportChrome: () => null }))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const columns = [{ code: 'P', label: 'Período', kind: 'value', scope: 'period', yearOffset: 0, month: null, formula: null, measure: null, percent: false }]
const definition = {
  rows: [
    { code: 'L10', label: 'Vendas', kind: 'accounts', accounts: '71', formula: null, measure: 'balance', sign: 'inverted', show: 'always', level: 0, bold: false, hidden: false },
    { code: 'L20', label: 'Gastos', kind: 'accounts', accounts: '6', formula: null, measure: 'balance', sign: 'inverted', show: 'always', level: 0, bold: false, hidden: false },
    { code: 'L30', label: 'Resultado', kind: 'formula', accounts: null, formula: 'L10 + L20', measure: 'balance', sign: 'normal', show: 'always', level: 0, bold: true, hidden: false },
  ],
  columns,
}
const listing = {
  maps: [{ id: 'm1', name: 'Resultado simples', description: 'O meu', definition, createdAtUtc: '2026-10-01T10:00:00Z' }],
  templates: [{ key: 'exploracao-mensal', name: 'Mapa de exploração mensal', description: 'Mês a mês', excludeClosing: true }],
  columnSets: [{ key: 'periodo', label: 'Período', description: '', columns }],
}

function serve(calls: string[]) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push(`${init?.method ?? 'GET'} ${url}`)
    if (url.endsWith('/maps')) return Promise.resolve(init?.method === 'POST' ? json({ id: 'novo', name: 'Cópia' }) : json(listing))
    if (url.endsWith('/maps/m1')) return Promise.resolve(json({ id: 'm1', name: 'Resultado simples' }))
    if (url.includes('/maps/templates/')) return Promise.resolve(json({ name: 'Mapa de exploração mensal', description: 'Mês a mês', excludeClosing: true, definition }))
    if (url.endsWith('/maps/run')) {
      const body = JSON.parse(String(init?.body)) as { rowCode?: string }
      if (body.rowCode) return Promise.resolve(json([{ accountCode: '7111', accountName: 'Vendas de vidro', values: [1500] }]))
      return Promise.resolve(json({
        columns,
        rows: [
          { code: 'L10', label: 'Vendas', kind: 'Accounts', level: 0, bold: false, values: [1500] },
          { code: 'L20', label: 'Gastos', kind: 'Accounts', level: 0, bold: false, values: [-400.5] },
          { code: 'L30', label: 'Resultado', kind: 'Formula', level: 0, bold: true, values: [1099.5] },
        ],
        warnings: ['2 conta(s) com valores não entram em nenhuma linha: 721, 722.'],
      }))
    }
    return Promise.resolve(json({}, 404))
  })
}

function renderAt(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/mapas" element={<MapsPage />} />
    <Route path="/mapas/novo" element={<MapPage />} />
    <Route path="/mapas/modelo/:template" element={<MapPage />} />
    <Route path="/mapas/:id" element={<MapPage />} />
  </Routes></MemoryRouter>)
}

describe('mapas à medida', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lista os mapas da empresa e os modelos', async () => {
    vi.stubGlobal('fetch', serve([]))
    renderAt('/mapas')
    expect(await screen.findByRole('link', { name: 'Resultado simples' })).toHaveAttribute('href', '/mapas/m1')
    expect(screen.getByRole('link', { name: 'Mapa de exploração mensal' })).toHaveAttribute('href', '/mapas/modelo/exploracao-mensal')
  })

  it('abre um mapa guardado, calcula-o e explica uma linha', async () => {
    const calls: string[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/mapas/m1')
    expect(await screen.findByRole('heading', { name: 'Resultado simples' })).toBeInTheDocument()
    expect(await screen.findByText('1 099,50')).toBeInTheDocument()
    expect(screen.getByText('-400,50')).toHaveClass('negative')
    expect(screen.getByText(/não entram em nenhuma linha/)).toBeInTheDocument()
    const run = calls.find((call) => call.startsWith('POST') && call.endsWith('/maps/run'))
    expect(run).toBeDefined()

    fireEvent.click(screen.getByText('Vendas'))
    expect(await screen.findByText('Vendas de vidro')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '7111' })).toHaveAttribute('href', '/contas/7111')
    // Uma linha de fórmula não se explica.
    fireEvent.click(screen.getByText('Resultado'))
    expect(screen.getByText('Vendas de vidro')).toBeInTheDocument()
  })

  it('desenha a partir de um modelo e guarda como mapa da empresa', async () => {
    const calls: string[] = []
    const fetchMock = serve(calls)
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/mapas/modelo/exploracao-mensal')
    expect(await screen.findByRole('heading', { name: 'Mapa de exploração mensal' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Desenhar' }))
    fireEvent.change(screen.getByLabelText('Contas da linha L10'), { target: { value: '71 + 72' } })
    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar linha' }))
    expect(screen.getByLabelText('Código da linha 4')).toHaveValue('L40')
    fireEvent.change(screen.getByLabelText('Nome do mapa'), { target: { value: 'Cópia' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar como mapa da empresa' }))
    await waitFor(() => expect(calls.some((call) => call.startsWith('POST') && call.endsWith('/maps'))).toBe(true))
    const saved = JSON.parse(String(fetchMock.mock.calls.find((call) => call[1]?.method === 'POST' && String(call[0]).endsWith('/maps'))![1]!.body)) as { name: string; definition: { rows: { accounts: string | null }[] } }
    expect(saved.name).toBe('Cópia')
    expect(saved.definition.rows[0].accounts).toBe('71 + 72')
    expect(saved.definition.rows).toHaveLength(4)
    expect(await screen.findByText('Mapa «Cópia» guardado.')).toBeInTheDocument()
  })
})
