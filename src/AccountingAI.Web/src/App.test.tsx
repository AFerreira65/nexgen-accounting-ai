import { fireEvent, render, screen, within } from '@testing-library/react'
import indexHtml from '../index.html?raw'
import { App } from './App'

describe('App', () => {
  beforeEach(() => window.localStorage.clear())

  it('opens the dashboard and preserves context between movement and account', () => {
    window.history.pushState({}, '', '/')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Bom dia, António' })).toBeInTheDocument()

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

    expect(screen.getByText('Equilibrado')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Registar lançamento' }))

    const row = screen.getByRole('link', { name: 'GERAL 2026 00001' }).closest('tr') as HTMLElement
    expect(within(row).getByText('Por conferir')).toBeInTheDocument()

    fireEvent.click(within(row).getByRole('button', { name: 'Anular' }))
    expect(within(screen.getByRole('link', { name: 'GERAL 2026 00001' }).closest('tr') as HTMLElement).getByText('Anulado')).toBeInTheDocument()
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
  })

  it('shows the trial balance with debit and credit totals', () => {
    window.history.pushState({}, '', '/balancete')
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Balancete' })).toBeInTheDocument()
    expect(screen.getByText('Total de débito')).toBeInTheDocument()
    expect(screen.getByText('Total de crédito')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '6221' })).toBeInTheDocument()
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
      '<CurrencyCode>EUR</CurrencyCode></Header></AuditFile>',
    ].join('\n')

    fireEvent.change(screen.getByLabelText(/Escolher ficheiro SAF-T/i), {
      target: { files: [new File([xml], 'saft.xml', { type: 'text/xml' })] },
    })

    const matchLine = await screen.findByText(/Empresa encontrada/i)
    expect(matchLine).toHaveTextContent(/Almeida & Costa/)
    expect(screen.getByText(/com contabilidade/i)).toBeInTheDocument()
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

    const row = screen.getByRole('link', { name: '12' }).closest('tr') as HTMLElement
    fireEvent.click(within(row).getByRole('button', { name: 'Limpar' }))
    expect(await screen.findByText(/1 de 5/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Aplicar sugestões do referencial/i }))
    expect(await screen.findByText(/2 de 5/)).toBeInTheDocument()
  })
})
