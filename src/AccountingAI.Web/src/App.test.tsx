import { fireEvent, render, screen, within } from '@testing-library/react'
import indexHtml from '../index.html?raw'
import { App } from './App'

describe('App', () => {
  beforeEach(() => window.localStorage.clear())

  it('opens the dashboard and preserves context between movement and account', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Bom dia, António' })).toBeInTheDocument()
    expect(screen.getByText('Resultado')).toBeInTheDocument()
    expect(screen.getByText('Total do ativo')).toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('link', { name: /COMP 2026 00842/i })[0])
    expect(screen.getByRole('heading', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Linhas do movimento' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: '6221' }))
    expect(screen.getByRole('heading', { name: /6221 · Material de escritório/ })).toBeInTheDocument()
  })

  it('explains how to start the application when index.html is opened as a file', () => {
    expect(indexHtml).toContain("window.location.protocol === 'file:'")
    expect(indexHtml).toContain('A aplicação precisa do servidor local')
    expect(indexHtml).toContain('http://127.0.0.1:5173/')
  })

  it('switches company and opens its scoped settings', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Almeida & Costa/i }))
    fireEvent.click(screen.getByRole('menuitem', { name: /Ilha Azul Serviços/i }))

    expect(screen.getByRole('button', { name: /Ilha Azul Serviços/i })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('link', { name: 'Definições' }))
    expect(screen.getByRole('heading', { name: 'Preferências de trabalho' })).toBeInTheDocument()
    expect(screen.getByText(/configuração exclusiva de Ilha Azul Serviços/i)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Fuso horário' })).toHaveValue('Atlantic/Azores')

    // A empresa dos Açores tem o texto "Grande"; mudar para "Maior" aplica-o ao root.
    expect(document.documentElement.dataset.textSize).toBe('large')
    fireEvent.change(screen.getByRole('combobox', { name: 'Tamanho do texto' }), { target: { value: 'Larger' } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar preferências/i }))
    expect(document.documentElement.dataset.textSize).toBe('larger')
  })

  it('switches the accounting fallback together with the active company', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Almeida & Costa/i }))
    fireEvent.click(screen.getByRole('menuitem', { name: /Ilha Azul Serviços/i }))

    expect(screen.getByRole('link', { name: 'VEND AZO 2026 00014' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
  })

  it('opens the accounting master-data screens with the active company data', () => {
    window.history.pushState({}, '', '/contas')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Plano e abertura de contas' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '11' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Diários' }))
    expect(screen.getByRole('heading', { name: 'Diários' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /COMPRAS/i })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Documentos' }))
    expect(screen.getByRole('heading', { name: 'Documentos contabilísticos' })).toBeInTheDocument()
    expect(screen.getByText('Fatura de fornecedor')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Taxonomias' }))
    expect(screen.getByRole('heading', { name: 'Taxonomias' })).toBeInTheDocument()
    expect(screen.getByText('Taxonomia S — SNC base')).toBeInTheDocument()
    expect(screen.getByText(/Catálogo S \(Portaria 302\/2016\)/)).toBeInTheDocument()
  })

  it('shows and safely advances document number series', () => {
    window.history.pushState({}, '', '/series')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Séries documentais' })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Tipo de documento/i }).length).toBeGreaterThan(0)
    const ftvRow = screen.getByText('FTV').closest('tr') as HTMLElement
    fireEvent.click(within(ftvRow).getByRole('button', { name: /Avançar/i }))
    const nextNumber = within(ftvRow).getByRole('spinbutton', { name: 'Próximo número de FTV' })
    fireEvent.change(nextNumber, { target: { value: '20' } })
    fireEvent.click(within(ftvRow).getByRole('button', { name: 'Guardar' }))

    expect(screen.getByText(/Série FTV ajustada apenas nesta demonstração/i)).toBeInTheDocument()
    expect(within(screen.getByText('FTV').closest('tr') as HTMLElement).getByText('20')).toBeInTheDocument()
  })

  it('registers a balanced movement from the movements screen in demo mode', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo lançamento/i }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Documento' }), { target: { value: 'OD 2026/1' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Descrição' }), { target: { value: 'Lançamento de teste' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Conta da linha 1' }), { target: { value: '6221' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Débito da linha 1' }), { target: { value: '100' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Conta da linha 2' }), { target: { value: '2211001' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Crédito da linha 2' }), { target: { value: '100' } })
    fireEvent.change(screen.getByRole('textbox', { name: /Observações/ }), { target: { value: 'Rever com o cliente' } })

    expect(screen.getByText('Equilibrado')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Registar lançamento' }))

    const row = screen.getByRole('link', { name: 'GERAL 2026 00001' }).closest('tr') as HTMLElement
    expect(within(row).getByText('Por conferir')).toBeInTheDocument()
    expect(within(row).getByText('nota')).toBeInTheDocument()

    fireEvent.change(screen.getByRole('textbox', { name: 'Pesquisar movimentos' }), { target: { value: 'rever com o cliente' } })
    expect(screen.getByRole('link', { name: 'GERAL 2026 00001' })).toBeInTheDocument()
    fireEvent.change(screen.getByRole('textbox', { name: 'Pesquisar movimentos' }), { target: { value: '' } })

    fireEvent.click(screen.getByRole('link', { name: 'GERAL 2026 00001' }))
    expect(screen.getByRole('heading', { name: 'Observações' })).toBeInTheDocument()
    expect(screen.getByText('Rever com o cliente')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('link', { name: /Movimentos contabilísticos/ }))

    fireEvent.click(within(screen.getByRole('link', { name: 'GERAL 2026 00001' }).closest('tr') as HTMLElement).getByRole('button', { name: 'Anular' }))
    expect(within(screen.getByRole('link', { name: 'GERAL 2026 00001' }).closest('tr') as HTMLElement).getByText('Anulado')).toBeInTheDocument()
  })

  it('duplicates a movement into a prefilled new entry', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    const comprasRow = screen.getByRole('link', { name: 'COMP 2026 00842' }).closest('tr') as HTMLElement
    fireEvent.click(within(comprasRow).getByRole('button', { name: 'Duplicar' }))

    expect(screen.getByRole('heading', { name: 'Novo lançamento a partir de COMP 2026 00842' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Descrição' })).toHaveValue('Aquisição de material de escritório')
    expect(screen.getByRole('combobox', { name: 'Conta da linha 1' })).toHaveValue('6221')
    expect(screen.getByRole('combobox', { name: 'Conta da linha 3' })).toHaveValue('2211001')
    expect(screen.getByRole('textbox', { name: 'Documento' })).toHaveValue('')

    fireEvent.click(screen.getByRole('button', { name: 'Registar lançamento' }))
    expect(screen.getByRole('link', { name: 'COMPRAS 2026 00001' })).toBeInTheDocument()
  })

  it('rejects an integration account in a movement line', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo lançamento/i }))
    const account1 = screen.getByRole('combobox', { name: 'Conta da linha 1' })

    fireEvent.change(account1, { target: { value: '1' } })
    fireEvent.blur(account1)
    expect(account1).toHaveClass('invalid')
    expect(screen.getByText(/conta de integração/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registar lançamento' })).toBeDisabled()

    fireEvent.change(account1, { target: { value: '6221' } })
    fireEvent.blur(account1)
    expect(account1).not.toHaveClass('invalid')
    expect(screen.queryByText(/conta de integração/i)).not.toBeInTheDocument()
  })

  it('suggests movement accounts while typing in the editor', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo lançamento/i }))
    const account1 = screen.getByRole('combobox', { name: 'Conta da linha 1' })
    fireEvent.focus(account1)
    fireEvent.change(account1, { target: { value: '22' } })

    const option = screen.getByText('2211001')
    expect(option).toBeInTheDocument()
    fireEvent.mouseDown(option)
    expect(account1).toHaveValue('2211001')
  })

  it('advances fields with Enter and adds a line from the last field', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo lançamento/i }))

    const account1 = screen.getByRole('combobox', { name: 'Conta da linha 1' })
    fireEvent.keyDown(account1, { key: 'Enter' })
    expect(screen.getByRole('textbox', { name: 'Descrição da linha 1' })).toHaveFocus()

    expect(screen.queryByRole('combobox', { name: 'Conta da linha 3' })).not.toBeInTheDocument()
    fireEvent.keyDown(screen.getByRole('spinbutton', { name: 'Crédito da linha 2' }), { key: 'Enter' })
    const account3 = screen.getByRole('combobox', { name: 'Conta da linha 3' })
    expect(account3).toBeInTheDocument()
    expect(account3).toHaveFocus()
  })

  it('filters the movement list by journal and status', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Estado'), { target: { value: 'Anulado' } })
    expect(screen.getByText('Nenhum movimento corresponde aos filtros.')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Estado'), { target: { value: 'Por conferir' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'BANC 2026 00196' })).toBeInTheDocument()
  })

  it('filters the movement list by date range', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    // COMP é de 2026-08-29, VEND de 2026-08-28: um limite superior a 28 exclui o COMP.
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Data final'), { target: { value: '2026-08-28' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'VEND 2026 00327' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Limpar datas' }))
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()

    const createObjectURL = vi.fn(() => 'blob:mock')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    fireEvent.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(clickSpy).toHaveBeenCalledTimes(1)
    clickSpy.mockRestore()
  })

  it('numbers a movement by document type in demo mode', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo lançamento/i }))
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de documento' }), { target: { value: 'FTV' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Documento' }), { target: { value: 'FT A/1' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Descrição' }), { target: { value: 'Fatura a cliente' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Conta da linha 1' }), { target: { value: '2111001' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Débito da linha 1' }), { target: { value: '120' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Conta da linha 2' }), { target: { value: '6221' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Crédito da linha 2' }), { target: { value: '120' } })
    fireEvent.click(screen.getByRole('button', { name: 'Registar lançamento' }))

    expect(screen.getByRole('link', { name: 'FTV 2026 00001' })).toBeInTheDocument()
  })

  it('shows the journal report with chronological entries and totals', () => {
    window.history.pushState({}, '', '/livro-diario')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Livro diário' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Diário' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Tipo de documento' })).toBeInTheDocument()
    expect(screen.getAllByText('Total de débito').length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()

    const createObjectURL = vi.fn(() => 'blob:mock')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    fireEvent.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(clickSpy).toHaveBeenCalledTimes(1)
    clickSpy.mockRestore()

    fireEvent.change(screen.getByLabelText('Conta'), { target: { value: '7211' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'VEND 2026 00327' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Conta'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Data inicial'), { target: { value: '2026-09-01' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByText('Sem lançamentos para os filtros escolhidos.')).toBeInTheDocument()
  })

  it('shows the trial balance with debit and credit totals', () => {
    window.history.pushState({}, '', '/balancete')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Balancete' })).toBeInTheDocument()
    expect(screen.getByText('Total de débito')).toBeInTheDocument()
    expect(screen.getByText('Total de crédito')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '6221' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Abertura' })).not.toBeInTheDocument()

    expect(screen.queryByText(/Subtotal classe/)).not.toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Agrupar por classe'))
    expect(screen.getByText('6 · Gastos')).toBeInTheDocument()
    expect(screen.getByText('Subtotal classe 6')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '6221' })).toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Agrupar por classe'))
    expect(screen.queryByText(/Subtotal classe/)).not.toBeInTheDocument()

    // Agrupar por taxonomia: as contas de demonstração dos movimentos não têm código,
    // pelo que caem no grupo "Sem taxonomia"; ligar desliga o agrupamento por classe.
    fireEvent.click(screen.getByLabelText('Agrupar por taxonomia'))
    expect(screen.getByText('Subtotal Sem taxonomia')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '6221' })).toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Agrupar por classe'))
    expect(screen.getByLabelText('Agrupar por taxonomia')).not.toBeChecked()
    expect(screen.queryByText('Subtotal Sem taxonomia')).not.toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Agrupar por classe'))

    fireEvent.click(within(screen.getByRole('group', { name: 'Períodos rápidos' })).getByRole('button', { name: 'Ano' }))
    expect(screen.getByLabelText('Data inicial')).toHaveValue('2026-01-01')
    expect(screen.getByLabelText('Data final')).toHaveValue('2026-12-31')

    fireEvent.change(screen.getByLabelText('Data inicial'), { target: { value: '2026-08-29' } })
    expect(screen.getByRole('columnheader', { name: 'Abertura' })).toBeInTheDocument()

    expect(screen.queryByRole('columnheader', { name: 'Saldo homólogo' })).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Data final'), { target: { value: '2026-12-31' } })
    fireEvent.click(screen.getByLabelText('Comparar com período homólogo'))
    expect(screen.getByRole('columnheader', { name: 'Saldo homólogo' })).toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Comparar com período homólogo'))

    fireEvent.change(screen.getByLabelText('Data final'), { target: { value: '2020-12-31' } })
    expect(screen.queryByRole('link', { name: '6221' })).not.toBeInTheDocument()
    expect(screen.getByText('Sem movimentos para apresentar.')).toBeInTheDocument()
  })

  it('shows the income statement grouped by nature with a net result', () => {
    window.history.pushState({}, '', '/resultados')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Demonstração de resultados' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Rendimentos' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Gastos' })).toBeInTheDocument()
    expect(screen.getByText('72 · Prestações de serviços')).toBeInTheDocument()
    expect(screen.getByText('62 · Fornecimentos e serviços externos')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '7211' })).toBeInTheDocument()
    expect(screen.getAllByText('Resultado líquido do período').length).toBeGreaterThan(0)

    expect(screen.queryByRole('columnheader', { name: 'Homólogo' })).not.toBeInTheDocument()
    fireEvent.change(screen.getAllByLabelText('Data inicial')[0], { target: { value: '2026-01-01' } })
    fireEvent.change(screen.getAllByLabelText('Data final')[0], { target: { value: '2026-12-31' } })
    fireEvent.click(screen.getByLabelText('Comparar com período homólogo'))
    expect(screen.getAllByRole('columnheader', { name: 'Homólogo' }).length).toBeGreaterThan(0)
  })

  it('shows the balance sheet with assets, liabilities and equity in balance', () => {
    window.history.pushState({}, '', '/balanco')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Balanço' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Ativo' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Passivo' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Capital próprio' })).toBeInTheDocument()
    expect(screen.getByText('21 · Clientes')).toBeInTheDocument()
    expect(screen.getAllByText('24 · Estado e outros entes públicos').length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: '2111001' })).toBeInTheDocument()
    expect(screen.getByText('Ativo = Passivo + CP')).toBeInTheDocument()

    expect(screen.queryByRole('columnheader', { name: 'Homólogo' })).not.toBeInTheDocument()
    fireEvent.change(screen.getAllByLabelText('Data inicial')[0], { target: { value: '2026-01-01' } })
    fireEvent.change(screen.getAllByLabelText('Data final')[0], { target: { value: '2026-12-31' } })
    fireEvent.click(screen.getByLabelText('Comparar com período homólogo'))
    expect(screen.getAllByRole('columnheader', { name: 'Homólogo' }).length).toBeGreaterThan(0)
  })

  it('previews the year-end closing entry and keeps register disabled in demo mode', () => {
    window.history.pushState({}, '', '/apuramento')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Apuramento de resultados' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '7211' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '6221' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '81' })).toBeInTheDocument()
    expect(screen.getByText(/requer ligação ao SQL Server/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registar apuramento/ })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Reabrir exercício/ })).not.toBeInTheDocument()
  })

  it('previews the year-opening entry carrying balances forward, register disabled in demo', () => {
    window.history.pushState({}, '', '/abertura')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Abertura do exercício' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '1211' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '56' })).toBeInTheDocument()
    expect(screen.getByText(/requer ligação ao SQL Server/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registar abertura/ })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Reabrir exercício/ })).not.toBeInTheDocument()
  })

  it('shows the account ledger with a running balance and a date filter', () => {
    window.history.pushState({}, '', '/contas/6221')
    render(<App />)

    expect(screen.getByRole('heading', { name: /6221 · Material de escritório/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Movimentos desta conta' })).toBeInTheDocument()
    const comprasRow = screen.getByRole('link', { name: 'COMP 2026 00842' }).closest('tr') as HTMLElement
    expect(within(comprasRow).getByText('nota')).toHaveAttribute('title', expect.stringContaining('30 dias'))

    fireEvent.change(screen.getByLabelText('Data inicial'), { target: { value: '2026-09-01' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByText('Sem movimentos para esta conta.')).toBeInTheDocument()
    expect(screen.getAllByText('Saldo de abertura').length).toBeGreaterThan(0)
  })

  it('aggregates the movement sub-accounts for an integration account', () => {
    window.history.pushState({}, '', '/contas/1')
    render(<App />)

    expect(screen.getByText(/Conta de integração: soma dos movimentos das subcontas/i)).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Conta' })).toBeInTheDocument()
    const bancRow = screen.getByRole('link', { name: 'BANC 2026 00196' }).closest('tr') as HTMLElement
    expect(within(bancRow).getByRole('link', { name: '1211' })).toBeInTheDocument()
  })

  it('shows the organisation screen with companies and users sections', () => {
    window.history.pushState({}, '', '/organizacao')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Empresas e utilizadores' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Empresas' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Utilizadores' })).toBeInTheDocument()
    expect(screen.getByText(/requer a liga/i)).toBeInTheDocument()
  })

  it('creates a journal and a document type in demo mode', () => {
    window.history.pushState({}, '', '/diarios')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Novo diário/i }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Código' }), { target: { value: 'AUX' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Descrição' }), { target: { value: 'Diário auxiliar' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar diário' }))
    expect(screen.getByRole('link', { name: /AUX/ })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: 'Documentos' }))
    fireEvent.click(screen.getByRole('button', { name: /Novo documento/i }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Código' }), { target: { value: 'FTX' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Descrição' }), { target: { value: 'Documento auxiliar X' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar documento' }))
    expect(screen.getByText('Documento auxiliar X')).toBeInTheDocument()
  })

  it('creates a company account from the chart of accounts screen', () => {
    window.history.pushState({}, '', '/contas')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Nova conta/i }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Código' }), { target: { value: '6811' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Descrição' }), { target: { value: 'Donativos e liberalidades' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar conta' }))

    expect(screen.getByRole('link', { name: '6811' })).toBeInTheDocument()
  })

  it('reads a SAF-T header and matches the company by tax number', async () => {
    window.history.pushState({}, '', '/importar')
    render(<App />)
    expect(screen.getByRole('heading', { name: 'Importar SAF-T' })).toBeInTheDocument()

    const xml = [
      '<?xml version="1.0" encoding="utf-8"?>',
      '<AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01"><Header>',
      '<AuditFileVersion>1.04_01</AuditFileVersion>',
      '<TaxRegistrationNumber>DEMO-PT-0001</TaxRegistrationNumber>',
      '<TaxAccountingBasis>I</TaxAccountingBasis>',
      '<CompanyName>Empresa do Ficheiro, Lda.</CompanyName>',
      '<FiscalYear>2026</FiscalYear><StartDate>2026-01-01</StartDate><EndDate>2026-12-31</EndDate>',
      '<CurrencyCode>EUR</CurrencyCode></Header>',
      '<MasterFiles><GeneralLedgerAccounts>',
      '<Account><AccountID>11</AccountID><AccountDescription>Caixa</AccountDescription></Account>',
      '<Account><AccountID>99999999</AccountID><AccountDescription>Conta nova do ficheiro</AccountDescription></Account>',
      '</GeneralLedgerAccounts></MasterFiles></AuditFile>',
    ].join('\n')

    fireEvent.change(screen.getByLabelText(/Escolher ficheiro SAF-T/i), {
      target: { files: [new File([xml], 'saft.xml', { type: 'text/xml' })] },
    })

    const matchLine = await screen.findByText(/Empresa encontrada/i)
    expect(matchLine).toHaveTextContent(/Almeida & Costa/)
    expect(screen.getByText(/com contabilidade/i)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Pré-visualizar plano de contas/i }))
    const newRow = await screen.findByText('99999999')
    expect(within(newRow.closest('tr') as HTMLElement).getByText('Nova')).toBeInTheDocument()
    expect(within(screen.getByText('11').closest('tr') as HTMLElement).getByText('Já existe')).toBeInTheDocument()
  })

  it('proposes creating a company when the SAF-T tax number is unknown', async () => {
    window.history.pushState({}, '', '/importar')
    render(<App />)

    const xml = [
      '<?xml version="1.0" encoding="utf-8"?>',
      '<AuditFile xmlns="urn:OECD:StandardAuditFile-Tax:PT_1.04_01"><Header>',
      '<AuditFileVersion>1.04_01</AuditFileVersion>',
      '<TaxRegistrationNumber>599999999</TaxRegistrationNumber>',
      '<TaxAccountingBasis>C</TaxAccountingBasis>',
      '<CompanyName>Nova Empresa do Ficheiro, Lda.</CompanyName>',
      '<FiscalYear>2026</FiscalYear><StartDate>2026-01-01</StartDate><EndDate>2026-12-31</EndDate>',
      '<CurrencyCode>EUR</CurrencyCode></Header></AuditFile>',
    ].join('\n')

    fireEvent.change(screen.getByLabelText(/Escolher ficheiro SAF-T/i), {
      target: { files: [new File([xml], 'saft.xml', { type: 'text/xml' })] },
    })

    expect(await screen.findByRole('heading', { name: 'Propor a criação da empresa' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Designação' })).toHaveValue('Nova Empresa do Ficheiro, Lda.')
    expect(screen.getByText(/A criação requer ligação ao SQL Server/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Criar empresa e provisionar cadastros/i })).toBeDisabled()
  })

  it('clears and re-applies a template taxonomy code from the taxonomy screen', async () => {
    window.history.pushState({}, '', '/taxonomias')
    render(<App />)

    expect(screen.getByText(/2 de 5/)).toBeInTheDocument()

    const row = screen.getByRole('link', { name: '121' }).closest('tr') as HTMLElement
    fireEvent.click(within(row).getByRole('button', { name: 'Limpar' }))
    expect(await screen.findByText(/1 de 5/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Aplicar sugestões do referencial/i }))
    expect(await screen.findByText(/2 de 5/)).toBeInTheDocument()
  })
})
