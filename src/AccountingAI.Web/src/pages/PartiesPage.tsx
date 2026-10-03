import { Contact, Database, Plus, Search, X } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { type PartyRow } from '../app/demo-data'
import { Pager } from '../app/Pager'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext, type PartyListParams } from '../app/WorkContext'

const ROLE_FILTERS: { value: PartyListParams['role']; label: string }[] = [
  { value: 'Todos', label: 'Todos' },
  { value: 'Clientes', label: 'Clientes' },
  { value: 'Fornecedores', label: 'Fornecedores' },
]

function roleLabel(party: PartyRow) {
  if (party.isCustomer && party.isSupplier) return 'Cliente e fornecedor'
  if (party.isCustomer) return 'Cliente'
  if (party.isSupplier) return 'Fornecedor'
  return 'Outro'
}

function PartyEditor({ party, onSaved, onClose }: { party: PartyRow | null; onSaved: () => void; onClose: () => void }) {
  const { activeCompany, accountingSetup, createParty, updateParty } = useWorkContext()
  const isCreate = party === null
  const [countryCode, setCountryCode] = useState('PT')
  const [taxRegistrationNumber, setTaxRegistrationNumber] = useState('')
  const [name, setName] = useState(party?.name ?? '')
  const [isCustomer, setIsCustomer] = useState(party?.isCustomer ?? true)
  const [isSupplier, setIsSupplier] = useState(party?.isSupplier ?? false)
  const [customerAccountCode, setCustomerAccountCode] = useState(party?.customerAccountCode ?? '')
  const [supplierAccountCode, setSupplierAccountCode] = useState(party?.supplierAccountCode ?? '')
  const [isActive, setIsActive] = useState(party?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  const posting = accountingSetup.accounts.filter((account) => account.allowsPosting && account.isActive)
  const customerAccounts = posting.filter((account) => account.code.startsWith('21'))
  const supplierAccounts = posting.filter((account) => account.code.startsWith('22'))

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    const input = {
      name,
      isCustomer,
      isSupplier,
      customerAccountCode: customerAccountCode.trim() || null,
      supplierAccountCode: supplierAccountCode.trim() || null,
      isActive,
    }
    try {
      if (isCreate) await createParty(activeCompany.companyId, { ...input, countryCode, taxRegistrationNumber })
      else await updateParty(activeCompany.companyId, party.id, input)
      onSaved()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o terceiro.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Novo terceiro' : `Editar ${party.name}`}</h2><p>{isCreate ? 'O país e o número de contribuinte identificam o terceiro e não se alteram depois.' : `${party.countryCode} ${party.taxRegistrationNumber}`}</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <>
        <label><span>País</span><input value={countryCode} onChange={(event) => setCountryCode(event.target.value.toUpperCase())} maxLength={2} placeholder="PT" /></label>
        <label><span>Número de contribuinte</span><input value={taxRegistrationNumber} onChange={(event) => setTaxRegistrationNumber(event.target.value)} maxLength={30} placeholder="ex.: 500584095" autoFocus /></label>
      </>}
      <label><span>Nome</span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={200} placeholder="Nome ou designação social" /></label>
      <label className="toggle-field"><input type="checkbox" checked={isCustomer} onChange={(event) => setIsCustomer(event.target.checked)} /><span><strong>Cliente</strong><small>Aparece nas vendas e na declaração recapitulativa.</small></span></label>
      <label className="toggle-field"><input type="checkbox" checked={isSupplier} onChange={(event) => setIsSupplier(event.target.checked)} /><span><strong>Fornecedor</strong><small>Aparece nas compras e nas retenções.</small></span></label>
      <label><span>Subconta de cliente</span><input list="party-customer-accounts" value={customerAccountCode} onChange={(event) => setCustomerAccountCode(event.target.value)} placeholder="Opcional — ex.: 21111" /></label>
      <label><span>Subconta de fornecedor</span><input list="party-supplier-accounts" value={supplierAccountCode} onChange={(event) => setSupplierAccountCode(event.target.value)} placeholder="Opcional — ex.: 22111" /></label>
      <datalist id="party-customer-accounts">{customerAccounts.slice(0, 500).map((account) => <option key={account.code} value={account.code}>{account.name}</option>)}</datalist>
      <datalist id="party-supplier-accounts">{supplierAccounts.slice(0, 500).map((account) => <option key={account.code} value={account.code}>{account.name}</option>)}</datalist>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Terceiro ativo</strong><small>Desative para o retirar das novas seleções.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar terceiro'}</button>
      </footer>
    </form>
  </section>
}

export function PartiesPage() {
  const { activeCompany, source, parties } = useWorkContext()
  const companyId = activeCompany.companyId
  const pageSize = activeCompany.settings.pageSize || 50
  const onDatabase = source === 'database'

  const [search, setSearch] = useState('')
  const [role, setRole] = useState<PartyListParams['role']>('Todos')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [page, setPage] = useState(1)
  const [rows, setRows] = useState<PartyRow[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; party: PartyRow } | null>(null)
  usePublishRecord(editor?.mode === 'edit' ? 'terceiros' : undefined, editor?.mode === 'edit' ? editor.party.id : undefined, editor?.mode === 'edit' ? editor.party.name : undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); setTotal(0); return }
    const controller = new AbortController()
    setLoading(true)
    setError('')
    // A pesquisa espera um instante para não pedir uma página por tecla.
    const timer = window.setTimeout(() => {
      void parties(companyId, { search: search.trim(), role, includeInactive, page, pageSize })
        .then((result) => { if (!controller.signal.aborted) { setRows(result.items); setTotal(result.total) } })
        .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os terceiros.') })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }, 200)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [companyId, onDatabase, parties, search, role, includeInactive, page, pageSize, reload])

  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const saved = () => { setEditor(null); setReload((value) => value + 1) }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Terceiros</h1><p>Clientes e fornecedores da empresa, identificados pelo número de contribuinte.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Contact /></span><h2>Requer ligação ao SQL Server</h2><p>Os terceiros são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Terceiros</h1><p>Clientes e fornecedores da empresa, identificados pelo país e pelo número de contribuinte, com a subconta 21/22 a que estão ligados.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar terceiros</span><input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} placeholder="Pesquisar nome ou contribuinte…" /></label>
      <div className="segmented" role="group" aria-label="Filtrar por função">
        {ROLE_FILTERS.map((item) => <button key={item.value} type="button" aria-pressed={role === item.value} className={role === item.value ? 'selected' : ''} onClick={() => { setRole(item.value); setPage(1) }}>{item.label}</button>)}
      </div>
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => { setIncludeInactive(event.target.checked); setPage(1) }} /> Incluir inativos</label>
      <span className="catalog-count">{loading ? 'A carregar…' : `${total} ${total === 1 ? 'terceiro' : 'terceiros'}`}</span>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo terceiro</button>
    </div>

    {editor?.mode === 'create' && <PartyEditor party={null} onSaved={saved} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <PartyEditor key={editor.party.id} party={editor.party} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Contribuinte</th><th>Nome</th><th>Função</th><th>Conta de cliente</th><th>Conta de fornecedor</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{rows.map((party) => <tr key={party.id}>
          <td><span className="primary-cell">{party.countryCode !== 'PT' && `${party.countryCode} `}{party.taxRegistrationNumber}</span></td>
          <td>{party.name}</td>
          <td>{roleLabel(party)}</td>
          <td>{party.customerAccountCode ? <Link className="primary-cell" to={`/contas/${party.customerAccountCode}`}>{party.customerAccountCode}</Link> : <span className="muted-value">—</span>}</td>
          <td>{party.supplierAccountCode ? <Link className="primary-cell" to={`/contas/${party.supplierAccountCode}`}>{party.supplierAccountCode}</Link> : <span className="muted-value">—</span>}</td>
          <td><span className={`badge ${party.isActive ? 'success' : 'warning'}`}>{party.isActive ? 'Ativo' : 'Inativo'}</span></td>
          <td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', party })}>Editar</button></td>
        </tr>)}</tbody>
      </table></div>
      {!loading && rows.length === 0 && <p className="empty-state">{search || role !== 'Todos' ? 'Nenhum terceiro corresponde ao filtro.' : 'Ainda não há terceiros. Crie o primeiro com «Novo terceiro».'}</p>}
      <Pager currentPage={Math.min(page, totalPages)} totalPages={totalPages} onPage={setPage} />
    </section>
  </div>
}
