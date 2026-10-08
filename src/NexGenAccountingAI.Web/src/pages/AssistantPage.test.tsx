import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AssistantPage } from './AssistantPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    can: () => true,
    activeCompany: { companyId: 'c1', legalName: 'Empresa A', settings: {} },
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const conversation = { id: 'k1', title: 'Quanto vendi?', updatedAtUtc: '2026-10-07T09:00:00Z', messages: 2 }
const history = [
  { id: 'm1', role: 'user', content: 'Quanto vendi?', tools: [], createdAtUtc: '2026-10-07T09:00:00Z' },
  { id: 'm2', role: 'assistant', content: 'Vendeu 1 000 €.', tools: [{ name: 'run_listing', summary: 'Listagem «Saldos por conta e mês»: 2 linha(s)', isError: false }], createdAtUtc: '2026-10-07T09:00:05Z' },
]

function serve(calls: { url: string; body?: unknown }[], answer: unknown = {
  refusal: null, conversationId: 'k2', hitLimit: false,
  answer: { id: 'm9', role: 'assistant', content: 'Os gastos subiram 700 €.', tools: [{ name: 'variance', summary: 'Variações de 01/09/2026–30/09/2026 contra 01/08/2026–31/08/2026', isError: false }], createdAtUtc: '2026-10-07T10:00:00Z' },
}) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    if (init?.method === 'POST') return Promise.resolve(json(answer))
    if (init?.method === 'DELETE') return Promise.resolve(new Response(null, { status: 204 }))
    if (url.endsWith('/chat/k1')) return Promise.resolve(json(history))
    return Promise.resolve(json([conversation]))
  })
}

describe('assistente', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lista as conversas e abre uma com o que a IA consultou', async () => {
    vi.stubGlobal('fetch', serve([]))
    render(<AssistantPage />)
    fireEvent.click(await screen.findByRole('button', { name: /^Quanto vendi\?/ }))
    expect(await screen.findByText('Vendeu 1 000 €.')).toBeInTheDocument()
    expect(screen.getByText('A IA consultou 1 fonte(s)')).toBeInTheDocument()
    expect(screen.getByText(/Listagem «Saldos por conta e mês»/)).toBeInTheDocument()
  })

  it('envia a pergunta, mostra a resposta com as fontes e continua a mesma conversa', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    render(<AssistantPage />)
    fireEvent.change(await screen.findByLabelText('Pergunta'), { target: { value: 'O que mudou nos gastos?' } })
    fireEvent.click(screen.getByRole('button', { name: /Enviar/ }))
    expect(await screen.findByText('Os gastos subiram 700 €.')).toBeInTheDocument()
    expect(calls.find((call) => call.url.startsWith('POST'))!.body).toEqual({ conversationId: null, question: 'O que mudou nos gastos?' })
    fireEvent.change(screen.getByLabelText('Pergunta'), { target: { value: 'E em julho?' } })
    fireEvent.click(screen.getByRole('button', { name: /Enviar/ }))
    await waitFor(() => expect(calls.filter((call) => call.url.startsWith('POST')).length).toBe(2))
    expect(calls.filter((call) => call.url.startsWith('POST'))[1].body).toEqual({ conversationId: 'k2', question: 'E em julho?' })
  })

  it('mostra a recusa e devolve a pergunta ao campo', async () => {
    vi.stubGlobal('fetch', serve([], { refusal: 'A IA está desligada para esta empresa.', conversationId: null, answer: null, hitLimit: false }))
    render(<AssistantPage />)
    fireEvent.change(await screen.findByLabelText('Pergunta'), { target: { value: 'Quanto vendi?' } })
    fireEvent.click(screen.getByRole('button', { name: /Enviar/ }))
    expect(await screen.findByText('A IA está desligada para esta empresa.')).toBeInTheDocument()
    expect(screen.getByLabelText('Pergunta')).toHaveValue('Quanto vendi?')
  })

  it('uma sugestão envia-se de um clique, e apaga-se uma conversa', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    vi.stubGlobal('confirm', () => true)
    render(<AssistantPage />)
    fireEvent.click(await screen.findByRole('button', { name: /O que falta para fechar o mês passado\?/ }))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('POST'))).toBe(true))
    expect((calls.find((call) => call.url.startsWith('POST'))!.body as { question: string }).question).toBe('O que falta para fechar o mês passado?')
    const list = screen.getByRole('region', { name: 'Conversas' })
    fireEvent.click(within(list).getByLabelText('Apagar Quanto vendi?'))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('DELETE') && call.url.endsWith('/chat/k1'))).toBe(true))
  })
})
