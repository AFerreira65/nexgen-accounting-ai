import { Building2, Database, Plus, Users, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { ACCOUNTING_STANDARDS, accountingStandardName } from '../app/accountingStandards'
import { useWorkContext } from '../app/WorkContext'
import { CompanyMembers, saveMembership, type PermissionGroupOption } from './CompanyMembers'
import { AutomaticJobs } from './AutomaticJobs'
import { AtCredentialsPanel } from './AtCredentialsPanel'
import { COMPANY_ROLES, companyRoleLabel } from '../app/companyRoles'

type OrgUser = { userId: string; displayName: string; email: string; mobile?: string | null; phone?: string | null; extension?: string | null; role: string; isActive: boolean; isResponsible?: boolean; isAccountant?: boolean; permissionGroupId?: string | null }
type OrgCompany = { id: string; canManage: boolean; legalName: string; taxRegistrationNumber: string; region: string; baseCurrency: string; isActive: boolean; postingLockDate: string | null; addressDetail?: string | null; city?: string | null; postalCode?: string | null; users: OrgUser[] }
type OrgFirm = { id: string; name: string; slug: string; canCreateCompanies: boolean; companies: OrgCompany[] }

type EditableUser = { userId: string; displayName: string; email: string; mobile?: string | null; phone?: string | null; extension?: string | null; isAccountant?: boolean; isActive: boolean }

const REGION_LABELS: Record<string, string> = { Mainland: 'Continente', Azores: 'Açores', Madeira: 'Madeira' }

async function problem(response: Response) {
  try {
    const body = await response.json() as { errors?: Record<string, string[]> }
    return body.errors ? Object.values(body.errors).flat()[0] ?? `HTTP ${response.status}` : `HTTP ${response.status}`
  } catch { return `HTTP ${response.status}` }
}

export function OrganizationPage() {
  const { source, refreshWorkContext, user: sessionUser } = useWorkContext()
  const database = source === 'database'
  const [firms, setFirms] = useState<OrgFirm[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [revision, setRevision] = useState(0)
  const [groups, setGroups] = useState<PermissionGroupOption[]>([])
  const [editor, setEditor] = useState<
    | { kind: 'company'; company: OrgCompany | null; firmId: string }
    | { kind: 'user'; user: EditableUser | null }
    | null
  >(null)

  const reload = useCallback(() => {
    setRevision((value) => value + 1)
    void refreshWorkContext() // as empresas novas aparecem no seletor de empresa
  }, [refreshWorkContext])

  useEffect(() => {
    if (!database) { setLoading(false); return }
    const controller = new AbortController()
    setLoading(true)
    setLoadError('')
    void fetch('/api/v1/development/organization', { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as OrgFirm[] : Promise.reject(new Error(await problem(response))))
      .then((data) => { setFirms(data); setLoadError('') })
      .catch((fetchError: unknown) => {
        if (controller.signal.aborted) return
        setFirms([])
        setLoadError(fetchError instanceof Error && fetchError.message ? fetchError.message : 'Não foi possível carregar o cadastro. Confirme que a API local está a correr.')
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [database, revision])

  // O que o servidor diz que o utilizador pode fazer; é também o servidor que o garante.
  const creationFirm = firms.find((firm) => firm.canCreateCompanies)
  const managesAny = firms.some((firm) => firm.companies.some((company) => company.canManage))
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
  // A empresa em edição, sempre a partir da lista mais recente (os acessos mudam ao gravar).
  const editingCompany = editor?.kind === 'company' && editor.company ? companies.find((company) => company.id === editor.company?.id) : undefined
  const editingFirmId = editingCompany?.canManage ? editingCompany.firmId : undefined
  useEffect(() => {
    if (!editingFirmId) { setGroups([]); return }
    const controller = new AbortController()
    void fetch(`/api/v1/development/organization/firms/${editingFirmId}/permission-groups`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as PermissionGroupOption[] : [])
      .then((list) => { if (!controller.signal.aborted) setGroups(list) })
      .catch(() => { if (!controller.signal.aborted) setGroups([]) })
    return () => controller.abort()
  }, [editingFirmId, revision])

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Cadastro organizacional</p><h1>Empresas e utilizadores</h1><p>As empresas a que tem acesso e os utilizadores delas. Só o dono ou um administrador de uma empresa a altera e vê quem lhe tem acesso; quem cria uma empresa fica dono dela. A criação de empresa provisiona o plano standard da norma.</p></div>
      <span className={`connection-badge ${database ? 'database' : 'demonstration'}`}><Database size={15} />{database ? 'Ligado à base de dados' : 'Modo de demonstração'}</span>
    </header>

    {!database && <p className="save-status">A gestão do cadastro organizacional requer a ligação à API local.</p>}

    {database && loadError && <section className="panel">
      <p className="save-status error">{loadError}</p>
      <button className="button button-secondary" type="button" onClick={reload}>Tentar novamente</button>
    </section>}

    {editor?.kind === 'company' && <CompanyEditor company={editor.company} firmId={editor.firmId} onDone={() => { setEditor(null); reload() }} onCancel={() => setEditor(null)} />}
    {editingCompany?.canManage && <CompanyMembers
      companyId={editingCompany.id}
      companyName={editingCompany.legalName}
      members={editingCompany.users}
      knownUsers={users}
      groups={groups}
      canGrantOwner={editingCompany.users.some((member) => member.userId === sessionUser.id && member.role === 'Owner' && member.isActive)}
      onChanged={reload}
    />}
    {editingCompany?.canManage && sessionUser.isAccountant && <AtCredentialsPanel
      key={editingCompany.id}
      companyId={editingCompany.id}
      companyName={editingCompany.legalName}
      taxRegistrationNumber={editingCompany.taxRegistrationNumber}
    />}
    {editor?.kind === 'user' && <UserEditor user={editor.user} sessionIsAccountant={sessionUser.isAccountant ?? false} managedCompanies={companies.filter((company) => company.canManage)} onDone={() => { setEditor(null); reload() }} onCancel={() => setEditor(null)} />}

    {!loadError && <section className="panel">
      <div className="panel-heading"><div><h2><Building2 size={17} /> Empresas</h2><p>{loading ? 'A carregar…' : `${companies.length} ${companies.length === 1 ? 'empresa' : 'empresas'}`}</p></div>
        {database && creationFirm && <button className="button button-primary" type="button" onClick={() => setEditor({ kind: 'company', company: null, firmId: creationFirm.id })}><Plus size={16} /> Nova empresa</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Empresa</th><th>NIF</th><th>Região</th><th>Moeda</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {companies.map((company) => <tr key={company.id}><td>{company.legalName}</td><td>{company.taxRegistrationNumber}</td><td>{REGION_LABELS[company.region] ?? company.region}</td><td>{company.baseCurrency}</td><td><span className={`badge ${company.isActive ? 'success' : 'warning'}`}>{company.isActive ? 'Ativa' : 'Inativa'}</span></td><td>{database && company.canManage && <button className="button button-secondary" type="button" onClick={() => setEditor({ kind: 'company', company, firmId: company.firmId })}>Editar</button>}</td></tr>)}
        {!loading && companies.length === 0 && <tr><td colSpan={6} className="empty-state">Ainda não há empresas. Use «Nova empresa» ou importe um SAF-T.</td></tr>}
      </tbody></table></div>
    </section>}

    {!loadError && <section className="panel">
      <div className="panel-heading"><div><h2><Users size={17} /> Utilizadores</h2><p>{users.length} {users.length === 1 ? 'utilizador' : 'utilizadores'}</p></div>
        {database && managesAny && <button className="button button-primary" type="button" onClick={() => setEditor({ kind: 'user', user: null })}><Plus size={16} /> Novo utilizador</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Utilizador</th><th>Email</th><th>Telemóvel</th><th>Telefone</th><th>Ext.</th><th className="numeric">Empresas</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {users.map((user) => <tr key={user.userId}><td>{user.displayName}</td><td>{user.email}</td><td>{user.mobile ?? '—'}</td><td>{user.phone ?? '—'}</td><td>{user.extension ?? '—'}</td><td className="numeric strong">{user.companies}</td><td><span className={`badge ${user.isActive ? 'success' : 'warning'}`}>{user.isActive ? 'Ativo' : 'Inativo'}</span></td><td>{database && <button className="button button-secondary" type="button" onClick={() => setEditor({ kind: 'user', user: { userId: user.userId, displayName: user.displayName, email: user.email, mobile: user.mobile, phone: user.phone, extension: user.extension, isAccountant: user.isAccountant, isActive: user.isActive } })}>Editar</button>}</td></tr>)}
        {!loading && users.length === 0 && <tr><td colSpan={8} className="empty-state">Ainda não há utilizadores.</td></tr>}
      </tbody></table></div>
    </section>}

    {database && <AutomaticJobs />}
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
  const [addressDetail, setAddressDetail] = useState(company?.addressDetail ?? '')
  const [city, setCity] = useState(company?.city ?? '')
  const [postalCode, setPostalCode] = useState(company?.postalCode ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true); setError('')
    try {
      const response = isCreate
        ? await fetch('/api/v1/development/organization/companies', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ accountingFirmId: firmId, legalName, taxRegistrationNumber, taxRegion, accountingStandard, baseCurrency }) })
        : await fetch(`/api/v1/development/organization/companies/${company.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ legalName, taxRegion, baseCurrency, isActive, postingLockDate: postingLockDate || null, addressDetail, city, postalCode }) })
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
        {ACCOUNTING_STANDARDS.map((standard) => <option key={standard} value={standard}>{accountingStandardName(standard)}</option>)}
      </select>
    </label> : null}
    <label><span>Moeda base</span><input value={baseCurrency} maxLength={3} onChange={(event) => setBaseCurrency(event.target.value)} /></label>
    {isCreate ? null : <>
      <label><span>Morada da sede</span><input value={addressDetail} onChange={(event) => setAddressDetail(event.target.value)} maxLength={210} placeholder="Rua, número, andar" /></label>
      <label><span>Código postal</span><input value={postalCode} onChange={(event) => setPostalCode(event.target.value)} maxLength={8} placeholder="0000-000" /></label>
      <label><span>Localidade</span><input value={city} onChange={(event) => setCity(event.target.value)} maxLength={50} /></label>
    </>}
    {isCreate ? null : <label><span>Data de bloqueio de lançamentos</span><input type="date" value={postingLockDate} onChange={(event) => setPostingLockDate(event.target.value)} /></label>}
    {isCreate ? null : <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Empresa ativa</strong></span></label>}
  </EditorShell>
}

function UserEditor({ user, managedCompanies, sessionIsAccountant, onDone, onCancel }: { user: EditableUser | null; sessionIsAccountant: boolean; managedCompanies: { id: string; legalName: string }[]; onDone: () => void; onCancel: () => void }) {
  const isCreate = user === null
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [mobile, setMobile] = useState(user?.mobile ?? '')
  const [phone, setPhone] = useState(user?.phone ?? '')
  const [extension, setExtension] = useState(user?.extension ?? '')
  const [isAccountant, setIsAccountant] = useState(user?.isAccountant ?? false)
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  // Um utilizador novo sem acesso a nenhuma empresa não aparece na lista: dá-se logo o primeiro.
  const [grantCompanyId, setGrantCompanyId] = useState(managedCompanies[0]?.id ?? '')
  const [grantRole, setGrantRole] = useState('Accountant')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const firstField = useRef<HTMLInputElement>(null)
  useEffect(() => { firstField.current?.focus() }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true); setError('')
    try {
      const response = isCreate
        ? await fetch('/api/v1/development/organization/users', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ displayName, email, mobile, phone, extension, isAccountant }) })
        : await fetch(`/api/v1/development/organization/users/${user.userId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ displayName, email, isActive, mobile, phone, extension, isAccountant }) })
      if (!response.ok) throw new Error(await problem(response))
      if (isCreate && grantCompanyId) {
        const created = await response.json() as { id: string }
        await saveMembership(grantCompanyId, created.id, { role: grantRole, isActive: true, isResponsible: false })
      }
      onDone()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o utilizador.')
      setSaving(false)
    }
  }

  return <EditorShell title={isCreate ? 'Novo utilizador' : `Editar ${user.displayName}`} error={error} saving={saving} onCancel={onCancel} onSubmit={submit}>
    <label><span>Nome</span><input ref={firstField} value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
    <label><span>Email</span><input type="email" value={email} onChange={(event) => setEmail(event.target.value)} /></label>
    <label><span>Telemóvel</span><input type="tel" value={mobile} maxLength={32} onChange={(event) => setMobile(event.target.value)} /></label>
    <label><span>Telefone</span><input type="tel" value={phone} maxLength={32} onChange={(event) => setPhone(event.target.value)} /></label>
    <label><span>Extensão</span><input value={extension} maxLength={16} onChange={(event) => setExtension(event.target.value)} /></label>
    <label className="toggle-field"><input type="checkbox" checked={isAccountant} disabled={!sessionIsAccountant} onChange={(event) => setIsAccountant(event.target.checked)} /><span><strong>Contabilista</strong><small>Tem todas as permissões e é o único que vê e usa as senhas da AT (IES, Modelo 22). Só um contabilista altera esta opção.</small></span></label>
    {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Utilizador ativo</strong></span></label>}
    {isCreate && managedCompanies.length > 0 && <label><span>Dar acesso a</span><select value={grantCompanyId} onChange={(event) => setGrantCompanyId(event.target.value)}><option value="">Nenhuma empresa por agora</option>{managedCompanies.map((company) => <option key={company.id} value={company.id}>{company.legalName}</option>)}</select></label>}
    {isCreate && grantCompanyId && <label><span>Função</span><select value={grantRole} onChange={(event) => setGrantRole(event.target.value)}>{COMPANY_ROLES.filter((role) => role !== 'Owner').map((role) => <option key={role} value={role}>{companyRoleLabel(role)}</option>)}</select></label>}
  </EditorShell>
}
