import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ListingPage } from './ListingPage'
import { ListingsPage } from './ListingsPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const field = (key: string, label: string, type: string, extra: Record<string, unknown> = {}) => ({
  key, label, type, filterable: true, sortable: true, groupable: false, measure: false,
  operators: type === 'text' ? ['contains', 'notcontains', 'eq', 'ne', 'starts', 'empty', 'notempty'] : ['eq', 'ne', 'gt', 'ge', 'lt', 'le', 'between'], ...extra,
})
const catalog = {
  sources: [{
    key: 'linhas', label: 'Linhas de lançamentos', description: 'Cada linha de cada lançamento.',
    fields: [
      field('data', 'Data', 'date'), field('lancamento', 'Lançamento', 'text'), field('conta', 'Conta', 'text', { groupable: true }),
      field('terceiro', 'Terceiro', 'text', { groupable: true }), field('debito', 'Débito', 'money', { measure: true }), field('credito', 'Crédito', 'money', { measure: true }),
    ],
  }, {
    key: 'compras', label: 'Compras do e-Fatura', description: 'As faturas de compra.',
    fields: [field('data', 'Data', 'date'), field('fornecedor', 'Fornecedor', 'text', { groupable: true }), field('total', 'Total', 'money', { measure: true })],
  }],
  dateTokens: ['hoje', 'inicio-ano'],
}
const definition = {
  source: 'linhas', columns: ['conta', 'debito', 'credito'], groupBy: ['conta'],
  filter: { op: 'and', conditions: [{ field: 'data', operator: 'ge', value: '@inicio' }], groups: [] },
  sort: [], parameters: [{ name: 'inicio', label: 'Desde', type: 'date', default: 'inicio-ano' }],
}
const saved = [{ id: 'l1', name: 'Vendas por conta', description: 'Do ano', scope: 'company', isFavorite: true, mine: true, definition }]
const templates = [{
  key: 'compras-por-lancar', name: 'Compras por lançar', description: 'As faturas do e-Fatura por lançar.',
  definition: { source: 'compras', columns: ['data', 'total'], filter: null, sort: [], groupBy: [], parameters: [] },
}]
const columns = [
  { key: 'conta', label: 'Conta', type: 'text', measure: false },
  { key: 'debito', label: 'Débito', type: 'money', measure: true },
  { key: 'credito', label: 'Crédito', type: 'money', measure: true },
]

function serve(calls: { url: string; body?: unknown }[]) {
  return vi.fn((url: string, init?: RequestInit) => {
    const body = init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : undefined
    calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body })
    if (url.endsWith('/listings/catalog')) return Promise.resolve(json(catalog))
    if (url.endsWith('/listings/templates')) return Promise.resolve(json(templates))
    if (url.endsWith('/listings/ai-draft')) {
      const request = (body as { request: string }).request
      if (request === 'desligada') return Promise.resolve(json({ refusal: 'A IA está desligada para esta empresa.', draft: null }))
      return Promise.resolve(json({ refusal: null, draft: { name: 'Vendas por cliente', explanation: 'Agrupa por cliente.', definition: templates[0].definition, problems: [], model: 'modelo' } }))
    }
    if (url.endsWith('/listings/pivot')) {
      return Promise.resolve(json({
        rowField: 'conta', columnField: 'terceiro', measure: 'debito', measureLabel: 'Débito',
        rows: [{ key: '1201', label: '1201' }, { key: '6221', label: '6221' }],
        columns: [{ key: 'A', label: 'Cliente A' }, { key: null, label: '(sem valor)' }],
        values: [[100, 50], [0, 300]], rowTotals: [150, 300], columnTotals: [100, 350], grandTotal: 450,
      }))
    }
    if (url.endsWith('/listings') && !init?.method) return Promise.resolve(json(saved))
    if (url.endsWith('/listings') && init?.method === 'POST') return Promise.resolve(json({ ...saved[0], id: 'l2' }))
    if (url.endsWith('/listings/run')) {
      const path = (body?.groupPath ?? []) as (string | null)[]
      if (path.length === 0) {
        return Promise.resolve(json({
          kind: 'groups', columns, groupBy: ['conta'], rows: [], totalRows: 3, totals: { debito: 1800, credito: 1800, saldo: 0 }, warnings: [],
          groups: [{ key: '7211', label: '7211', count: 2, sums: { debito: 0, credito: 1500, saldo: -1500 } }, { key: null, label: '(sem valor)', count: 1, sums: { debito: 300, credito: 0, saldo: 300 } }],
        }))
      }
      return Promise.resolve(json({
        kind: 'rows', columns, groupBy: ['conta'], groups: [], totalRows: 2, totals: { debito: 0, credito: 1500, saldo: -1500 }, warnings: [],
        rows: [{ cells: ['7211', 0, 1000], link: { entryReference: 'L-1', partyId: null } }, { cells: ['7211', 0, 500], link: { entryReference: 'L-2', partyId: null } }],
      }))
    }
    return Promise.resolve(json({}, 404))
  })
}

function renderAt(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/listagens" element={<ListingsPage />} />
    <Route path="/listagens/nova" element={<ListingPage />} />
    <Route path="/listagens/modelo/:template" element={<ListingPage />} />
    <Route path="/listagens/:id" element={<ListingPage />} />
  </Routes></MemoryRouter>)
}

describe('listagens à medida', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lista as guardadas com o alcance em português', async () => {
    vi.stubGlobal('fetch', serve([]))
    renderAt('/listagens')
    expect(await screen.findByRole('link', { name: /Vendas por conta/ })).toHaveAttribute('href', '/listagens/l1')
    expect(screen.getByText('Esta empresa')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Compras por lançar' })).toHaveAttribute('href', '/listagens/modelo/compras-por-lancar')
    fireEvent.change(screen.getByLabelText('Procurar listagens'), { target: { value: 'inexistente' } })
    expect(screen.queryByRole('link', { name: /Vendas por conta/ })).not.toBeInTheDocument()
  })

  it('corre com os parâmetros, abre um grupo e liga a linha ao lançamento', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens/l1')
    expect(await screen.findByRole('heading', { name: 'Vendas por conta' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-02-01' } })
    fireEvent.click(screen.getByRole('button', { name: /Executar/ }))
    expect(await screen.findByText('(sem valor)')).toBeInTheDocument()
    const run = calls.find((call) => call.url.endsWith('/listings/run'))!.body as { parameters: Record<string, string>; groupPath: unknown[] }
    expect(run.parameters).toEqual({ inicio: '2026-02-01' })
    expect(run.groupPath).toEqual([])
    expect(screen.getAllByText('1 800,00', { selector: 'tfoot strong' }), 'o total geral, em débito e em crédito').toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: /Conta: 7211/ }))
    expect(await screen.findAllByText('7211')).not.toHaveLength(0)
    const links = await screen.findAllByRole('link', { name: '7211' })
    expect(links).toHaveLength(2)
    expect(links[0]).toHaveAttribute('href', '/contas/7211')
    const second = calls.filter((call) => call.url.endsWith('/listings/run'))[1].body as { groupPath: unknown[] }
    expect(second.groupPath).toEqual(['7211'])
  })

  it('abre o desenho, junta uma condição e guarda uma cópia', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens/l1')
    await screen.findByRole('heading', { name: 'Vendas por conta' })
    fireEvent.click(screen.getByRole('button', { name: /Desenhar/ }))
    expect(await screen.findByLabelText('Nome da listagem')).toHaveValue('Vendas por conta')

    fireEvent.click(screen.getByRole('button', { name: /Condição/ }))
    expect(screen.getByLabelText('Campo da condição 2')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Campo da condição 2'), { target: { value: 'conta' } })
    fireEvent.change(screen.getByLabelText('Operador da condição 2'), { target: { value: 'starts' } })
    fireEvent.change(screen.getByLabelText('Valor da condição 2'), { target: { value: '72' } })

    fireEvent.click(screen.getByRole('button', { name: /Guardar cópia/ }))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('POST') && call.url.endsWith('/listings'))).toBe(true))
    const body = calls.find((call) => call.url.startsWith('POST') && call.url.endsWith('/listings'))!.body as { name: string; scope: string; definition: { filter: { conditions: { operator: string; value: string }[] } } }
    expect(body.name).toBe('Vendas por conta (cópia)')
    expect(body.scope).toBe('company')
    expect(body.definition.filter.conditions[1]).toMatchObject({ operator: 'starts', value: '72' })
  })

  it('abre um modelo do sistema e corre-o sem o guardar', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens/modelo/compras-por-lancar')
    expect(await screen.findByRole('heading', { name: 'Compras por lançar' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Executar/ }))
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/listings/run'))).toBe(true))
    const run = calls.find((call) => call.url.endsWith('/listings/run'))!.body as { definition: { source: string } }
    expect(run.definition.source).toBe('compras')
    expect(calls.some((call) => call.url.startsWith('POST') && call.url.endsWith('/listings'))).toBe(false)
  })

  it('troca a origem dos dados numa listagem nova e repõe as colunas dessa origem', async () => {
    vi.stubGlobal('fetch', serve([]))
    renderAt('/listagens/nova')
    const origin = await screen.findByLabelText('Origem dos dados')
    expect(screen.getByLabelText('Débito')).toBeChecked()
    fireEvent.change(origin, { target: { value: 'compras' } })
    await waitFor(() => expect(screen.queryByLabelText('Débito')).not.toBeInTheDocument())
  })

  it('gera a tabela cruzada com os filtros da listagem e mostra os totais', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens/l1')
    await screen.findByRole('heading', { name: 'Vendas por conta' })
    fireEvent.change(screen.getByLabelText('Campo das linhas'), { target: { value: 'conta' } })
    fireEvent.change(screen.getByLabelText('Campo das colunas'), { target: { value: 'terceiro' } })
    fireEvent.change(screen.getByLabelText('Medida da tabela cruzada'), { target: { value: 'debito' } })
    fireEvent.click(screen.getByRole('button', { name: /Gerar tabela cruzada/ }))
    const table = await screen.findByRole('table', { name: 'Resultado da tabela cruzada' })
    expect(table).toHaveTextContent('Cliente A')
    expect(table).toHaveTextContent('450,00')
    const body = calls.find((call) => call.url.endsWith('/listings/pivot'))!.body as { rowField: string; columnField: string; measure: string; definition: { filter: unknown } }
    expect(body).toMatchObject({ rowField: 'conta', columnField: 'terceiro', measure: 'debito' })
    expect(body.definition.filter).toBeTruthy()
  })

  it('pinta as células que cumprem uma regra de formatação, sem mudar o valor', async () => {
    const withRule = { ...saved[0], definition: { ...definition, groupBy: [], formatting: [{ column: 'credito', operator: 'gt', value: '700', tone: 'danger' }] } }
    const base = serve([])
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      if (url.endsWith('/listings') && !init?.method) return Promise.resolve(json([withRule]))
      if (url.endsWith('/listings/run')) {
        return Promise.resolve(json({
          kind: 'rows', columns, groupBy: [], groups: [], totalRows: 2, totals: { debito: 0, credito: 1500, saldo: -1500 }, warnings: [],
          rows: [{ cells: ['7211', 0, 1000], link: { entryReference: 'L-1', partyId: null } }, { cells: ['7211', 0, 500], link: { entryReference: 'L-2', partyId: null } }],
        }))
      }
      return base(url, init)
    }))
    renderAt('/listagens/l1')
    await screen.findByRole('heading', { name: 'Vendas por conta' })
    fireEvent.click(screen.getByRole('button', { name: /Executar/ }))
    const high = await screen.findByText('1 000,00', { selector: 'td' })
    expect(high).toHaveClass('tone-danger')
    expect(screen.getByText('500,00', { selector: 'td' })).not.toHaveClass('tone-danger')
  })

  it('pede uma listagem em português e abre o rascunho no desenhador, sem guardar', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens')
    fireEvent.change(await screen.findByLabelText('O que quer ver'), { target: { value: 'compras por lançar' } })
    fireEvent.click(screen.getByRole('button', { name: /Criar com IA/ }))
    expect(await screen.findByLabelText('Nome da listagem')).toHaveValue('Vendas por cliente')
    expect(screen.getByRole('note')).toHaveTextContent('Confira as colunas')
    expect(calls.find((call) => call.url.endsWith('/listings/ai-draft'))!.body).toEqual({ request: 'compras por lançar' })
    expect(calls.some((call) => call.url.startsWith('POST') && call.url.endsWith('/listings'))).toBe(false)
  })

  it('mostra a recusa quando a IA está desligada', async () => {
    vi.stubGlobal('fetch', serve([]))
    renderAt('/listagens')
    fireEvent.change(await screen.findByLabelText('O que quer ver'), { target: { value: 'desligada' } })
    fireEvent.click(screen.getByRole('button', { name: /Criar com IA/ }))
    expect(await screen.findByText('A IA está desligada para esta empresa.')).toBeInTheDocument()
  })

  it('abre já com os parâmetros do endereço e corre sozinha (vindo de um ecrã de registo)', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderAt('/listagens/l1?inicio=2026-03-01&run=1')
    expect(await screen.findByText('(sem valor)')).toBeInTheDocument()
    const runs = calls.filter((call) => call.url.endsWith('/listings/run'))
    expect(runs).toHaveLength(1)
    expect((runs[0].body as { parameters: Record<string, string> }).parameters).toEqual({ inicio: '2026-03-01' })
  })
})
