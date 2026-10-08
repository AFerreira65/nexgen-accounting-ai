import { Check, CircleAlert, Copy, Database, Globe, Mail, Send, Trash2, UserPlus, UserX } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from '../app/WorkContext'
import { formatMovementDate } from '../app/demo-data'
import { PORTAL_CATEGORIES } from './ClientPortalApp'

type PortalUser = { accountId: string; email: string; displayName: string; status: 'Invited' | 'Active' | 'Disabled'; lastLoginAtUtc: string | null; inviteExpiresAtUtc: string | null }
type InviteResult = { user: PortalUser; emailSent: boolean; link: string | null; emailError: string | null }
type ClientUpload = { id: string; fileName: string; sizeBytes: number; uploadedAtUtc: string; senderName: string | null }
type Item = { id: string; category: string; title: string; fileName: string; sizeBytes: number; publishedAtUtc: string; fromArchive: boolean; publishedByName: string | null }

const statusLabels = { Invited: 'Convidado', Active: 'Ativo', Disabled: 'Desativado' } as const
const size = (bytes: number) => bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

/**
 * O portal do cliente, do lado do gabinete (ADR-0020): convidar clientes por email e ver o que está publicado. O cliente
 * recebe um link, escolhe uma senha e a conta fica ativa. Os ficheiros publicam-se a partir dos relatórios, das listagens
 * (botão «Portal») e do arquivo digital (botão «Partilhar no portal»).
 */
export function PortalAdminPage() {
  const { activeCompany, source, user, can } = useWorkContext()
  const onDatabase = source === 'database'
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/portal`
  const [users, setUsers] = useState<PortalUser[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [uploads, setUploads] = useState<ClientUpload[]>([])
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [link, setLink] = useState<{ email: string; url: string; why: string | null } | null>(null)
  const publish = can('portal.publish')

  const load = useCallback(async () => {
    const [usersResponse, itemsResponse, uploadsResponse] = await Promise.all([fetch(`${root}/users`), fetch(`${root}/items`), fetch(`${root}/uploads`)])
    if (!usersResponse.ok) throw new Error(await problemMessage(usersResponse))
    if (!itemsResponse.ok) throw new Error(await problemMessage(itemsResponse))
    setUsers(await usersResponse.json() as PortalUser[])
    setItems(await itemsResponse.json() as Item[])
    setUploads(uploadsResponse.ok ? await uploadsResponse.json() as ClientUpload[] : [])
  }, [root])

  useEffect(() => {
    if (!onDatabase) return
    void load().catch((loadError: unknown) => setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar o portal.'))
  }, [onDatabase, load])

  async function run(action: () => Promise<string | void>) {
    setBusy(true); setError(''); setMessage('')
    try {
      const done = await action()
      await load()
      if (done) setMessage(done)
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy(false)
    }
  }

  const reportInvite = (result: InviteResult, done: string) => {
    if (result.emailSent) { setLink(null); return done }
    setLink({ email: result.user.email, url: result.link ?? '', why: result.emailError })
    return 'O convite foi criado, mas o email não seguiu: envie o link abaixo ao cliente.'
  }

  const invite = (event: FormEvent) => {
    event.preventDefault()
    return run(async () => {
      const response = await fetch(`${root}/users`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, name }) })
      if (!response.ok) throw new Error(await problemMessage(response))
      const result = await response.json() as InviteResult
      setEmail(''); setName('')
      return reportInvite(result, `Convite enviado para ${result.user.email}.`)
    })
  }

  const resend = (account: PortalUser) => run(async () => {
    const response = await fetch(`${root}/users/${account.accountId}/resend`, { method: 'POST' })
    if (!response.ok) throw new Error(await problemMessage(response))
    return reportInvite(await response.json() as InviteResult, `Novo convite enviado para ${account.email}.`)
  })

  const revoke = (account: PortalUser) => run(async () => {
    const response = await fetch(`${root}/users/${account.accountId}`, { method: 'DELETE' })
    if (!response.ok) throw new Error(await problemMessage(response))
    return `${account.email} deixou de ter acesso a esta empresa.`
  })

  const remove = (item: Item) => run(async () => {
    const response = await fetch(`${root}/items/${item.id}`, { method: 'DELETE' })
    if (!response.ok) throw new Error(await problemMessage(response))
    return `«${item.title}» deixou de estar no portal.`
  })

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Portal do cliente</h1><p>Convidar clientes e publicar-lhes documentos e relatórios.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Globe /></span><h2>Requer ligação à base de dados</h2><p>O portal precisa do servidor da aplicação.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Portal do cliente</h1>
      <p>Os clientes de {activeCompany.legalName.replace(' (Demonstração)', '')} entram em <code>{window.location.origin}/cliente</code> e veem só o que publicar aqui: documentos, relatórios, balancetes e mapas de gestão. Convide-os por email; ao confirmarem a senha, a conta fica ativa.</p></div></header>

    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}
    {link && <section className="panel" role="status"><div className="panel-heading"><div><h2><Mail size={16} /> Enviar o link à mão</h2>
      <p>{link.why ?? 'O email não seguiu.'} Para quem enviar: {link.email}. O link vale 7 dias e só pode ser usado uma vez.</p></div></div>
      <div className="builder-row"><input readOnly aria-label="Link do convite" value={link.url} style={{ minWidth: 380 }} onFocus={(event) => event.currentTarget.select()} />
        <button className="button button-secondary" type="button" onClick={() => void navigator.clipboard?.writeText(link.url)}><Copy size={15} /> Copiar</button></div></section>}

    <section className="panel">
      <div className="panel-heading"><div><h2><UserPlus size={17} /> Clientes com acesso</h2><p>{users.length} {users.length === 1 ? 'cliente' : 'clientes'}.</p></div></div>
      {publish && <form className="builder-row" onSubmit={(event) => void invite(event)}>
        <label>Nome<input value={name} onChange={(event) => setName(event.target.value)} /></label>
        <label>Email<input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></label>
        <button className="button button-primary" type="submit" disabled={busy || !email}><Send size={15} /> Convidar</button>
      </form>}
      <div className="table-wrap"><table>
        <thead><tr><th>Cliente</th><th>Estado</th><th>Último acesso</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{users.map((account) => <tr key={account.accountId}>
          <td>{account.displayName}<small>{account.email}</small></td>
          <td><span className={`badge ${account.status === 'Active' ? 'success' : 'warning'}`}>{statusLabels[account.status]}</span>{account.status === 'Invited' && account.inviteExpiresAtUtc && <small>convite até {formatMovementDate(account.inviteExpiresAtUtc.slice(0, 10))}</small>}</td>
          <td>{account.lastLoginAtUtc ? formatMovementDate(account.lastLoginAtUtc.slice(0, 10)) : '—'}</td>
          <td className="numeric">{publish && <>
            <button className="button button-secondary" type="button" disabled={busy} onClick={() => void resend(account)}>{account.status === 'Active' ? 'Nova senha' : 'Reenviar convite'}</button>
            <button className="icon-button" type="button" aria-label={`Tirar o acesso de ${account.email}`} disabled={busy} onClick={() => void revoke(account)}><UserX size={15} /></button></>}</td>
        </tr>)}
        {users.length === 0 && <tr><td colSpan={4} className="empty-state">Ainda não convidou nenhum cliente.</td></tr>}</tbody>
      </table></div>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2><Globe size={17} /> Publicado no portal</h2><p>{items.length} {items.length === 1 ? 'ficheiro' : 'ficheiros'}. Publique pelo botão «Portal» das listagens e dos relatórios, ou partilhe documentos no arquivo digital.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Ficheiro</th><th>Tipo</th><th>Publicado</th><th className="numeric">Tamanho</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{items.map((item) => <tr key={item.id}>
          <td>{item.title}<small>{item.fileName}{item.fromArchive ? ' · do arquivo' : ''}</small></td>
          <td>{PORTAL_CATEGORIES.find((entry) => entry.key === item.category)?.label ?? item.category}</td>
          <td>{formatMovementDate(item.publishedAtUtc.slice(0, 10))}{item.publishedByName && <small>{item.publishedByName}</small>}</td>
          <td className="numeric">{size(item.sizeBytes)}</td>
          <td className="numeric">{publish && <button className="icon-button" type="button" aria-label={`Retirar ${item.title} do portal`} disabled={busy} onClick={() => void remove(item)}><Trash2 size={15} /></button>}</td>
        </tr>)}
        {items.length === 0 && <tr><td colSpan={5} className="empty-state">Ainda não publicou nada.</td></tr>}</tbody>
      </table></div>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2><Send size={17} /> Enviados pelos clientes</h2><p>{uploads.length} {uploads.length === 1 ? 'documento' : 'documentos'}. Os ficheiros estão no <Link to="/arquivo">arquivo digital</Link>, sem lançamento, prontos para tratar.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Ficheiro</th><th>Enviado por</th><th>Data</th><th className="numeric">Tamanho</th></tr></thead>
        <tbody>{uploads.map((upload) => <tr key={upload.id}><td>{upload.fileName}</td><td>{upload.senderName ?? '—'}</td><td>{formatMovementDate(upload.uploadedAtUtc.slice(0, 10))}</td><td className="numeric">{size(upload.sizeBytes)}</td></tr>)}
        {uploads.length === 0 && <tr><td colSpan={4} className="empty-state">Os clientes ainda não enviaram nada.</td></tr>}</tbody>
      </table></div>
    </section>
  </div>
}
