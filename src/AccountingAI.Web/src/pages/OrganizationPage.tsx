import { Building2, Database, Plus, Users, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useWorkContext } from '../app/WorkContext'

type OrgUser = { userId: string; displayName: string; email: string; role: string; isActive: boolean }
type OrgCompany = { id: string; legalName: string; taxRegistrationNumber: string; region: string; baseCurrency: string; isActive: boolean; postingLockDate: string | null; users: OrgUser[] }
type OrgFirm = { id: string; name: string; slug: string; companies: OrgCompany[] }

const REGION_LABELS: Record<string, string> = { Mainland: 'Continente', Azores: 'Açores', Madeira: 'Madeira' }
const STANDARDS = ['SncGeneral', 'SncSmallEntities', 'SncMicroEntities', 'SncNonProfit', 'IfrsEu'] as const

async function problem(response: Response) {
  try {
    const body = await response.json() as { errors?: Record<string, string[]> }
    return body.errors ? Object.values(body.errors).flat()[0] ?? `HTTP ${response.status}` : `HTTP ${response.status}`
  } catch { return `HTTP ${response.status}` }
}

export function OrganizationPage() {
  const { source } = useWorkContext()
  const database = source === 'database'
  const [firms, setFirms] = useState<OrgFirm[]>([])
  const [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0)
  const [editor, setEditor] = useState<
    | { kind: 'company'; company: OrgCompany | null; firmId: string }
    | { kind: 'user'; user: { userId: string; displayName: string; email: string; isActive: boolean } | null }
    | null
  >(null)

  const reload = useCallback(() => setRevision((value) => value + 1), [])

  useEffect(() => {
    if (!database) { setLoading(false); return }
    const controller = new AbortController()
    setLoading(true)
    void fetch('/api/v1/development/organization', { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as OrgFirm[] : Promise.reject(new Error()))
      .then((data) => setFirms(data))
      .catch(() => { if (!controller.signal.aborted) setFirms([]) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [database, revision])

  const companies = useMemo(() => firms.flatMap((firm) => firm.companies.map((company) => ({ ...company, firmId: firm.id }))), [firms])
  const users = useMemo(() => {
    const map = new Map<string, OrgUser & { companies: number }>()
    for (const company of companies) {
      for (const member of company.users) {
        const existing = map.get(member.userId)
        map.set(member.userId, existing ? { ...existing, companies: existing.companies + 1 } : { ...member, companies: 1 })
      }
    }
    return [...map.values()].sort((a, b) => a.displayName.localeCompare(b.displayName, 'pt-PT'))
  }, [companies])

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Cadastro organizacional</p><h1>Empresas e utilizadores</h1><p>Gabinetes, empresas e associações de utilizadores. A criação de empresa provisiona o plano standard da norma.</p></div>
      <span className={`connection-badge ${database ? 'database' : 'demonstration'}`}><Database size={15} />{database ? 'Ligado ao SQL Server' : 'Modo de demonstração'}</span>
    </header>

    {!database && <p className="save-status">A gestão do cadastro organizacional requer a ligação à API local.</p>}

    {editor?.kind === 'company' && <CompanyEditor company={editor.company} firmId={editor.firmId} onDone={() => { setEditor(null); reload() }} onCancel={() => setEditor(null)} />}
    {editor?.kind === 'user' && <UserEditor user={editor.user} onDone={() => { setEditor(null); reload() }} onCancel={() => setEditor(null)} />}

    <section className="panel">
      <div className="panel-heading"><div><h2><Building2 size={17} /> Empresas</h2><p>{loading ? 'A carregar…' : `${companies.length} empresas`}</p></div>
        {database && firms[0] && <button className="button button-primary" type="button" onClick={() => setEditor({ kind: 'company', company: null, firmId: firms[0].id })}><Plus size={16} /> Nova empresa</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Empresa</th><th>NIF</th><th>Região</th><th>Moeda</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {companies.map((company) => <tr key={company.id}><td>{company.legalName}</td><td>{company.taxRegistrationNumber}</td><td>{REGION_LABELS[company.region] ?? company.region}</td><td>{company.baseCurrency}</td><td><span className={`badge ${company.isActive ? 'success' : 'warning'}`}>{company.isActive ? 'Ativa' : 'Inativa'}</span></td><td>{database && <button className="button button-secondary" type="button" onClick={() => setEditor({ kind: 'company', company, firmId: company.firmId })}>Editar</button>}</td></tr>)}
      </tbody></table></div>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2><Users size={17} /> Utilizadores</h2><p>{users.length} utilizadores</p></div>
        {database && <button className="button button-primary" type="button" onClick={() => setEditor({ kind: 'user', user: null })}><Plus size={16} /> Novo utilizador</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Utilizador</th><th>Email</th><th className="numeric">Empresas</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {users.map((user) => <tr key={user.userId}><td>{user.displayName}</td><td>{user.email}</td><td className="numeric strong">{user.companies}</td><td><span className={`badge ${user.isActive ? 'success' : 'warning'}`}>{user.isActive ? 'Ativo' : 'Inativo'}</span></td><td>{database && <button className="button button-secondary" type="button" onClick={() => setEditor({ kind: 'user', user: { userId: user.userId, displayName: user.displayName, email: user.email, isActive: user.isActive } })}>Editar</button>}</td></tr>)}
      </tbody></table></div>
    </section>
  </div>
}

function EditorShell({ title, error, saving, onCancel, onSubmit, children }: {
  title: string; error: string; saving: boolean; onCancel: () => void; onSubmit: (event: FormEvent) => void; children: ReactNode
}) {
  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>{title}</h2></div><button className="icon-button" type="button" aria-label="Fechar" onClick={onCancel}><X size={18} /></button></div>
    <form className="settings-grid" onSubmit={onSubmit}>
      {children}
      <footer className="settings-actions">
        {error && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onCancel}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={saving}>{saving ? 'A guardar…' : 'Guardar'}</button>
      </footer>
    </form>
  </section>
}

function CompanyEditor({ company, firmId, onDone, onCancel }: { company: OrgCompany | null; firmId: string; onDone: () => void; onCancel: () => void }) {
  const isCreate = company === null
  const [legalName, setLegalName] = useState(company?.legalName ?? '')
  const [taxRegistrationNumber, setTaxRegistrationNumber] = useState(company?.taxRegistrationNumber ?? '')
  const [taxRegion, setTaxRegion] = useState(company?.region ?? 'Mainland')
  const [accountingStandard, setAccountingStandard] = useState<string>('SncGeneral')
  const [baseCurrency, setBaseCurrency] = useState(company?.baseCurrency ?? 'EUR')
  const [isActive, setIsActive] = useState(company?.isActive ?? true)
  const [postingLockDate, setPostingLockDate] = useState(company?.postingLockDate ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true); setError('')
    try {
      const response = isCreate
        ? await fetch('/api/v1/development/organization/companies', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ accountingFirmId: firmId, legalName, taxRegistrationNumber, taxRegion, accountingStandard, baseCurrency }) })
        : await fetch(`/api/v1/development/organization/companies/${company.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ legalName, taxRegion, baseCurrency, isActive, postingLockDate: postingLockDate || null }) })
      if (!response.ok) throw new Error(await problem(response))
      onDone()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar a empresa.')
      setSaving(false)
    }
  }

  return <EditorShell title={isCreate ? 'Nova empresa' : `Editar ${company.legalName}`} error={error} saving={saving} onCancel={onCancel} onSubmit={submit}>
    <label><span>Designação</span><input value={legalName} onChange={(event) => setLegalName(event.target.value)} /></label>
    {isCreate ? <label><span>NIF</span><input value={taxRegistrationNumber} onChange={(event) => setTaxRegistrationNumber(event.target.value)} /></label> : null}
    <label><span>Região fiscal</span>
      <select value={taxRegion} onChange={(event) => setTaxRegion(event.target.value)}>
        {Object.entries(REGION_LABELS).map(([regionValue, regionLabel]) => <option key={regionValue} value={regionValue}>{regionLabel}</option>)}
      </select>
    </label>
    {isCreate ? <label><span>Norma contabilística</span>
      <select value={accountingStandard} onChange={(event) => setAccountingStandard(event.target.value)}>
        {STANDARDS.map((standard) => <option key={standard} value={standard}>{standard}</option>)}
      </select>
    </label> : null}
    <label><span>Moeda base</span><input value={baseCurrency} maxLength={3} onChange={(event) => setBaseCurrency(event.target.value)} /></label>
    {isCreate ? null : <label><span>Data de bloqueio de lançamentos</span><input type="date" value={postingLockDate} onChange={(event) => setPostingLockDate(event.target.value)} /></label>}
    {isCreate ? null : <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Empresa ativa</strong></span></label>}
  </EditorShell>
}

function UserEditor({ user, onDone, onCancel }: { user: { userId: string; displayName: string; email: string; isActive: boolean } | null; onDone: () => void; onCancel: () => void }) {
  const isCreate = user === null
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const firstField = useRef<HTMLInputElement>(null)
  useEffect(() => { firstField.current?.focus() }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true); setError('')
    try {
      const response = isCreate
        ? await fetch('/api/v1/development/organization/users', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ displayName, email }) })
        : await fetch(`/api/v1/development/organization/users/${user.userId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ displayName, email, isActive }) })
      if (!response.ok) throw new Error(await problem(response))
      onDone()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o utilizador.')
      setSaving(false)
    }
  }

  return <EditorShell title={isCreate ? 'Novo utilizador' : `Editar ${user.displayName}`} error={error} saving={saving} onCancel={onCancel} onSubmit={submit}>
    <label><span>Nome</span><input ref={firstField} value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
    <label><span>Email</span><input type="email" value={email} onChange={(event) => setEmail(event.target.value)} /></label>
    {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Utilizador ativo</strong></span></label>}
  </EditorShell>
}
