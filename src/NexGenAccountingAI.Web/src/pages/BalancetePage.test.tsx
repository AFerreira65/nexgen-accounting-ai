import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BalancetePage } from './BalancetePage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    source: 'database',
    user: { id: 'u1' },
  }),
}))
vi.mock('../app/ReportChrome', () => ({ ReportChrome: () => null }))

const columns = [
  { code: 'SI', label: 'Saldo inicial', percent: false },
  { code: 'D', label: 'Débito', percent: false },
  { code: 'C', label: 'Crédito', percent: false },
]
const answer = (rows: unknown[]) => new Response(JSON.stringify({
  columns, rows, totals: [0, 1500, 1500], totalDebit: 1500, totalCredit: 1500, accountCount: 3, maxDepth: 3, truncated: false,
  warnings: ['O exercício de 2026 ainda não tem lançamento de abertura.'],
  columnSets: [{ key: 'classico', label: 'Balancete clássico', description: '' }, { key: 'comparativo', label: 'Comparativo com o ano anterior', description: '' }],
}), { status: 200 })
const row = (code: string, name: string, depth: number, parentCode: string | null, hasChildren: boolean, values: (number | null)[]) =>
  ({ code, name, depth, parentCode, hasChildren, isPosting: !hasChildren, values })

describe('balancete em árvore', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('mostra as contas por níveis, abre um ramo e muda de colunas', async () => {
    const fetchMock = vi.fn((url: string) => Promise.resolve(url.includes('parent=21')
      ? answer([row('2111', 'Clientes gerais', 3, '21', false, [0, 1234.5, -200])])
      : answer([
        row('2', 'Contas a receber e a pagar', 1, null, true, [0, 1234.5, 200]),
        row('21', 'Clientes', 2, '2', true, [0, 1234.5, 200]),
        row('22', 'Fornecedores', 2, '2', false, [null, 0, 0]),
      ])))
    vi.stubGlobal('fetch', fetchMock)
    render(<MemoryRouter><BalancetePage /></MemoryRouter>)

    expect(await screen.findByText('Clientes')).toBeInTheDocument()
    expect(fetchMock.mock.calls[0][0]).toContain('/users/u1/companies/c1/accounting/balancete?year=2026&fromMonth=1&toMonth=12&columns=classico&depth=2')
    expect(screen.getByText(/ainda não tem lançamento de abertura/)).toBeInTheDocument()
    expect(screen.getByText('Balancete equilibrado')).toBeInTheDocument()
    expect(screen.getAllByText('1 234,50').length).toBe(2)
    expect(screen.getByRole('link', { name: '22' })).toHaveAttribute('href', '/contas/22')
    expect(screen.queryByRole('link', { name: '21' })).toBeNull()

    // O ramo 21 ainda não veio: abri-lo vai buscá-lo.
    fireEvent.click(screen.getByRole('button', { name: 'Abrir a conta 21' }))
    expect(await screen.findByText('Clientes gerais')).toBeInTheDocument()
    expect(screen.getByText('-200,00')).toHaveClass('negative')
    // Fechar a classe esconde tudo o que está por baixo, sem voltar ao servidor.
    const calls = fetchMock.mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: 'Fechar a conta 2' }))
    expect(screen.queryByText('Clientes')).toBeNull()
    expect(fetchMock.mock.calls.length).toBe(calls)

    fireEvent.change(screen.getByLabelText('Colunas'), { target: { value: 'comparativo' } })
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)![0]).toContain('columns=comparativo'))
    fireEvent.change(screen.getByLabelText('Apuramento'), { target: { value: 'sem' } })
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)![0]).toContain('excludeClosing=true'))
  })

  it('mostra a recusa do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { balancete: ['O período tem de ser de um mês a outro do mesmo exercício.'] } }), { status: 400 })))
    render(<MemoryRouter><BalancetePage /></MemoryRouter>)
    expect(await screen.findByRole('alert')).toHaveTextContent('O período tem de ser de um mês a outro')
  })
})
