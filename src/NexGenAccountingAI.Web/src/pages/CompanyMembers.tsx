import { CircleAlert, UserPlus } from 'lucide-react'
import { useState } from 'react'
import { COMPANY_ROLES, companyRoleLabel } from '../app/companyRoles'

export type CompanyMember = { userId: string; displayName: string; email: string; role: string; isActive: boolean; isResponsible?: boolean; permissionGroupId?: string | null }
type Draft = { role: string; isActive: boolean; isResponsible: boolean; permissionGroupId?: string | null }
export type PermissionGroupOption = { id: string; name: string }

async function problem(response: Response) {
  try {
    const body = await response.json() as { errors?: Record<string, string[]> }
    return body.errors ? Object.values(body.errors).flat()[0] ?? `HTTP ${response.status}` : `HTTP ${response.status}`
  } catch { return `HTTP ${response.status}` }
}

/** Grava o acesso de um utilizador a uma empresa (função, ativo, responsável e grupo de permissões). */
export async function saveMembership(companyId: string, userId: string, draft: Draft) {
  const response = await fetch(`/api/v1/development/organization/companies/${companyId}/members/${userId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(draft.permissionGroupId === undefined ? draft : { ...draft, setPermissionGroup: true }),
  })
  if (!response.ok) throw new Error(await problem(response))
}

/**
 * Quem tem acesso a uma empresa, para o dono ou um administrador: a função, se
 * o acesso está ativo e quem é responsável pela empresa no gabinete (a carteira,
 * F3-28C). As regras — só um dono mexe em donos, a empresa não fica sem dono —
 * são do servidor; aqui só se mostra a resposta.
 */
export function CompanyMembers({ companyId, companyName, members, knownUsers, groups = [], canGrantOwner, onChanged }: {
  companyId: string
  companyName: string
  members: CompanyMember[]
  knownUsers: { userId: string; displayName: string; email: string }[]
  groups?: PermissionGroupOption[]
  canGrantOwner: boolean
  onChanged: () => void
}) {
  const [drafts, setDrafts] = useState<Record<string, Draft>>({})
  const [newUserId, setNewUserId] = useState('')
  const [newRole, setNewRole] = useState('Accountant')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const draft = (member: CompanyMember): Draft => drafts[member.userId] ?? { role: member.role, isActive: member.isActive, isResponsible: member.isResponsible ?? false, permissionGroupId: groups.length > 0 ? member.permissionGroupId ?? null : undefined }
  const change = (member: CompanyMember, patch: Partial<Draft>) => setDrafts((current) => ({ ...current, [member.userId]: { ...draft(member), ...patch } }))
  const roles = COMPANY_ROLES.filter((role) => canGrantOwner || role !== 'Owner')
  const candidates = knownUsers.filter((user) => !members.some((member) => member.userId === user.userId))

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError('')
    try {
      await action()
      setDrafts({})
      onChanged()
    } catch (problemError) {
      setError(problemError instanceof Error ? problemError.message : 'Não foi possível guardar o acesso.')
    } finally {
      setBusy(false)
    }
  }

  return <section className="panel" aria-label={`Acessos a ${companyName}`}>
    <div className="panel-heading"><div><h2>Acessos a {companyName}</h2><p>Quem trabalha nesta empresa, com que função e com que grupo de permissões (o que pode fazer em cada ecrã; os grupos gerem-se em Administração → Grupos de permissões). «Responsável» marca de quem é a empresa na carteira do gabinete: o painel do gabinete filtra por ele. Só um dono dá ou tira a função de dono, e a empresa tem de ficar sempre com um dono ativo.</p></div></div>
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    <div className="table-wrap"><table>
      <thead><tr><th>Utilizador</th><th>Função</th>{groups.length > 0 && <th>Grupo de permissões</th>}<th>Ativo</th><th>Responsável</th><th><span className="sr-only">Ações</span></th></tr></thead>
      <tbody>{members.map((member) => {
        const value = draft(member)
        const locked = !canGrantOwner && member.role === 'Owner'
        return <tr key={member.userId}>
          <td>{member.displayName}<small>{member.email}</small></td>
          <td>{locked
            ? companyRoleLabel(member.role)
            : <select aria-label={`Função de ${member.displayName}`} value={value.role} onChange={(event) => change(member, { role: event.target.value })}>{roles.map((role) => <option key={role} value={role}>{companyRoleLabel(role)}</option>)}</select>}</td>
          {groups.length > 0 && <td>{locked || value.role === 'Owner' || value.role === 'Administrator'
            ? <small>Todas as permissões</small>
            : <select aria-label={`Grupo de permissões de ${member.displayName}`} value={value.permissionGroupId ?? ''} onChange={(event) => change(member, { permissionGroupId: event.target.value || null })}><option value="">Predefinido da função</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select>}</td>}
          <td><input type="checkbox" aria-label={`Acesso ativo de ${member.displayName}`} disabled={locked} checked={value.isActive} onChange={(event) => change(member, { isActive: event.target.checked, isResponsible: event.target.checked && value.isResponsible })} /></td>
          <td><input type="checkbox" aria-label={`${member.displayName} é responsável`} disabled={locked || !value.isActive} checked={value.isResponsible} onChange={(event) => change(member, { isResponsible: event.target.checked })} /></td>
          <td><button className="button button-secondary" type="button" disabled={busy || !drafts[member.userId]} onClick={() => void run(() => saveMembership(companyId, member.userId, value))}>Guardar</button></td>
        </tr>
      })}</tbody>
    </table></div>
    {candidates.length > 0 && <div className="builder-row">
      <label>Dar acesso a <select aria-label="Utilizador a quem dar acesso" value={newUserId} onChange={(event) => setNewUserId(event.target.value)}><option value="">Escolher…</option>{candidates.map((user) => <option key={user.userId} value={user.userId}>{user.displayName} · {user.email}</option>)}</select></label>
      <label>Função <select aria-label="Função do novo acesso" value={newRole} onChange={(event) => setNewRole(event.target.value)}>{roles.map((role) => <option key={role} value={role}>{companyRoleLabel(role)}</option>)}</select></label>
      <button className="button button-secondary" type="button" disabled={busy || !newUserId} onClick={() => void run(async () => { await saveMembership(companyId, newUserId, { role: newRole, isActive: true, isResponsible: false }); setNewUserId('') })}><UserPlus size={16} /> Dar acesso</button>
    </div>}
  </section>
}
