import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ProducerShell } from '../app/ProducerShell'
import { ProducerSettingsPage, type ProducerAtSettings } from './ProducerSettingsPage'

const certificate = { configured: true, valid: true, subject: 'CN=Teste', issuer: null, serialNumber: null, thumbprint: null, notBeforeUtc: '2026-01-01T00:00:00Z', notAfterUtc: '2027-01-01T00:00:00Z', hasPrivateKey: true, environment: 'Testes', problem: null, source: 'geral' } as const
const publicKey = { configured: true, valid: true, fileName: 'ChaveCifraPublicaAT2027.cer', subject: 'CN=Chave', notBeforeUtc: null, notAfterUtc: '2027-04-15T00:00:00Z', source: 'geral', problem: null } as const

const configured: ProducerAtSettings = {
  settings: { certificateFileName: 'TesteWebservices.pfx', publicKeyFileName: 'ChaveCifraPublicaAT2027.cer', passwordConfigured: true, updatedAtUtc: '2026-10-07T09:00:00Z' },
  certificate,
  publicKey,
}

const empty: ProducerAtSettings = {
  settings: { certificateFileName: null, publicKeyFileName: null, passwordConfigured: false, updatedAtUtc: null },
  certificate: { ...certificate, configured: false, valid: false, subject: null, source: 'nenhuma' },
  publicKey: { ...publicKey, configured: false, valid: false, fileName: null, subject: null, source: 'nenhuma' },
}

const json = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

describe('configuração geral do produtor', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('mostra o estado dos ficheiros e diz que há senha sem a mostrar', async () => {
    fetchMock.mockImplementation(() => json(configured))
    render(<ProducerSettingsPage />)

    expect(await screen.findByText('Certificado de transporte')).toBeInTheDocument()
    expect(screen.getByText('TesteWebservices.pfx')).toBeInTheDocument()
    expect(screen.getByText('Válido')).toBeInTheDocument()
    expect(screen.getByText('Válida')).toBeInTheDocument()
    expect(screen.getByText(/Guardada, cifrada/)).toBeInTheDocument()
    expect(screen.getByLabelText('Senha do certificado')).toHaveValue('')
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/producer/at-settings', expect.anything())
  })

  it('envia o ficheiro em base64 com a senha, limpa a senha e mostra a confirmação', async () => {
    fetchMock.mockImplementation((_url: string, init?: RequestInit) => json(init?.method === 'PUT' ? configured : empty))
    render(<ProducerSettingsPage />)
    await screen.findByText('Certificado de transporte')

    const file = new File([new Uint8Array([1, 2, 3, 4])], 'TesteWebservices.pfx')
    fireEvent.change(screen.getByLabelText('Ficheiro do certificado (PFX/P12)'), { target: { files: [file] } })
    fireEvent.change(screen.getByLabelText('Senha do certificado'), { target: { value: 'senha-de-teste' } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar o certificado/ }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/api/v1/producer/at-settings/certificate')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({ fileName: 'TesteWebservices.pfx', contentBase64: 'AQIDBA==', password: 'senha-de-teste' })
    expect(await screen.findByText(/Certificado guardado na base de dados/)).toBeInTheDocument()
    expect(screen.getByLabelText('Senha do certificado')).toHaveValue('')
  })

  it('pede o ficheiro antes de enviar e mostra o erro do servidor', async () => {
    fetchMock.mockImplementation((_url: string, init?: RequestInit) =>
      init?.method === 'PUT' ? json({ errors: { atSettings: ['Não foi possível abrir o certificado com essa senha.'] } }, 400) : json(empty))
    render(<ProducerSettingsPage />)
    await screen.findByText('Certificado de transporte')

    fireEvent.click(screen.getByRole('button', { name: /Guardar o certificado/ }))
    expect(await screen.findByText('Escolha o ficheiro do certificado (PFX/P12).')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)

    fireEvent.change(screen.getByLabelText('Ficheiro do certificado (PFX/P12)'), { target: { files: [new File([new Uint8Array([9])], 'a.pfx')] } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar o certificado/ }))
    expect(await screen.findByText(/com essa senha/)).toBeInTheDocument()
  })

  it('guarda a chave pública e retira o certificado', async () => {
    fetchMock.mockImplementation((_url: string, init?: RequestInit) => json(init?.method === 'DELETE' ? empty : configured))
    render(<ProducerSettingsPage />)
    await screen.findByText('Certificado de transporte')

    fireEvent.change(screen.getByLabelText('Ficheiro da chave pública (CER)'), { target: { files: [new File([new Uint8Array([5, 6])], 'chave.cer')] } })
    fireEvent.click(screen.getByRole('button', { name: /Guardar a chave pública/ }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/producer/at-settings/public-key')
    expect(JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)).toEqual({ fileName: 'chave.cer', contentBase64: 'BQY=' })

    fireEvent.click(await screen.findByRole('button', { name: /Retirar o certificado/ }))
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toBe('/api/v1/producer/at-settings/certificate'))
    expect((fetchMock.mock.calls.at(-1)?.[1] as RequestInit).method).toBe('DELETE')
    expect(await screen.findByText(/Certificado retirado/)).toBeInTheDocument()
  })

  it('o ecrã do produtor não traz o menu da aplicação, só a configuração geral e a saída', async () => {
    fetchMock.mockImplementation(() => json(empty))
    render(<ProducerShell user={{ id: 'u1', displayName: 'Produtor NexGen', email: 'produtor@test.invalid', isProducer: true }} />)

    expect(await screen.findByText('Configuração geral da aplicação')).toBeInTheDocument()
    expect(screen.getByText('Produtor NexGen')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Sair/ })).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.queryByText('Comunicação com a AT')).not.toBeInTheDocument()
  })
})
