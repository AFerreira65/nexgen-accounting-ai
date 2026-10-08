import { Ban, Check, CircleAlert, Database, FileText, Inbox } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from '../app/WorkContext'
import { EmailAccountPanel } from './EmailAccountPanel'

type Attachment = { id: string; fileName: string; contentType: string; sizeBytes: number; archived: boolean }
type InboundEmail = {
  id: string
  firmId: string
  fromAddress: string
  subject: string
  receivedAtUtc: string
  bodyPreview: string | null
  status: 'Archived' | 'NeedsCompany' | 'Ignored'
  companyId: string | null
  companyName: string | null
  attachments: Attachment[]
}
type OrgFirm = { id: string; canCreateCompanies: boolean; companies: { id: string; legalName: string; canManage: boolean }[] }

const filters = [
  { key: 'pending', label: 'Por atribuir' },
  { key: 'archived', label: 'Arquivados' },
  { key: 'ignored', label: 'Sem documentos' },
] as const
const when = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'medium', timeStyle: 'short' })
const size = (bytes: number) => bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

/**
 * Os documentos recebidos por email na caixa do gabinete (F4E-03). A aplicação arquiva sozinha os que consegue
 * ligar a uma empresa; os outros esperam aqui, para o utilizador escolher a empresa (e então passam para o arquivo
 * digital dela) ou pôr o email de parte. O dono ou um administrador do gabinete configura a caixa.
 */
export function InboxPage() {
  const { activeCompany, source, user } = useWorkContext()
  const onDatabase = source === 'database'
  const [firm, setFirm] = useState<OrgFirm | null>(null)
  const [status, setStatus] = useState<typeof filters[number]['key']>('pending')
  const [emails, setEmails] = useState<InboundEmail[]>([])
  const [choice, setChoice] = useState<Record<string, string>>({})
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  useEffect(() => {
    if (!onDatabase) return
    void fetch('/api/v1/development/organization')
      .then(async (response) => response.ok ? await response.json() as OrgFirm[] : [])
      .then((firms) => setFirm(firms.find((item) => item.companies.some((company) => company.id === activeCompany.companyId)) ?? null))
      .catch(() => setFirm(null))
  }, [onDatabase, activeCompany.companyId])

  useEffect(() => {
    if (!onDatabase) return
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/inbound-emails?status=${status}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as InboundEmail[] : Promise.reject(new Error(await problemMessage(response))))
      .then((rows) => { if (!controller.signal.aborted) { setEmails(rows); setError('') } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os emails.') })
    return () => controller.abort()
  }, [onDatabase, status, user.id, reload])

  const act = useCallback(async (path: string, body?: unknown, done?: string) => {
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/inbound-emails/${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      if (done) setMessage(done)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy(false)
    }
  }, [user.id])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Caixa de entrada</h1><p>Os documentos que clientes e fornecedores enviam por email ao gabinete.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Inbox /></span><h2>Requer ligação à base de dados</h2><p>A receção de emails precisa do servidor da aplicação.</p></section>
    </div>
  }

  const companies = firm?.companies ?? []
  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Caixa de entrada</h1><p>Os documentos que clientes e fornecedores enviam por email ao gabinete. A aplicação arquiva sozinha os que consegue ligar a uma empresa; os outros esperam aqui por si.</p></div></header>

    {firm && companies.some((company) => company.canManage) && <EmailAccountPanel firmId={firm.id} />}

    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}

    <div className="filterbar no-print" role="tablist" aria-label="Estado dos emails">
      {filters.map((filter) => <button key={filter.key} type="button" role="tab" aria-selected={status === filter.key} className={`button ${status === filter.key ? 'button-primary' : 'button-secondary'}`} onClick={() => setStatus(filter.key)}>{filter.label}</button>)}
    </div>

    {emails.length === 0 && <section className="panel empty-state"><span className="empty-icon"><Inbox /></span><h2>{status === 'pending' ? 'Nada por atribuir' : 'Sem emails'}</h2>
      <p>{status === 'pending' ? 'Os documentos que a aplicação não souber a que empresa pertencem aparecem aqui.' : 'Ainda não há emails nesta lista.'}</p></section>}

    {emails.map((email) => <section className="panel" key={email.id} aria-label={`Email de ${email.fromAddress}`}>
      <div className="panel-heading"><div>
        <h2>{email.subject || '(sem assunto)'}</h2>
        <p>{email.fromAddress} · {when.format(new Date(email.receivedAtUtc))}{email.companyName && <> · arquivado em <strong>{email.companyName}</strong></>}</p>
      </div></div>
      {email.bodyPreview && <p className="mr-hint">{email.bodyPreview}</p>}
      <ul className="attachment-list">
        {email.attachments.map((attachment) => <li key={attachment.id}><FileText size={15} />
          {attachment.archived || email.status !== 'NeedsCompany'
            ? <span>{attachment.fileName}</span>
            : <a href={`/api/v1/development/users/${user.id}/inbound-emails/attachments/${attachment.id}`} target="_blank" rel="noreferrer">{attachment.fileName}</a>}
          <small>{size(attachment.sizeBytes)}{attachment.archived ? ' · arquivado' : ''}</small></li>)}
      </ul>
      {email.status === 'NeedsCompany' && <div className="builder-row no-print">
        <label>Empresa
          <select aria-label="Empresa do documento" value={choice[email.id] ?? ''} onChange={(event) => setChoice({ ...choice, [email.id]: event.target.value })}>
            <option value="">Escolher…</option>{companies.map((company) => <option key={company.id} value={company.id}>{company.legalName}</option>)}</select></label>
        <button className="button button-primary" type="button" disabled={busy || !choice[email.id]} onClick={() => void act(`${email.id}/assign`, { companyId: choice[email.id] }, 'Documentos arquivados na empresa.')}><Check size={16} /> Arquivar na empresa</button>
        <button className="button button-secondary" type="button" disabled={busy} onClick={() => void act(`${email.id}/ignore`, undefined, 'Email posto de parte.')}><Ban size={16} /> Não é um documento</button>
      </div>}
      {email.status === 'Archived' && email.companyId && <p className="mr-hint">Os documentos estão no <Link to="/arquivo">arquivo digital</Link> da empresa.</p>}
    </section>)}
  </div>
}
