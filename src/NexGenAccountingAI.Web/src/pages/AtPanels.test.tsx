import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AtCredentialsPanel } from './AtCredentialsPanel'
import { AtGeneralSettingsPanel } from './AtGeneralSettingsPanel'

const general = {
  settings: {
    canEdit: true, certificateDirectory: 'C:\\AT\\certificados', certificateFileName: 'TesteWebservices.pfx',
    publicKeyDirectory: 'C:\\AT\\chaves', publicKeyFileName: 'ChaveCifraPublicaAT2027.cer', passwordConfigured: true, updatedAtUtc: '2026-10-07T09:00:00Z',
  },
  certificate: { configured: true, valid: true, subject: 'CN=Teste', issuer: null, serialNumber: null, thumbprint: null, notBeforeUtc: null, notAfterUtc: null, hasPrivateKey: true, environment: 'Testes', problem: null, source: 'geral' },
  publicKey: { configured: true, valid: true, path: 'C:\\AT\\chaves\\ChaveCifraPublicaAT2027.cer', subject: 'CN=Chave', notBeforeUtc: null, notAfterUtc: '2027-04-15T00:00:00Z', source: 'geral', problem: null },
}

const atGeneralSettings = vi.fn()
const saveAtGeneralSettings = vi.fn()
const atCredentials = vi.fn()
const saveAtCredentials = vi.fn()
const deleteAtCredentials = vi.fn()

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({ atGeneralSettings, saveAtGeneralSettings, atCredentials, saveAtCredentials, deleteAtCredentials }),
}))

describe('configuração geral da AT', () => {
  beforeEach(() => {
    atGeneralSettings.mockReset().mockResolvedValue(general)
    saveAtGeneralSettings.mockReset().mockResolvedValue(general)
  })

  it('mostra os locais e os nomes, diz que há senha sem a mostrar e indica o estado dos ficheiros', async () => {
    render(<AtGeneralSettingsPanel />)

    expect(await screen.findByDisplayValue('TesteWebservices.pfx')).toBeInTheDocument()
    expect(screen.getByDisplayValue('C:\\AT\\certificados')).toBeInTheDocument()
    expect(screen.getByDisplayValue('ChaveCifraPublicaAT2027.cer')).toBeInTheDocument()
    expect(screen.getByLabelText('Senha do certificado')).toHaveValue('')
    expect(screen.getByPlaceholderText(/guardada; em branco mantém/)).toBeInTheDocument()
    expect(screen.getByText('Válido')).toBeInTheDocument()
    expect(screen.getByText(/válida até/)).toBeInTheDocument()
  })

  it('guarda os campos, envia a senha só quando é escrita e limpa-a depois', async () => {
    const onChanged = vi.fn()
    render(<AtGeneralSettingsPanel onChanged={onChanged} />)
    await screen.findByDisplayValue('TesteWebservices.pfx')

    fireEvent.change(screen.getByLabelText('Nome do ficheiro do certificado'), { target: { value: 'Producao.pfx' } })
    fireEvent.change(screen.getByLabelText('Senha do certificado'), { target: { value: 'nova-senha' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(saveAtGeneralSettings).toHaveBeenCalledTimes(1))
    expect(saveAtGeneralSettings.mock.calls[0][0]).toEqual({
      certificateDirectory: 'C:\\AT\\certificados', certificateFileName: 'Producao.pfx',
      publicKeyDirectory: 'C:\\AT\\chaves', publicKeyFileName: 'ChaveCifraPublicaAT2027.cer', password: 'nova-senha', removePassword: false,
    })
    expect(await screen.findByText(/Configuração guardada/)).toBeInTheDocument()
    expect(screen.getByLabelText('Senha do certificado')).toHaveValue('')
    expect(onChanged).toHaveBeenCalled()
  })

  it('permite retirar a senha guardada e mostra o erro do servidor', async () => {
    saveAtGeneralSettings.mockRejectedValueOnce(new Error('A pasta do ficheiro do certificado tem de ser um caminho completo.'))
    render(<AtGeneralSettingsPanel />)
    await screen.findByDisplayValue('TesteWebservices.pfx')

    fireEvent.click(screen.getByLabelText(/Retirar a senha guardada/))
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }))

    expect(await screen.findByText(/caminho completo/)).toBeInTheDocument()
    expect(saveAtGeneralSettings.mock.calls[0][0]).toMatchObject({ password: '', removePassword: true })
  })

  it('esconde o formulário a quem não a pode alterar', async () => {
    atGeneralSettings.mockResolvedValue({ settings: { ...general.settings, canEdit: false, certificateDirectory: null, certificateFileName: null, passwordConfigured: false }, certificate: null, publicKey: null })
    render(<AtGeneralSettingsPanel />)

    expect(await screen.findByText(/só o dono ou um administrador/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Senha do certificado')).not.toBeInTheDocument()
  })
})

describe('credenciais da AT da empresa', () => {
  beforeEach(() => {
    atCredentials.mockReset().mockResolvedValue({ configured: true, subUser: '518104486/2', periodicity: 'Trimestral', environment: 'Testes', updatedAtUtc: null })
    saveAtCredentials.mockReset().mockImplementation((_company: string, input: { subUser: string; periodicity: 'Mensal' | 'Trimestral'; environment: 'Testes' | 'Producao' }) =>
      Promise.resolve({ configured: true, subUser: input.subUser, periodicity: input.periodicity, environment: input.environment, updatedAtUtc: null }))
  })

  it('mostra o subutilizador e a periodicidade guardados, mas nunca a senha', async () => {
    render(<AtCredentialsPanel companyId="c1" companyName="Empresa Um" taxRegistrationNumber="518104486" />)

    expect(await screen.findByDisplayValue('518104486/2')).toBeInTheDocument()
    expect(screen.getByText('Configuração da AT de Empresa Um')).toBeInTheDocument()
    expect(screen.getByLabelText('Senha')).toHaveValue('')
    expect(screen.getByPlaceholderText(/guardada/)).toBeInTheDocument()
    expect(screen.getByText(/Regime trimestral, ambiente de testes/)).toBeInTheDocument()
  })

  it('exige a senha para guardar e guarda-a para a empresa certa', async () => {
    render(<AtCredentialsPanel companyId="c1" companyName="Empresa Um" taxRegistrationNumber="518104486" />)
    await screen.findByDisplayValue('518104486/2')

    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique o subutilizador e a senha.')).toBeInTheDocument()
    expect(saveAtCredentials).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'senha-da-empresa' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(saveAtCredentials).toHaveBeenCalledWith('c1', { subUser: '518104486/2', password: 'senha-da-empresa', periodicity: 'Trimestral', environment: 'Testes' }))
    expect(await screen.findByText(/Credenciais guardadas/)).toBeInTheDocument()
    expect(screen.getByLabelText('Senha')).toHaveValue('')
  })
})
