import { Contact, Database, Download, FileUp, Link2, Plus, Receipt, Search, X } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { type PartyImportResult, type PartyRow, type SaftPartyLinkResult } from '../app/demo-data'
import { Pager } from '../app/Pager'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext, type PartyListParams } from '../app/WorkContext'

const ROLE_FILTERS: { value: PartyListParams['role']; label: string }[] = [
  { value: 'Todos', label: 'Todos' },
  { value: 'Clientes', label: 'Clientes' },
  { value: 'Fornecedores', label: 'Fornecedores' },
]

function roleLabel(party: Pick<PartyRow, 'isCustomer' | 'isSupplier'>) {
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
  const [addressDetail, setAddressDetail] = useState(party?.addressDetail ?? '')
  const [city, setCity] = useState(party?.city ?? '')
  const [postalCode, setPostalCode] = useState(party?.postalCode ?? '')
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
      addressDetail: addressDetail.trim() || null,
      city: city.trim() || null,
      postalCode: postalCode.trim() || null,
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
      <label><span>Morada</span><input value={addressDetail} onChange={(event) => setAddressDetail(event.target.value)} maxLength={210} placeholder="Opcional — rua, número, andar" /></label>
      <label><span>Código postal</span><input value={postalCode} onChange={(event) => setPostalCode(event.target.value)} maxLength={20} placeholder="Opcional" /></label>
      <label><span>Localidade</span><input value={city} onChange={(event) => setCity(event.target.value)} maxLength={50} placeholder="Opcional" /></label>
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

const IMPORT_STATE_LABELS: Record<PartyImportResult['items'][number]['state'], string> = {
  New: 'Novo',
  Updated: 'A completar',
  Unchanged: 'Sem alterações',
}

type ImportSource = { kind: 'saft'; file: File } | { kind: 'efatura' }

function PartyImporter({ onImported, onClose }: { onImported: () => void; onClose: () => void }) {
  const { activeCompany, importPartiesFromSaft, importPartiesFromEFatura, linkSaftParties } = useWorkContext()
  const companyId = activeCompany.companyId
  const fileInput = useRef<HTMLInputElement>(null)
  const [source, setSource] = useState<ImportSource | null>(null)
  const [result, setResult] = useState<PartyImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [link, setLink] = useState<SaftPartyLinkResult | null>(null)

  // Depois de os terceiros existirem, liga-os aos lançamentos que já tinham
  // sido importados deste ficheiro (as linhas sem terceiro).
  const runLink = async (file: File, apply: boolean) => {
    setBusy(true)
    setError('')
    try {
      setLink(await linkSaftParties(companyId, file, apply))
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível analisar os lançamentos.')
    } finally {
      setBusy(false)
    }
  }

  const run = async (from: ImportSource, apply: boolean) => {
    setBusy(true)
    setError('')
    setLink(null)
    try {
      const value = from.kind === 'saft'
        ? await importPartiesFromSaft(companyId, from.file, apply)
        : await importPartiesFromEFatura(companyId, apply)
      setSource(from)
      setResult(value)
      if (apply) onImported()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível analisar a origem.')
    } finally {
      setBusy(false)
    }
  }

  const pending = result ? result.newCount + result.updatedCount : 0

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>Importar terceiros</h2><p>Cria os terceiros em falta a partir dos clientes e fornecedores de um SAF-T ou dos emitentes das compras do e-Fatura. Primeiro analisa; só grava quando confirmar.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <div className="builder-row party-import-sources">
      <input ref={fileInput} type="file" accept=".xml,application/xml,text/xml" hidden onChange={(event) => { const file = event.target.files?.[0]; event.target.value = ''; if (file) void run({ kind: 'saft', file }, false) }} />
      <button className="button button-secondary" type="button" disabled={busy} onClick={() => fileInput.current?.click()}><FileUp size={16} /> Analisar um SAF-T…</button>
      <button className="button button-secondary" type="button" disabled={busy} onClick={() => void run({ kind: 'efatura' }, false)}><Receipt size={16} /> Analisar as compras do e-Fatura</button>
      {busy && <span className="catalog-count">A processar…</span>}
    </div>
    {error && <p className="save-status error">{error}</p>}
    {result && source && <>
      <p className={`save-status ${result.applied ? 'success' : ''}`}>
        {source.kind === 'saft' ? source.file.name : 'Compras do e-Fatura'} — {result.sourceCount} na origem: {result.newCount} {result.applied ? 'criados' : 'novos'}, {result.updatedCount} {result.applied ? 'completados' : 'a completar'}, {result.unchangedCount} sem alterações, {result.skippedCount} de fora.
      </p>
      {result.skipped.length > 0 && <ul className="party-import-skipped">{result.skipped.map((item) => <li key={item.reason}>{item.reason}: {item.count}</li>)}</ul>}
      {result.items.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th>Contribuinte</th><th>Nome</th><th>Função</th><th>Conta de cliente</th><th>Conta de fornecedor</th><th>Resultado</th></tr></thead>
        <tbody>{result.items.map((item) => <tr key={`${item.countryCode}-${item.taxRegistrationNumber}`} className={item.state === 'Unchanged' ? 'muted-row' : undefined}>
          <td>{item.countryCode !== 'PT' && `${item.countryCode} `}{item.taxRegistrationNumber}</td>
          <td>{item.name}</td>
          <td>{roleLabel(item)}</td>
          <td>{item.customerAccountCode ?? '—'}</td>
          <td>{item.supplierAccountCode ?? '—'}</td>
          <td><span className={`badge ${item.state === 'New' ? 'success' : item.state === 'Updated' ? 'warning' : 'neutral'}`}>{IMPORT_STATE_LABELS[item.state]}</span></td>
        </tr>)}</tbody>
      </table></div>}
      {result.items.length < result.newCount + result.updatedCount + result.unchangedCount && <p className="catalog-count">A mostrar os primeiros {result.items.length}; a importação abrange todos.</p>}
      {source.kind === 'saft' && (result.applied || pending === 0) && <div className="party-link">
        <p><strong>Lançamentos já importados deste ficheiro.</strong> Se os movimentos foram importados antes de os terceiros existirem, as linhas ficaram sem terceiro. Esta ligação preenche só as linhas sem terceiro, sem alterar valores nem contas.</p>
        {link && <p className={`save-status ${link.applied ? 'success' : ''}`}>
          {link.transactionCount} transações com cliente ou fornecedor: {link.linesLinked} {link.linesLinked === 1 ? 'linha' : 'linhas'} {link.applied ? 'ligadas' : 'a ligar'}, {link.linesAlreadyLinked} já com terceiro; {link.entriesNotFound} sem lançamento importado, {link.entriesChanged} com o lançamento entretanto alterado, {link.transactionsWithUnknownParty} com terceiro que não existe.
        </p>}
        <div className="builder-row">
          <button className="button button-secondary" type="button" disabled={busy} onClick={() => void runLink(source.file, false)}><Link2 size={16} /> Analisar os lançamentos</button>
          {link && !link.applied && link.linesLinked > 0 && <button className="button button-primary" type="button" disabled={busy} onClick={() => void runLink(source.file, true)}>Ligar {link.linesLinked} {link.linesLinked === 1 ? 'linha' : 'linhas'}</button>}
        </div>
      </div>}
      {!result.applied && <footer className="settings-actions">
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="button" disabled={busy || pending === 0} onClick={() => void run(source, true)}><Download size={16} /> {pending === 0 ? 'Nada a importar' : `Importar ${pending} ${pending === 1 ? 'terceiro' : 'terceiros'}`}</button>
      </footer>}
    </>}
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
  const [importing, setImporting] = useState(false)
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
      <section className="panel empty-state"><span className="empty-icon"><Contact /></span><h2>Requer ligação à base de dados</h2><p>Os terceiros são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Terceiros</h1><p>Clientes e fornecedores da empresa, identificados pelo país e pelo número de contribuinte, com a subconta 21/22 a que estão ligados.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar terceiros</span><input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} placeholder="Pesquisar nome ou contribuinte…" /></label>
      <div className="segmented" role="group" aria-label="Filtrar por função">
        {ROLE_FILTERS.map((item) => <button key={item.value} type="button" aria-pressed={role === item.value} className={role === item.value ? 'selected' : ''} onClick={() => { setRole(item.value); setPage(1) }}>{item.label}</button>)}
      </div>
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => { setIncludeInactive(event.target.checked); setPage(1) }} /> Incluir inativos</label>
      <span className="catalog-count">{loading ? 'A carregar…' : `${total} ${total === 1 ? 'terceiro' : 'terceiros'}`}</span>
      <button className="button button-secondary" type="button" onClick={() => setImporting(true)}><Download size={16} /> Importar</button>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo terceiro</button>
    </div>

    {importing && <PartyImporter onImported={() => setReload((value) => value + 1)} onClose={() => setImporting(false)} />}

    {editor?.mode === 'create' && <PartyEditor party={null} onSaved={saved} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <PartyEditor key={editor.party.id} party={editor.party} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Contribuinte</th><th>Nome</th><th>Função</th><th>Conta de cliente</th><th>Conta de fornecedor</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{rows.map((party) => <tr key={party.id}>
          <td><span className="primary-cell">{party.countryCode !== 'PT' && `${party.countryCode} `}{party.taxRegistrationNumber}</span></td>
          <td><Link className="primary-cell" to={`/terceiros/${party.id}`}>{party.name}</Link></td>
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
