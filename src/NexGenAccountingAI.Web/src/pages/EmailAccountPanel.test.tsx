import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { captureRows, toCsv, downloadCsv } from '../app/csv'
import { EmailAccountPanel } from './EmailAccountPanel'

const account = {
  configured: true, provider: 'Password', address: 'ana@empresa.pt', displayName: 'Ana', smtpHost: 'smtp.exemplo.pt', smtpPort: 587, smtpSecurity: 'StartTls',
  imapHost: null, imapPort: null, imapSecurity: 'Ssl', imapFolder: 'INBOX', username: null, hasSecret: true, authorizedAccount: null,
  dailyDigest: false, pollingEnabled: false, lastUsedAtUtc: null, lastError: null,
}
const json = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

describe('conta de email', () => {
  const fetchMock = vi.fn()
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock) })
  afterEach(() => vi.unstubAllGlobals())

  it('nunca mostra a senha e só a envia quando é escrita', async () => {
    fetchMock.mockImplementation((_url: string, init?: RequestInit) => json(init?.method === 'PUT' ? account : { account, oAuth: { microsoft: false, google: false } }))
    render(<EmailAccountPanel />)
    expect(await screen.findByDisplayValue('ana@empresa.pt')).toBeInTheDocument()
    expect(screen.getByLabelText(/Senha/)).toHaveValue('')

    fireEvent.click(screen.getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/api/v1/mail/account')
    expect(JSON.parse(init.body as string)).toMatchObject({ provider: 'Password', address: 'ana@empresa.pt', password: null })

    fireEvent.change(screen.getByLabelText(/Senha/), { target: { value: 'senha-de-aplicacao' } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar/ }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(5))
    expect(JSON.parse((fetchMock.mock.calls[3] as [string, RequestInit])[1].body as string).password).toBe('senha-de-aplicacao')
  })

  it('com Microsoft pede ao produtor a aplicação OAuth quando ainda não existe', async () => {
    fetchMock.mockImplementation(() => json({ account: { ...account, provider: 'Microsoft' }, oAuth: { microsoft: false, google: true } }))
    render(<EmailAccountPanel />)
    const connect = await screen.findByRole('button', { name: /Ligar de novo/ })
    expect(connect).toBeDisabled()
    expect(screen.getByText(/ainda não registou a aplicação OAuth da Microsoft/)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Servidor de envio/)).not.toBeInTheDocument()
  })

  it('na caixa do gabinete mostra os servidores de receção', async () => {
    fetchMock.mockImplementation(() => json({ account: { ...account, imapHost: 'imap.exemplo.pt', imapPort: 993 }, oAuth: { microsoft: true, google: true } }))
    render(<EmailAccountPanel firmId="f1" />)
    expect(await screen.findByDisplayValue('imap.exemplo.pt')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/mail/firms/f1/inbox-account')
    expect(screen.getByRole('button', { name: /Ler agora/ })).toBeEnabled()
  })
})

describe('captureRows', () => {
  it('apanha as linhas que o ecrã exportaria, sem descarregar nada', async () => {
    const rows = await captureRows(() => downloadCsv('x.csv', toCsv([['Conta', 'Saldo'], ['11', 10]])))
    expect(rows).toEqual([['Conta', 'Saldo'], ['11', 10]])
  })

  it('devolve nulo quando o ecrã não tinha nada para exportar', async () => {
    expect(await captureRows(() => undefined)).toBeNull()
  })
})
