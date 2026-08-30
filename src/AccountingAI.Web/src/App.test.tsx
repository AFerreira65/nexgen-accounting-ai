import { fireEvent, render, screen } from '@testing-library/react'
import indexHtml from '../index.html?raw'
import { App } from './App'

describe('App', () => {
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
})
