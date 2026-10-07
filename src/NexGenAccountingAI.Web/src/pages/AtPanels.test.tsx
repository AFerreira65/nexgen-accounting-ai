import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AtCredentialsPanel } from './AtCredentialsPanel'

const atCredentials = vi.fn()
const saveAtCredentials = vi.fn()
const deleteAtCredentials = vi.fn()

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({ atCredentials, saveAtCredentials, deleteAtCredentials }),
}))

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
