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
    expect(screen.getByText('Caixa e bancos')).toBeInTheDocument()
    expect(screen.getByText('Saldo de clientes')).toBeInTheDocument()
    expect(screen.getByText('Saldo de fornecedores')).toBeInTheDocument()
    // Posição de IVA: 575 (24331 liquidado) − 276 (24323 dedutível) = 299, a entregar.
    const vatCard = screen.getByText('Posição de IVA').closest('a') as HTMLElement
    expect(vatCard).toHaveTextContent(/299,00/)
    expect(vatCard).toHaveTextContent('A entregar ao Estado')

    // Filtro de período: recuar a data de referência para antes do lançamento
    // de compras (29/08, IVA dedutível 276) só deixa o de vendas (28/08, IVA
    // liquidado 575) na posição de IVA.
    fireEvent.change(screen.getByLabelText('Data de referência'), { target: { value: '2026-08-28' } })
    expect(vatCard).toHaveTextContent(/575,00/)
    expect(vatCard).toHaveTextContent(/Até/)
    expect(vatCard).not.toHaveTextContent('A entregar ao Estado')
    fireEvent.click(screen.getByRole('button', { name: 'Hoje' }))
    expect(vatCard).toHaveTextContent(/299,00/)
    expect(vatCard).toHaveTextContent('A entregar ao Estado')

    fireEvent.click(screen.getAllByRole('link', { name: /COMP 2026 00842/i })[0])
    expect(screen.getByRole('heading', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Linhas do movimento' })).toBeInTheDocument()
    // A origem real, não o texto fixo antigo.
    expect(screen.getByText('Origem').closest('span')).toHaveTextContent('Manual')
    expect(screen.getByRole('button', { name: /Copiar ligação/ })).toBeInTheDocument()
    // Ações de estado no próprio detalhe (COMP está conferido).
    expect(screen.getByRole('button', { name: 'Reabrir' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Anular' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('link', { name: '6221' }))
    expect(screen.getByRole('heading', { name: /6221 · Material de escritório/ })).toBeInTheDocument()
  })

  it('keeps the dashboard reference date between visits', () => {
    window.history.pushState({}, '', '/')
    const { unmount } = render(<App />)

    fireEvent.change(screen.getByLabelText('Data de referência'), { target: { value: '2026-06-30' } })
    unmount()

    window.history.pushState({}, '', '/')
    render(<App />)
    expect(screen.getByLabelText('Data de referência')).toHaveValue('2026-06-30')
    fireEvent.click(screen.getByRole('button', { name: 'Hoje' }))
    expect(screen.getByLabelText('Data de referência')).toHaveValue('')
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

  it('changes the exercise from the top bar and every screen follows it', () => {
    window.history.pushState({}, '', '/livro-diario')
    render(<App />)

    // A empresa de demonstração tem o exercício 2026; o livro diário arranca nele.
    expect(screen.getByRole('button', { name: /Exercício 2026/ })).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Exercício' })).toHaveValue(2026)

    fireEvent.click(screen.getByRole('button', { name: /Exercício 2026/ }))
    // Escape fecha o menu sem escolher nada.
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menuitem', { name: '2024' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Exercício 2026/ }))
    fireEvent.click(screen.getByRole('menuitem', { name: '2024' }))

    expect(screen.getByRole('button', { name: /Exercício 2024/ })).toBeInTheDocument()
    // O filtro de exercício do livro diário acompanha a mudança.
    expect(screen.getByRole('spinbutton', { name: 'Exercício' })).toHaveValue(2024)
  })

  it('jumps to an account from the top-bar search', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    const search = screen.getByRole('combobox', { name: /Pesquisar conta/ })
    fireEvent.change(search, { target: { value: '6221' } })

    const hit = screen.getByRole('option', { name: /6221.*Material de escritório/ })
    fireEvent.click(hit)

    expect(screen.getByRole('heading', { name: /6221 · Material de escritório/ })).toBeInTheDocument()
    // O menu fecha e o campo limpa depois de saltar.
    expect(screen.queryByRole('option', { name: /Conta ·/ })).not.toBeInTheDocument()
    expect(search).toHaveValue('')
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
    expect(screen.getByRole('link', { name: 'COMPRAS' })).toBeInTheDocument()

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

    fireEvent.change(screen.getByLabelText('Estado'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: 'Manual' } })
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: 'SaftImport' } })
    expect(screen.getByText('Nenhum movimento corresponde aos filtros.')).toBeInTheDocument()
  })

  it('opens the movement list scoped to the company exercise', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    // A empresa de demonstração tem o exercício 2026.
    expect(screen.getByLabelText('Data inicial')).toHaveValue('2026-01-01')
    expect(screen.getByLabelText('Data final')).toHaveValue('2026-12-31')
    fireEvent.click(screen.getByRole('button', { name: 'Limpar datas' }))
    expect(screen.getByLabelText('Data inicial')).toHaveValue('')
    expect(screen.getByLabelText('Data final')).toHaveValue('')
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

  it('reopens a reviewed movement so it can be corrected', () => {
    window.history.pushState({}, '', '/movimentos')
    render(<App />)

    const row = () => screen.getByRole('link', { name: 'COMP 2026 00842' }).closest('tr') as HTMLElement
    expect(within(row()).getByText('Conferido')).toBeInTheDocument()
    expect(within(row()).queryByRole('button', { name: 'Corrigir' })).not.toBeInTheDocument()

    fireEvent.click(within(row()).getByRole('button', { name: 'Reabrir' }))
    expect(within(row()).getByText('Por conferir')).toBeInTheDocument()
    expect(within(row()).getByRole('button', { name: 'Corrigir' })).toBeInTheDocument()
    expect(within(row()).queryByRole('button', { name: 'Reabrir' })).not.toBeInTheDocument()
  })

  it('opens the movement editor from the detail screen', () => {
    window.history.pushState({}, '', '/movimentos/BANC-2026-00196')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'BANC 2026 00196' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Duplicar/ }))
    expect(screen.getByRole('heading', { name: /Novo lançamento a partir de BANC 2026 00196/ })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Corrigir' }))
    expect(screen.getByRole('heading', { name: /Corrigir BANC 2026 00196/ })).toBeInTheDocument()
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

    // Filtro de origem: os movimentos de demonstração são manuais.
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: 'Opening' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: 'Manual' } })
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: '' } })

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
    // Arranca no exercício da empresa (2026); «Limpar datas» abre tudo.
    expect(screen.getAllByLabelText('Data inicial')[0]).toHaveValue('2026-01-01')
    fireEvent.click(screen.getByRole('button', { name: 'Limpar datas' }))
    expect(screen.getAllByLabelText('Data inicial')[0]).toHaveValue('')

    expect(screen.queryByRole('columnheader', { name: 'Homólogo' })).not.toBeInTheDocument()
    fireEvent.change(screen.getAllByLabelText('Data inicial')[0], { target: { value: '2026-01-01' } })
    fireEvent.change(screen.getAllByLabelText('Data final')[0], { target: { value: '2026-12-31' } })
    fireEvent.click(screen.getByLabelText('Comparar com período homólogo'))
    expect(screen.getAllByRole('columnheader', { name: 'Homólogo' }).length).toBeGreaterThan(0)
  })

  it('shows the VAT summary from the IVA accounts', () => {
    window.history.pushState({}, '', '/iva')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Resumo de IVA' })).toBeInTheDocument()
    // Demo (empresa do Continente): 24323 dedutível a débito 276; 24331 liquidado a crédito 575.
    expect(screen.getByRole('link', { name: '24323' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '24331' })).toBeInTheDocument()
    expect(screen.getAllByText(/575,00/).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/276,00/).length).toBeGreaterThan(0)
    expect(screen.getAllByText('A entregar ao Estado').length).toBeGreaterThan(0)
    expect(screen.getAllByText(/299,00/).length).toBeGreaterThan(0)
    // Arranca no exercício da empresa (2026); «Limpar datas» abre tudo.
    expect(screen.getAllByLabelText('Data inicial')[0]).toHaveValue('2026-01-01')
    fireEvent.click(screen.getByRole('button', { name: 'Limpar datas' }))
    expect(screen.getAllByLabelText('Data inicial')[0]).toHaveValue('')

    const createObjectURL = vi.fn(() => 'blob:mock')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    fireEvent.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    clickSpy.mockRestore()
  })

  it('shows client and supplier current-account balances', () => {
    window.history.pushState({}, '', '/contas-correntes')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Contas correntes' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Clientes' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Fornecedores' })).toBeInTheDocument()
    // Demo: cliente 2111001 com saldo 3075; o fornecedor 2211001 fica a zero (fatura + pagamento).
    expect(screen.getByRole('link', { name: '2111001' })).toBeInTheDocument()
    expect(screen.getAllByText(/075,00/).length).toBeGreaterThan(0)
    expect(screen.getByText('Sem saldos de fornecedores.')).toBeInTheDocument()

    const createObjectURL = vi.fn(() => 'blob:mock')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    fireEvent.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    clickSpy.mockRestore()
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
    // A razão arranca no exercício da empresa (2026); «Limpar datas» abre tudo.
    expect(screen.getByLabelText('Data inicial')).toHaveValue('2026-01-01')
    fireEvent.click(screen.getByRole('button', { name: 'Limpar datas' }))
    expect(screen.getByLabelText('Data inicial')).toHaveValue('')
    const comprasRow = screen.getByRole('link', { name: 'COMP 2026 00842' }).closest('tr') as HTMLElement
    expect(within(comprasRow).getByText('nota')).toHaveAttribute('title', expect.stringContaining('30 dias'))

    // Filtro de origem: só restringe as linhas mostradas, o saldo mantém-se.
    const saldoAtual = screen.getByText(/Saldo (atual|final)/).closest('article') as HTMLElement
    const saldoBefore = within(saldoAtual).getByText(/€/).textContent
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: 'Opening' } })
    expect(screen.queryByRole('link', { name: 'COMP 2026 00842' })).not.toBeInTheDocument()
    expect(screen.getByText('Sem movimentos para esta conta.')).toBeInTheDocument()
    expect(within(saldoAtual).getByText(/€/).textContent).toEqual(saldoBefore)
    fireEvent.change(screen.getByLabelText('Origem'), { target: { value: '' } })
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()

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

  it('opens a journal and lists its movements', () => {
    window.history.pushState({}, '', '/diarios/COMPRAS')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Diário de Compras' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'COMP 2026 00842' })).toBeInTheDocument()
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

  it('shows the e-Fatura purchases screen (requires the database)', () => {
    window.history.pushState({}, '', '/compras-efatura')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Compras e-Fatura' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the cross-check screen (requires the database)', () => {
    window.history.pushState({}, '', '/cruzamento')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Cruzamento' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the data dictionary screen', () => {
    window.history.pushState({}, '', '/dicionario-de-dados')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Dicionário de dados' })).toBeInTheDocument()
  })

  it('shows the analyses screen', () => {
    window.history.pushState({}, '', '/analises')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Análises', level: 1 })).toBeInTheDocument()
  })

  it('shows the management reports screen (requires the database)', () => {
    window.history.pushState({}, '', '/relatorios-gestao')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Relatórios de gestão', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the VAT settlement screen (requires the database)', () => {
    window.history.pushState({}, '', '/apuramento-iva')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Apuramento de IVA', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the AT communication screen (requires the database)', () => {
    window.history.pushState({}, '', '/comunicacao-at')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Comunicação com a AT', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the VAT classification screen (requires the database)', () => {
    window.history.pushState({}, '', '/config-iva')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Configuração de IVA', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the SAF-T export screen (requires the database)', () => {
    window.history.pushState({}, '', '/exportar-saft')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Exportar SAF-T', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the financial statements screen (requires the database)', () => {
    window.history.pushState({}, '', '/demonstracoes')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Demonstrações financeiras', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the monthly evolution charts with sample data in demonstration mode', () => {
    window.history.pushState({}, '', '/evolucao')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Evolução mensal', level: 1 })).toBeInTheDocument()
    expect(screen.getByText(/dados de exemplo/i)).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Rendimentos, gastos e resultado por mês/ })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Saldo de caixa e bancos/ })).toBeInTheDocument()
  })

  it('shows the operating map and ratios screens (require the database)', () => {
    window.history.pushState({}, '', '/mapa-exploracao')
    const { unmount } = render(<App />)
    expect(screen.getByRole('heading', { name: 'Mapa de exploração', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
    unmount()

    window.history.pushState({}, '', '/racios')
    render(<App />)
    expect(screen.getByRole('heading', { name: 'Rácios', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the analytic screens (require the database)', () => {
    window.history.pushState({}, '', '/centros-custo')
    const { unmount } = render(<App />)
    expect(screen.getByRole('heading', { name: 'Centros de custo', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
    unmount()

    window.history.pushState({}, '', '/mapa-analitico')
    render(<App />)
    expect(screen.getByRole('heading', { name: 'Mapa analítico', level: 1 })).toBeInTheDocument()
  })

  it('shows the fiscal tables screen (requires the database)', () => {
    window.history.pushState({}, '', '/tabelas-fiscais')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Tabelas fiscais', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the party balances screen (requires the database)', () => {
    window.history.pushState({}, '', '/saldos-terceiros')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Saldos de terceiros', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the entry templates screen (requires the database)', () => {
    window.history.pushState({}, '', '/lancamentos-tipo')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Lançamentos-tipo', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the VAT codes screen (requires the database)', () => {
    window.history.pushState({}, '', '/codigos-iva')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Códigos de IVA', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('shows the parties screen (requires the database)', () => {
    window.history.pushState({}, '', '/terceiros')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Terceiros', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('groups the side menu and collapses it to an icon rail', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    const nav = screen.getByRole('navigation', { name: 'Navegação principal' })
    expect(within(nav).getByText('Cadastros')).toBeInTheDocument()
    expect(within(nav).getByRole('link', { name: 'Terceiros' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Recolher o menu' }))
    expect(screen.getByRole('button', { name: 'Expandir o menu' })).toHaveAttribute('aria-pressed', 'true')
    // Recolhido, as ligações continuam acessíveis pelo nome.
    expect(within(nav).getByRole('link', { name: 'Terceiros' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Expandir o menu' }))
  })

  it('shows the IRC estimate screen (requires the database)', () => {
    window.history.pushState({}, '', '/estimativa-irc')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Estimativa de IRC', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Requer ligação ao SQL Server/i })).toBeInTheDocument()
  })

  it('offers an Opções menu on every screen with the two actions', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    fireEvent.click(screen.getByRole('button', { name: /Opções/ }))
    expect(screen.getByRole('menuitem', { name: /Dicionário de dados/ })).toBeInTheDocument()
    // Sem ligação à base de dados, «Sobre o Registo» fica desativado.
    expect(screen.getByRole('menuitem', { name: /Sobre o Registo/ })).toBeDisabled()

    fireEvent.click(screen.getByRole('menuitem', { name: /Dicionário de dados/ }))
    expect(screen.getByRole('heading', { name: 'Dicionário de dados' })).toBeInTheDocument()
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

  it('opens Tarefas from the top-bar bell', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    // Demonstração: 1 movimento por conferir + contas por classificar => pendente,
    // por isso o sino tem a contagem no nome acessível (distingue-o do menu lateral).
    const bell = screen.getByRole('link', { name: /Tarefas — \d+ pendentes/ })
    fireEvent.click(bell)
    expect(screen.getByRole('heading', { name: 'Tarefas' })).toBeInTheDocument()
  })

  it('lists pending movements and unclassified accounts on the tasks screen', () => {
    window.history.pushState({}, '', '/tarefas')
    const { unmount } = render(<App />)

    expect(screen.getByRole('heading', { name: 'Tarefas' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Movimentos por conferir' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /BANC 2026 00196/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Contas de movimento por classificar' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir para Taxonomias' })).toBeInTheDocument()
    // IVA a entregar: 575 (24331 liquidado) − 276 (24323 dedutível) = 299.
    expect(screen.getByRole('heading', { name: 'IVA a entregar' })).toBeInTheDocument()
    const vatTask = screen.getByRole('link', { name: /Posição de IVA a entregar ao Estado/ })
    expect(vatTask).toHaveAttribute('href', '/iva')
    expect(screen.getByText('299,00 €')).toBeInTheDocument()

    // Dispensar esconde o lembrete e o esconde entre visitas, enquanto a
    // posição de IVA não mudar (persiste em localStorage por empresa).
    fireEvent.click(screen.getByRole('button', { name: /Dispensar até à próxima entrega/ }))
    expect(screen.queryByRole('heading', { name: 'IVA a entregar' })).not.toBeInTheDocument()
    unmount()
    window.history.pushState({}, '', '/tarefas')
    render(<App />)
    expect(screen.getByRole('heading', { name: 'Tarefas' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'IVA a entregar' })).not.toBeInTheDocument()
  })
})
