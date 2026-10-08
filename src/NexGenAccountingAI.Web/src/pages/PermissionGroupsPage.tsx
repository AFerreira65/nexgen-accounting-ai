import { Copy, Database, KeyRound, Lock, Plus, Save, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type PermissionDefinition = { key: string; resource: string; resourceLabel: string; section: string; action: string; actionLabel: string; accountantOnly: boolean }
type PermissionGroup = { id: string; firmId: string; name: string; description: string | null; isSystem: boolean; permissions: string[]; members: number }
type OrgFirm = { id: string; companies: { id: string }[] }
type Draft = { id: string | null; name: string; description: string; permissions: string[]; isSystem: boolean }

const BASE = '/api/v1/development/organization'
const jsonHeaders = { 'Content-Type': 'application/json' }

/**
 * Os grupos de permissões do gabinete (ADR-0018): cada grupo é uma matriz ecrã × ação, e atribui-se
 * a cada utilizador no acesso à empresa. As ações que usam a senha do contabilista só as tem um
 * utilizador «Contabilista», por isso aparecem bloqueadas.
 */
export function PermissionGroupsPage() {
  const { activeCompany, source } = useWorkContext()
  const database = source === 'database'
  const [catalog, setCatalog] = useState<PermissionDefinition[]>([])
  const [firmId, setFirmId] = useState<string | null>(null)
  const [groups, setGroups] = useState<PermissionGroup[]>([])
  const [draft, setDraft] = useState<Draft | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)

  const loadGroups = useCallback(async (firm: string) => {
    const response = await fetch(`${BASE}/firms/${firm}/permission-groups`)
    if (!response.ok) throw new Error(await problemMessage(response))
    setGroups(await response.json() as PermissionGroup[])
  }, [])

  useEffect(() => {
    if (!database) { setLoading(false); return }
    let cancelled = false
    void (async () => {
      try {
        const [catalogResponse, orgResponse] = await Promise.all([fetch(`${BASE}/permission-catalog`), fetch(BASE)])
        if (!catalogResponse.ok) throw new Error(await problemMessage(catalogResponse))
        if (!orgResponse.ok) throw new Error(await problemMessage(orgResponse))
        const firms = await orgResponse.json() as OrgFirm[]
        const firm = firms.find((item) => item.companies.some((company) => company.id === activeCompany.companyId))?.id ?? null
        if (cancelled) return
        setCatalog(await catalogResponse.json() as PermissionDefinition[])
        setFirmId(firm)
        if (firm) await loadGroups(firm)
      } catch (loadError) {
        if (!cancelled) setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar os grupos.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [database, activeCompany.companyId, loadGroups])

  const sections = useMemo(() => {
    const result: { section: string; resources: { resource: string; label: string; items: PermissionDefinition[] }[] }[] = []
    for (const item of catalog) {
      let section = result.find((entry) => entry.section === item.section)
      if (!section) { section = { section: item.section, resources: [] }; result.push(section) }
      let resource = section.resources.find((entry) => entry.resource === item.resource)
      if (!resource) { resource = { resource: item.resource, label: item.resourceLabel, items: [] }; section.resources.push(resource) }
      resource.items.push(item)
    }
    return result
  }, [catalog])

  const select = (group: PermissionGroup) => { setError(''); setMessage(''); setDraft({ id: group.id, name: group.name, description: group.description ?? '', permissions: group.permissions, isSystem: group.isSystem }) }
  const startNew = (from?: Draft) => { setError(''); setMessage(''); setDraft({ id: null, name: from ? `${from.name} (cópia)` : '', description: from?.description ?? '', permissions: from?.permissions.filter((key) => !catalog.find((item) => item.key === key)?.accountantOnly) ?? [], isSystem: false }) }
  const toggle = (key: string, on: boolean) => setDraft((current) => current && { ...current, permissions: on ? [...current.permissions, key] : current.permissions.filter((item) => item !== key) })
  const setMany = (keys: string[], on: boolean) => setDraft((current) => current && { ...current, permissions: on ? [...new Set([...current.permissions, ...keys])] : current.permissions.filter((item) => !keys.includes(item)) })

  async function run(action: () => Promise<string | void>) {
    setBusy(true); setError(''); setMessage('')
    try {
      const done = await action()
      if (firmId) await loadGroups(firmId)
      if (done) setMessage(done)
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy(false)
    }
  }

  const save = () => draft && firmId && run(async () => {
    if (!draft.name.trim()) throw new Error('Indique o nome do grupo.')
    const body = JSON.stringify({ name: draft.name, description: draft.description, permissions: draft.permissions })
    const response = draft.id
      ? await fetch(`${BASE}/permission-groups/${draft.id}`, { method: 'PUT', headers: jsonHeaders, body })
      : await fetch(`${BASE}/firms/${firmId}/permission-groups`, { method: 'POST', headers: jsonHeaders, body })
    if (!response.ok) throw new Error(await problemMessage(response))
    const saved = await response.json() as PermissionGroup
    setDraft({ id: saved.id, name: saved.name, description: saved.description ?? '', permissions: saved.permissions, isSystem: false })
    return 'Grupo guardado.'
  })

  const remove = () => draft?.id && run(async () => {
    const response = await fetch(`${BASE}/permission-groups/${draft.id}`, { method: 'DELETE' })
    if (!response.ok) throw new Error(await problemMessage(response))
    setDraft(null)
    return 'Grupo apagado.'
  })

  const current = draft?.id ? groups.find((group) => group.id === draft.id) : undefined

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Administração</p><h1>Grupos de permissões</h1><p>Cada grupo diz o que um utilizador pode fazer em cada ecrã — consultar, criar, alterar, enviar à AT… Os grupos são do gabinete e atribuem-se a cada utilizador no acesso à empresa (Organização). Quem é «Contabilista» tem sempre tudo, e só ele usa as senhas da AT.</p></div>
      <span className={`connection-badge ${database ? 'database' : 'demonstration'}`}><Database size={15} />{database ? 'Ligado à base de dados' : 'Modo de demonstração'}</span>
    </header>

    {!database && <p className="save-status">Os grupos de permissões requerem a ligação à API local.</p>}
    {error && <p className="save-status error" role="alert">{error}</p>}
    {message && !error && <p className="save-status success">{message}</p>}

    {database && !loading && !firmId && !error && <p className="save-status">Só o dono ou um administrador de uma empresa gere os grupos de permissões.</p>}

    {database && firmId && <section className="panel">
      <div className="panel-heading"><div><h2><KeyRound size={17} /> Grupos</h2><p>{groups.length} {groups.length === 1 ? 'grupo' : 'grupos'}. Os de origem não se alteram: copie-os para criar um grupo seu.</p></div>
        <button className="button button-primary" type="button" onClick={() => startNew()}><Plus size={16} /> Novo grupo</button></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Grupo</th><th>Descrição</th><th className="numeric">Permissões</th><th className="numeric">Utilizadores</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>
          {groups.map((group) => <tr key={group.id}>
            <td>{group.name} {group.isSystem && <span className="badge">De origem</span>}</td>
            <td>{group.description ?? '—'}</td>
            <td className="numeric">{group.permissions.length}</td>
            <td className="numeric">{group.members}</td>
            <td><button className="button button-secondary" type="button" onClick={() => select(group)}>{group.isSystem ? 'Ver' : 'Editar'}</button></td>
          </tr>)}
          {loading && <tr><td colSpan={5} className="empty-state">A carregar…</td></tr>}
        </tbody>
      </table></div>
    </section>}

    {draft && <section className="panel" aria-label="Editar grupo">
      <div className="panel-heading"><div><h2>{draft.id ? (draft.isSystem ? `Grupo de origem: ${draft.name}` : `Editar ${draft.name}`) : 'Novo grupo'}</h2>
        <p>{draft.isSystem ? 'Os grupos de origem só se consultam; copie-o para o poder alterar.' : 'Marque o que o grupo pode fazer em cada ecrã.'}</p></div>
        <div className="toolbar-actions">
          <button className="button button-secondary" type="button" onClick={() => startNew(draft)}><Copy size={16} /> Copiar</button>
          {!draft.isSystem && draft.id && <button className="button button-secondary" type="button" disabled={busy || (current?.members ?? 0) > 0} title={(current?.members ?? 0) > 0 ? 'Está atribuído a utilizadores: atribua-lhes outro grupo primeiro.' : undefined} onClick={() => void remove()}><Trash2 size={16} /> Apagar</button>}
          {!draft.isSystem && <button className="button button-primary" type="button" disabled={busy} onClick={() => void save()}><Save size={16} /> Guardar</button>}
        </div>
      </div>
      <div className="settings-grid">
        <label><span>Nome</span><input value={draft.name} maxLength={80} disabled={draft.isSystem} onChange={(event) => setDraft({ ...draft, name: event.target.value })} /></label>
        <label><span>Descrição</span><input value={draft.description} maxLength={300} disabled={draft.isSystem} onChange={(event) => setDraft({ ...draft, description: event.target.value })} /></label>
      </div>
      {sections.map(({ section, resources }) => {
        const editableKeys = resources.flatMap((resource) => resource.items.filter((item) => !item.accountantOnly).map((item) => item.key))
        const allOn = editableKeys.every((key) => draft.permissions.includes(key))
        return <fieldset className="perm-section" key={section} disabled={draft.isSystem}>
          <legend>{section}</legend>
          <label className="perm-all"><input type="checkbox" checked={allOn} onChange={(event) => setMany(editableKeys, event.target.checked)} /> Marcar tudo em {section}</label>
          {resources.map((resource) => <div className="perm-row" key={resource.resource}>
            <strong>{resource.label}</strong>
            <span className="perm-actions">{resource.items.map((item) => <label key={item.key} className={item.accountantOnly ? 'perm-locked' : undefined} title={item.accountantOnly ? 'Usa a senha do contabilista: só um utilizador marcado como contabilista a tem.' : undefined}>
              <input type="checkbox" disabled={item.accountantOnly} checked={!item.accountantOnly && draft.permissions.includes(item.key)} onChange={(event) => toggle(item.key, event.target.checked)} />
              {item.actionLabel}{item.accountantOnly && <Lock size={12} aria-label="Só para o contabilista" />}
            </label>)}</span>
          </div>)}
        </fieldset>
      })}
    </section>}
  </div>
}
