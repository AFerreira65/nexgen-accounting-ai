import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AiKeyPanel } from './AiKeyPanel'

const json = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const empty = { key: { configured: false, hint: null, updatedAtUtc: null }, source: 'nenhuma' }
const configured = { key: { configured: true, hint: 'abcd1234', updatedAtUtc: '2026-10-08T09:00:00Z' }, source: 'geral' }

describe('chave da IA do produtor', () => {
  const fetchMock = vi.fn()
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock) })
  afterEach(() => vi.unstubAllGlobals())

  it('guarda a chave, limpa o campo e nunca a mostra', async () => {
    let saved = false
    fetchMock.mockImplementation((_url: string, init?: RequestInit) => { if (init?.method === 'PUT') saved = true; return json(saved ? configured : empty) })
    render(<AiKeyPanel />)
    expect(await screen.findByText('Não configurada')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Testar a chave/ })).toBeDisabled()

    const input = screen.getByLabelText(/Chave da API/)
    fireEvent.change(input, { target: { value: 'sk-ant-api03-uma-chave-de-teste-123456' } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar a chave/ }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/api/v1/producer/ai-settings')
    expect(JSON.parse(init.body as string)).toEqual({ apiKey: 'sk-ant-api03-uma-chave-de-teste-123456' })
    expect(await screen.findByText(/Chave guardada, cifrada/)).toBeInTheDocument()
    expect(input).toHaveValue('')
    expect(screen.getByText('Configurada')).toBeInTheDocument()
    expect(screen.getByText('…abcd1234')).toBeInTheDocument()
    expect(document.body.textContent).not.toContain('sk-ant-api03-uma-chave')
  })

  it('mostra a recusa da Anthropic ao testar', async () => {
    fetchMock.mockImplementation((url: string) => url.endsWith('/test') ? json({ ok: false, message: 'API key is invalid.', source: 'geral' }) : json(configured))
    render(<AiKeyPanel />)
    fireEvent.click(await screen.findByRole('button', { name: /Testar a chave/ }))
    expect(await screen.findByText(/A Anthropic não aceitou a chave: API key is invalid\./)).toBeInTheDocument()
  })
})
