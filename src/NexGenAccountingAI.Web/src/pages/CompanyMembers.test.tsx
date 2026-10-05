import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { CompanyMembers, type CompanyMember } from './CompanyMembers'

const members: CompanyMember[] = [
  { userId: 'u-owner', displayName: 'Dona', email: 'dona@x.invalid', role: 'Owner', isActive: true, isResponsible: false },
  { userId: 'u-acc', displayName: 'Contabilista', email: 'c@x.invalid', role: 'Accountant', isActive: true, isResponsible: false },
]

describe('acessos a uma empresa', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('um administrador não mexe no dono nem dá a função de dono', () => {
    render(<CompanyMembers companyId="c1" companyName="Empresa" members={members} knownUsers={[]} canGrantOwner={false} onChanged={() => {}} />)
    expect(screen.queryByLabelText('Função de Dona')).toBeNull()
    expect(screen.getByLabelText('Acesso ativo de Dona')).toBeDisabled()
    const role = screen.getByLabelText('Função de Contabilista')
    expect(within(role).queryByRole('option', { name: 'Dono' })).toBeNull()
    expect(within(role).getByRole('option', { name: 'Consulta' })).toBeInTheDocument()
  })

  it('marca o responsável e grava o acesso', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const onChanged = vi.fn()
    render(<CompanyMembers companyId="c1" companyName="Empresa" members={members} knownUsers={[]} canGrantOwner onChanged={onChanged} />)
    fireEvent.click(screen.getByLabelText('Contabilista é responsável'))
    const row = screen.getByLabelText('Contabilista é responsável').closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/development/organization/companies/c1/members/u-acc', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ role: 'Accountant', isActive: true, isResponsible: true }),
    }))
  })

  it('mostra a recusa do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { membership: ['A empresa ficaria sem nenhum dono ativo.'] } }), { status: 400 })))
    render(<CompanyMembers companyId="c1" companyName="Empresa" members={members} knownUsers={[]} canGrantOwner onChanged={() => {}} />)
    fireEvent.change(screen.getByLabelText('Função de Dona'), { target: { value: 'Administrator' } })
    const row = screen.getByLabelText('Função de Dona').closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText(/sem nenhum dono ativo/)).toBeInTheDocument()
  })
})
