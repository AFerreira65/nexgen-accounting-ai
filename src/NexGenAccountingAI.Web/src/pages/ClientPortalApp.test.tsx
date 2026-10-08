import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ClientPortalApp } from './ClientPortalApp'

const json = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const session = { accountId: 'a1', displayName: 'Maria Cliente', email: 'maria@cliente.pt' }
const item = (id: string, category: string, title: string) => ({ id, category, title, fileName: `${title}.pdf`, contentType: 'application/pdf', sizeBytes: 2048, publishedAtUtc: '2026-10-08T09:00:00Z', fromArchive: false })

describe('portal do cliente', () => {
  const fetchMock = vi.fn()
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock); window.history.pushState({}, '', '/cliente') })
  afterEach(() => vi.unstubAllGlobals())

  it('sem sessão mostra o início de sessão, com o aviso de que o acesso vem por convite', async () => {
    fetchMock.mockImplementation(() => json({}, 401))
    render(<ClientPortalApp />)
    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument()
    expect(screen.getByText(/envie um convite por email/)).toBeInTheDocument()
  })

  it('o convite pede a senha duas vezes, ativa a conta e entra', async () => {
    window.history.pushState({}, '', '/cliente/convite?token=abc')
    fetchMock.mockImplementation((url: string) => {
      if (url.startsWith('/api/v1/portal/invitation?')) return json({ email: 'maria@cliente.pt', displayName: 'Maria Cliente', companies: ['Empresa A, Lda.'] })
      if (url === '/api/v1/portal/invitation/accept') return json(session)
      if (url === '/api/v1/portal/me') return json({ session, companies: [{ companyId: 'c1', name: 'Empresa A, Lda.' }] })
      if (url === '/api/v1/portal/companies/c1/items') return json([item('i1', 'TrialBalance', 'Balancete de setembro')])
      return json({}, 404)
    })
    render(<ClientPortalApp />)
    expect(await screen.findByText(/Empresa A, Lda\./)).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'Senha-do-cliente-2026' } })
    fireEvent.change(screen.getByLabelText('Confirmar a senha'), { target: { value: 'outra-coisa-123' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ativar e entrar' }))
    expect(await screen.findByText('As duas senhas não são iguais.')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Confirmar a senha'), { target: { value: 'Senha-do-cliente-2026' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ativar e entrar' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/v1/portal/invitation/accept', expect.objectContaining({ method: 'POST' })))
    expect(JSON.parse((fetchMock.mock.calls.find((call) => call[0] === '/api/v1/portal/invitation/accept')![1] as RequestInit).body as string)).toEqual({ token: 'abc', password: 'Senha-do-cliente-2026' })
    expect(await screen.findByText('Balancete de setembro')).toBeInTheDocument()
  })

  it('mostra o que foi publicado por tipo e deixa descarregar', async () => {
    fetchMock.mockImplementation((url: string) => {
      if (url === '/api/v1/portal/me') return json({ session, companies: [{ companyId: 'c1', name: 'Empresa A, Lda.' }] })
      if (url === '/api/v1/portal/companies/c1/items') return json([item('i1', 'TrialBalance', 'Balancete de setembro'), item('i2', 'Documents', 'Fatura de setembro')])
      return json({}, 404)
    })
    render(<ClientPortalApp />)
    expect(await screen.findByText('Balancete de setembro')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Descarregar/ })).toHaveLength(2)

    fireEvent.click(screen.getByRole('tab', { name: 'Documentos' }))
    expect(screen.queryByText('Balancete de setembro')).not.toBeInTheDocument()
    expect(screen.getByText('Fatura de setembro')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Descarregar/ })[0]).toHaveAttribute('href', '/api/v1/portal/items/i2/content')
  })
  it('o «esqueci-me da senha» responde sempre o mesmo', async () => {
    fetchMock.mockImplementation((url: string) => url === '/api/v1/portal/auth/forgot' ? json({ sent: true }) : json({}, 401))
    render(<ClientPortalApp />)
    fireEvent.click(await screen.findByRole('button', { name: 'Esqueci-me da senha' }))
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'maria@cliente.pt' } })
    fireEvent.click(screen.getByRole('button', { name: 'Enviar o link' }))
    expect(await screen.findByText(/Se existir uma conta com esse email/)).toBeInTheDocument()
    expect(JSON.parse((fetchMock.mock.calls.find((call) => call[0] === '/api/v1/portal/auth/forgot')![1] as RequestInit).body as string)).toEqual({ email: 'maria@cliente.pt' })
  })

  it('envia os documentos escolhidos ao gabinete e mostra os enviados', async () => {
    const sentFiles: string[] = []
    fetchMock.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/v1/portal/me') return json({ session, companies: [{ companyId: 'c1', name: 'Empresa A, Lda.' }] })
      if (url === '/api/v1/portal/companies/c1/items') return json([])
      if (url.startsWith('/api/v1/portal/companies/c1/uploads?') && init?.method === 'POST') { sentFiles.push(decodeURIComponent(url.split('fileName=')[1])); return json({ id: 'u1' }) }
      if (url === '/api/v1/portal/companies/c1/uploads') return json(sentFiles.map((name, index) => ({ id: `u${index}`, fileName: name, sizeBytes: 1024, uploadedAtUtc: '2026-10-08T09:00:00Z' })))
      return json({}, 404)
    })
    render(<ClientPortalApp />)
    const input = await screen.findByLabelText('Documentos a enviar')
    fireEvent.change(input, { target: { files: [new File(['%PDF'], 'fatura outubro.pdf', { type: 'application/pdf' })] } })
    expect(await screen.findByText('1 documento enviado ao gabinete.')).toBeInTheDocument()
    expect(sentFiles).toEqual(['fatura outubro.pdf'])
    expect(await screen.findByText('Enviados por si')).toBeInTheDocument()
    expect(screen.getAllByText('fatura outubro.pdf').length).toBeGreaterThan(0)
  })
})
