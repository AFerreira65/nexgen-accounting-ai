import { fireEvent, render, screen } from '@testing-library/react'
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

    expect(screen.getByRole('link', { name: /Abrir COMP-2026-00842/i })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Almeida & Costa/i }))
    fireEvent.click(screen.getByRole('menuitem', { name: /Ilha Azul Serviços/i }))

    expect(screen.getByRole('link', { name: /Abrir VEND-AZO-2026-00014/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Abrir COMP-2026-00842/i })).not.toBeInTheDocument()
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
  })
})
