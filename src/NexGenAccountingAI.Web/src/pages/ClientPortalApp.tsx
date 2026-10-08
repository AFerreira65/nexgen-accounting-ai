import { Download, FileText, LogOut, Upload } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { problemMessage } from '../app/WorkContext'

type Session = { accountId: string; displayName: string; email: string }
type PortalCompany = { companyId: string; name: string }
type PortalItem = { id: string; category: 'Documents' | 'Reports' | 'TrialBalance' | 'Management'; title: string; fileName: string; contentType: string; sizeBytes: number; publishedAtUtc: string; fromArchive: boolean }
type Invitation = { email: string; displayName: string; companies: string[]; isReset?: boolean }
type Upload = { id: string; fileName: string; sizeBytes: number; uploadedAtUtc: string }

export const PORTAL_CATEGORIES = [
  { key: 'Documents', label: 'Documentos' },
  { key: 'Reports', label: 'Relatórios' },
  { key: 'TrialBalance', label: 'Balancetes' },
  { key: 'Management', label: 'Gestão' },
] as const

const when = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'medium' })
const size = (bytes: number) => bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

/**
 * O portal do cliente (ADR-0020): fora da aplicação do gabinete, com sessão própria. O cliente chega pelo link do
 * convite, escolhe uma senha (e a conta fica ativa) e passa a ver os documentos, relatórios, balancetes e mapas de gestão
 * que o gabinete lhe publicou. Só consulta e descarrega.
 */
export function ClientPortalApp() {
  const token = new URLSearchParams(window.location.search).get('token')
  const [session, setSession] = useState<Session | null | undefined>(token ? null : undefined)
  const [companies, setCompanies] = useState<PortalCompany[]>([])

  const load = useCallback(async () => {
    const response = await fetch('/api/v1/portal/me')
    if (!response.ok) { setSession(null); return }
    const body = await response.json() as { session: Session | null; companies: PortalCompany[] }
    setSession(body.session)
    setCompanies(body.companies)
  }, [])

  useEffect(() => { if (!token) void load().catch(() => setSession(null)) }, [token, load])

  const finish = (opened: Session) => { setSession(opened); window.history.replaceState({}, '', '/cliente'); void load() }

  const logOut = () => { void fetch('/api/v1/portal/auth/logout', { method: 'POST' }).finally(() => { setSession(null); setCompanies([]) }) }

  return <div className="portal-shell">
    <header className="producer-bar">
      <div className="login-brand"><span className="brand-mark">A</span><span><strong>Portal do cliente</strong></span></div>
      {session && <><span className="producer-user">{session.displayName}</span><button type="button" className="button" onClick={logOut}><LogOut size={15} />Sair</button></>}
    </header>
    <main className="portal-main">
      {session === undefined && <p aria-busy="true">A carregar…</p>}
      {session === null && (token ? <AcceptInvitation token={token} onDone={finish} /> : <PortalLogin onDone={finish} />)}
      {session && <PortalContent companies={companies} />}
    </main>
  </div>
}

function PortalLogin({ onDone }: { onDone: (session: Session) => void }) {
  const [forgot, setForgot] = useState(false)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true); setError('')
    try {
      const response = await fetch('/api/v1/portal/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) })
      if (!response.ok) throw new Error(await problemMessage(response))
      onDone(await response.json() as Session)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível entrar.')
    } finally {
      setBusy(false)
    }
  }

  if (forgot) return <ForgotPassword onBack={() => setForgot(false)} />

  return <form className="panel portal-card" onSubmit={(event) => void submit(event)} aria-label="Entrar no portal">
    <h1>Entrar</h1>
    <p>Os documentos e relatórios que o seu gabinete lhe disponibiliza. Ainda não tem acesso? Peça ao gabinete que lhe envie um convite por email.</p>
    {error && <p className="save-status error" role="alert">{error}</p>}
    <label>Email<input type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" required /></label>
    <label>Senha<input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" required /></label>
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'A entrar…' : 'Entrar'}</button>
    <button className="text-link" type="button" onClick={() => setForgot(true)}>Esqueci-me da senha</button>
  </form>
}

function ForgotPassword({ onBack }: { onBack: () => void }) {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      await fetch('/api/v1/portal/auth/forgot', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email }) })
    } finally {
      // A resposta é sempre a mesma: não se revela que emails têm conta.
      setSent(true)
      setBusy(false)
    }
  }

  return <form className="panel portal-card" onSubmit={(event) => void submit(event)} aria-label="Nova senha">
    <h1>Nova senha</h1>
    {sent
      ? <p role="status">Se existir uma conta com esse email, enviámos-lhe agora um link para escolher uma senha nova. Confirme também a pasta de correio não solicitado.</p>
      : <><p>Indique o email da sua conta e enviamos-lhe um link para escolher uma senha nova.</p>
        <label>Email<input type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" required /></label>
        <button className="button button-primary" type="submit" disabled={busy || !email}>{busy ? 'A enviar…' : 'Enviar o link'}</button></>}
    <button className="text-link" type="button" onClick={onBack}>Voltar a entrar</button>
  </form>
}

function AcceptInvitation({ token, onDone }: { token: string; onDone: (session: Session) => void }) {
  const [invitation, setInvitation] = useState<Invitation | null | undefined>(undefined)
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void fetch(`/api/v1/portal/invitation?token=${encodeURIComponent(token)}`)
      .then(async (response) => setInvitation(response.ok ? await response.json() as Invitation : null))
      .catch(() => setInvitation(null))
  }, [token])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (password !== confirm) { setError('As duas senhas não são iguais.'); return }
    setBusy(true); setError('')
    try {
      const response = await fetch('/api/v1/portal/invitation/accept', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token, password }) })
      if (!response.ok) throw new Error(await problemMessage(response))
      onDone(await response.json() as Session)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível ativar o acesso.')
    } finally {
      setBusy(false)
    }
  }

  if (invitation === undefined) return <p aria-busy="true">A verificar o convite…</p>
  if (invitation === null) return <section className="panel portal-card"><h1>Convite inválido</h1><p>Este convite expirou ou já foi usado. Peça ao gabinete que lhe envie um novo.</p></section>

  return <form className="panel portal-card" onSubmit={(event) => void submit(event)} aria-label="Ativar o acesso">
    <h1>{invitation.isReset ? 'Escolher uma senha nova' : 'Ativar o acesso'}</h1>
    <p>Olá, {invitation.displayName}. Escolha uma senha para entrar no portal de {invitation.companies.join(', ')} com o email <strong>{invitation.email}</strong>.</p>
    {error && <p className="save-status error" role="alert">{error}</p>}
    <label>Senha<input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" required /></label>
    <label>Confirmar a senha<input type="password" value={confirm} onChange={(event) => setConfirm(event.target.value)} autoComplete="new-password" required /></label>
    <small>Pelo menos 10 caracteres, com letras e números.</small>
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'A guardar…' : invitation.isReset ? 'Guardar e entrar' : 'Ativar e entrar'}</button>
  </form>
}

function PortalContent({ companies }: { companies: PortalCompany[] }) {
  const [companyId, setCompanyId] = useState(companies[0]?.companyId ?? '')
  const [items, setItems] = useState<PortalItem[] | null>(null)
  const [category, setCategory] = useState<PortalItem['category'] | 'all'>('all')
  const [error, setError] = useState('')
  const [uploads, setUploads] = useState<Upload[]>([])
  const [uploading, setUploading] = useState(false)
  const [notice, setNotice] = useState('')
  const fileInput = useRef<HTMLInputElement>(null)

  useEffect(() => { if (!companyId && companies[0]) setCompanyId(companies[0].companyId) }, [companies, companyId])

  useEffect(() => {
    if (!companyId) return
    const controller = new AbortController()
    void fetch(`/api/v1/portal/companies/${companyId}/items`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as PortalItem[] : Promise.reject(new Error(await problemMessage(response))))
      .then((rows) => { setItems(rows); setError('') })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar.') })
    return () => controller.abort()
  }, [companyId])

  useEffect(() => {
    if (!companyId) return
    void fetch(`/api/v1/portal/companies/${companyId}/uploads`)
      .then(async (response) => response.ok ? await response.json() as Upload[] : [])
      .then(setUploads)
      .catch(() => setUploads([]))
  }, [companyId])

  // Envia os documentos escolhidos, um a um (o corpo do pedido é o ficheiro). Cada falha mostra-se sem parar os outros.
  async function send(files: FileList | null) {
    if (!files || files.length === 0 || !companyId) return
    setUploading(true); setError(''); setNotice('')
    const failures: string[] = []
    let sent = 0
    for (const file of Array.from(files)) {
      const response = await fetch(`/api/v1/portal/companies/${companyId}/uploads?fileName=${encodeURIComponent(file.name)}`, { method: 'POST', body: file })
      if (response.ok) sent++
      else failures.push(`${file.name}: ${await problemMessage(response)}`)
    }
    const refreshed = await fetch(`/api/v1/portal/companies/${companyId}/uploads`)
    if (refreshed.ok) setUploads(await refreshed.json() as Upload[])
    if (sent > 0) setNotice(`${sent} ${sent === 1 ? 'documento enviado' : 'documentos enviados'} ao gabinete.`)
    if (failures.length > 0) setError(failures.join(' · '))
    if (fileInput.current) fileInput.current.value = ''
    setUploading(false)
  }

  if (companies.length === 0) return <section className="panel portal-card"><h1>Sem acesso</h1><p>A sua conta ainda não tem nenhuma empresa. Fale com o gabinete.</p></section>

  const shown = (items ?? []).filter((item) => category === 'all' || item.category === category)
  return <div className="page-stack">
    <header className="page-heading"><div><h1>{companies.find((company) => company.companyId === companyId)?.name}</h1><p>O que o gabinete lhe disponibilizou. Escolha o tipo de documento e descarregue o que precisar.</p></div></header>
    {companies.length > 1 && <label className="inline-check">Empresa <select aria-label="Empresa" value={companyId} onChange={(event) => { setItems(null); setCompanyId(event.target.value) }}>{companies.map((company) => <option key={company.companyId} value={company.companyId}>{company.name}</option>)}</select></label>}
    <div className="filterbar" role="tablist" aria-label="Tipo de documento">
      <button type="button" role="tab" aria-selected={category === 'all'} className={`button ${category === 'all' ? 'button-primary' : 'button-secondary'}`} onClick={() => setCategory('all')}>Tudo</button>
      {PORTAL_CATEGORIES.map((item) => <button key={item.key} type="button" role="tab" aria-selected={category === item.key} className={`button ${category === item.key ? 'button-primary' : 'button-secondary'}`} onClick={() => setCategory(item.key)}>{item.label}</button>)}
    </div>
    {error && <p className="save-status error" role="alert">{error}</p>}
    {notice && !error && <p className="save-status success">{notice}</p>}
    {items && shown.length === 0 && <section className="panel empty-state"><h2>Nada por aqui</h2><p>O gabinete ainda não publicou nada {category === 'all' ? '' : 'deste tipo '}para si.</p></section>}
    <section className="panel" aria-label="Enviar documentos ao gabinete">
      <div className="panel-heading"><div><h2>Enviar documentos ao gabinete</h2><p>Faturas, recibos, extratos… Aceitamos PDF, imagens e XML, até 15 MB cada. Os ficheiros chegam ao arquivo digital da empresa.</p></div>
        <button className="button button-primary" type="button" disabled={uploading} onClick={() => fileInput.current?.click()}><Upload size={15} /> {uploading ? 'A enviar…' : 'Escolher documentos'}</button></div>
      <input ref={fileInput} type="file" multiple hidden aria-label="Documentos a enviar" accept=".pdf,.png,.jpg,.jpeg,.webp,.tif,.tiff,.xml" onChange={(event) => void send(event.target.files)} />
      {uploads.length > 0 && <div className="table-wrap"><table><thead><tr><th>Enviados por si</th><th>Data</th><th className="numeric">Tamanho</th></tr></thead>
        <tbody>{uploads.map((upload) => <tr key={upload.id}><td>{upload.fileName}</td><td>{when.format(new Date(upload.uploadedAtUtc))}</td><td className="numeric">{size(upload.sizeBytes)}</td></tr>)}</tbody></table></div>}
    </section>
    {shown.length > 0 && <section className="panel"><div className="table-wrap"><table>
      <thead><tr><th>Ficheiro</th><th>Tipo</th><th>Publicado em</th><th className="numeric">Tamanho</th><th><span className="sr-only">Descarregar</span></th></tr></thead>
      <tbody>{shown.map((item) => <tr key={item.id}>
        <td><FileText size={14} /> {item.title}<small>{item.fileName}</small></td>
        <td>{PORTAL_CATEGORIES.find((entry) => entry.key === item.category)?.label}</td>
        <td>{when.format(new Date(item.publishedAtUtc))}</td>
        <td className="numeric">{size(item.sizeBytes)}</td>
        <td><a className="button button-secondary" href={`/api/v1/portal/items/${item.id}/content`} download={item.fileName}><Download size={15} /> Descarregar</a></td>
      </tr>)}</tbody>
    </table></div></section>}
  </div>
}
