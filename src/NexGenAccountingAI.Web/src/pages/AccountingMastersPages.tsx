import { BookOpen, CircleAlert, CircleCheck, FileText, Landmark, Plus, Search, Tags, X } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import type { AccountingAccount, AccountingDocumentType, AccountingJournal } from '../app/demo-data'
import { Pager, usePagedList } from '../app/Pager'
import { useWorkContext } from '../app/WorkContext'

function useFiltered<T>(items: T[], text: string, value: (item: T) => string) {
  return useMemo(() => {
    const normalized = text.trim().toLocaleLowerCase('pt-PT')
    return normalized ? items.filter((item) => value(item).toLocaleLowerCase('pt-PT').includes(normalized)) : items
  }, [items, text, value])
}

function SourceBadge({ database }: { database: boolean }) {
  return <span className={`connection-badge ${database ? 'database' : 'demonstration'}`}>{database ? 'Dados da BD' : 'Dados de demonstração'}</span>
}

const TAXONOMY_REFERENCES = ['S', 'M', 'N', 'O'] as const

function AccountEditor({ account, defaultReference, aggregators, onClose }: {
  account: AccountingAccount | null
  defaultReference: string
  aggregators: AccountingAccount[]
  onClose: () => void
}) {
  const { createAccount, updateAccount, taxonomyCatalog } = useWorkContext()
  const isCreate = account === null
  const [code, setCode] = useState('')
  const [name, setName] = useState(account?.name ?? '')
  const [parentCode, setParentCode] = useState(account?.parentCode ?? '')
  const [allowsPosting, setAllowsPosting] = useState(account?.allowsPosting ?? true)
  const [isActive, setIsActive] = useState(account?.isActive ?? true)
  const [reference, setReference] = useState(account?.taxonomyReference ?? defaultReference)
  const [taxonomyCode, setTaxonomyCode] = useState(account?.taxonomyCode?.toString() ?? '')
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      if (isCreate) {
        await createAccount({ code, name, parentCode: parentCode || null, allowsPosting })
      } else {
        const parsedCode = taxonomyCode.trim() ? Number(taxonomyCode.trim()) : null
        if (parsedCode !== null && (!Number.isInteger(parsedCode) || parsedCode <= 0)) {
          throw new Error('O código de taxonomia tem de ser um inteiro positivo.')
        }
        await updateAccount(account.code, {
          name,
          isActive,
          taxonomyReference: parsedCode === null ? null : reference,
          taxonomyCode: parsedCode,
        })
      }
      onClose()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar a conta.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Nova conta da empresa' : `Editar conta ${account.code}`}</h2><p>{isCreate ? 'A conta é criada apenas nesta empresa e não altera o plano base.' : 'A renomeação e a taxonomia aplicam-se de imediato; a desativação exige que não existam movimentos.'}</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value)} placeholder="ex.: 6811" autoFocus /></label>}
      <label><span>Descrição</span><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Descrição da conta" /></label>
      {isCreate
        ? <>
            <label><span>Conta-mãe</span><select value={parentCode} onChange={(event) => setParentCode(event.target.value)}><option value="">Sem conta-mãe</option>{aggregators.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
            <label><span>Tipo</span><select value={allowsPosting ? 'posting' : 'aggregate'} onChange={(event) => setAllowsPosting(event.target.value === 'posting')}><option value="posting">Movimento</option><option value="aggregate">Agregadora</option></select></label>
          </>
        : <>
            <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Conta ativa</strong><small>Desative para retirar a conta das novas seleções.</small></span></label>
            {account.allowsPosting && <>
              <label><span>Referencial</span><select value={reference} onChange={(event) => setReference(event.target.value)} disabled={!taxonomyCode.trim()}>{TAXONOMY_REFERENCES.map((item) => <option key={item} value={item}>{item}</option>)}</select></label>
              <label><span>Código de taxonomia</span><input type="number" min="1" list="taxonomy-codes" value={taxonomyCode} onChange={(event) => setTaxonomyCode(event.target.value)} placeholder="Vazio = por atribuir" />
                <small>{taxonomyCode.trim()
                  ? (taxonomyCatalog.codes.find((item) => item.code === Number(taxonomyCode))?.description ?? 'Código não encontrado no referencial ' + taxonomyCatalog.reference)
                  : `Catálogo ${taxonomyCatalog.reference}: ${taxonomyCatalog.codes.length} códigos`}</small>
              </label>
              <datalist id="taxonomy-codes">{taxonomyCatalog.codes.slice(0, 800).map((item) => <option key={item.code} value={item.code}>{item.description}</option>)}</datalist>
            </>}
          </>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar conta'}</button>
      </footer>
    </form>
  </section>
}

export function AccountsPage() {
  const { activeCompany, accountingSetup, setupSource, setupLoading } = useWorkContext()
  const [search, setSearch] = useState('')
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; account: AccountingAccount } | null>(null)
  const accounts = useFiltered(accountingSetup.accounts, search, (item) => `${item.code} ${item.name} ${item.taxonomyCode ?? ''}`)
  const { paged, currentPage, totalPages, setPage } = usePagedList(accounts, search, activeCompany.settings.pageSize || 50)
  const posting = accountingSetup.accounts.filter((item) => item.allowsPosting)
  const classified = posting.filter((item) => item.taxonomyCode !== null)
  const aggregators = accountingSetup.accounts.filter((item) => !item.allowsPosting)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Plano e abertura de contas</h1><p>Estrutura própria da empresa, criada a partir do referencial {accountingSetup.template.code}.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <section className="metric-grid compact-grid"><article className="metric-card"><span className="metric-icon teal"><BookOpen /></span><div><small>Contas ativas</small><strong>{accountingSetup.accounts.filter((item) => item.isActive).length}</strong><span>{posting.length} contas de movimento</span></div></article><article className="metric-card"><span className="metric-icon blue"><Tags /></span><div><small>Taxonomias atribuídas</small><strong>{classified.length}</strong><span>Referencial {accountingSetup.template.taxonomyReference}</span></div></article></section>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar código, descrição ou taxonomia…" /></label><span className="catalog-count">{setupLoading ? 'A carregar…' : `${accounts.length} contas`}</span><button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Nova conta</button></div>
    {editor?.mode === 'create' && <AccountEditor account={null} defaultReference={accountingSetup.template.taxonomyReference} aggregators={aggregators} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <AccountEditor account={editor.account} defaultReference={accountingSetup.template.taxonomyReference} aggregators={aggregators} onClose={() => setEditor(null)} />}
    <section className="panel"><div className="panel-heading"><div><h2>Contas da empresa</h2><p>As contas agregadoras não podem receber movimentos nem taxonomia.</p></div></div><div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th>Agregadora</th><th>Tipo</th><th>Taxonomia</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{paged.map((account) => <tr key={account.code}><td><Link className="primary-cell" to={`/contas/${account.code}`}>{account.code}</Link></td><td>{account.name}<small>{account.sourceTemplateVersion ? 'Plano base' : 'Configuração da empresa'}</small></td><td>{account.parentCode ?? '—'}</td><td>{account.allowsPosting ? 'Movimento' : 'Agregadora'}</td><td>{account.taxonomyCode ? <span className="taxonomy-pill">{account.taxonomyReference} · {account.taxonomyCode}</span> : <span className="muted-value">Por atribuir</span>}</td><td><span className={`badge ${account.isActive ? 'success' : 'warning'}`}>{account.isActive ? 'Ativa' : 'Inativa'}</span></td><td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', account })}>Editar</button></td></tr>)}</tbody></table></div><Pager currentPage={currentPage} totalPages={totalPages} onPage={setPage} /></section>
  </div>
}

function JournalEditor({ journal, onClose }: { journal: AccountingJournal | null; onClose: () => void }) {
  const { createJournal, updateJournal } = useWorkContext()
  const isCreate = journal === null
  const [code, setCode] = useState('')
  const [name, setName] = useState(journal?.name ?? '')
  const [isActive, setIsActive] = useState(journal?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      if (isCreate) await createJournal({ code, name })
      else await updateJournal(journal.code, { name, isActive })
      onClose()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o diário.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>{isCreate ? 'Novo diário' : `Editar diário ${journal.code}`}</h2><p>{isCreate ? 'O código é único dentro da empresa.' : 'Um diário com movimentos ou usado como predefinido não pode ser desativado.'}</p></div><button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button></div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value)} placeholder="ex.: AUX" autoFocus /></label>}
      <label><span>Descrição</span><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Descrição do diário" /></label>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Diário ativo</strong><small>Desative para o retirar das novas seleções.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar diário'}</button>
      </footer>
    </form>
  </section>
}

export function JournalsPage() {
  const { accountingSetup, activeCompany, movements, setupSource, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; journal: AccountingJournal } | null>(null)
  const journals = useFiltered(accountingSetup.journals, search, (item) => `${item.code} ${item.name}`)

  // A contagem de movimentos por diário vem de um pedido agregado quando a
  // empresa está ligada à base de dados; em demonstração conta no array.
  const onDatabase = setupSource === 'database'
  const [counts, setCounts] = useState<Record<string, number> | null>(null)
  useEffect(() => {
    if (!onDatabase) { setCounts(null); return }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/journal-movement-counts`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as Record<string, number>
      })
      .then((body) => setCounts(body))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setCounts(null)
      })
    return () => controller.abort()
  }, [onDatabase, activeCompany.companyId, user.id])
  const countFor = (code: string) => counts ? counts[code] ?? 0 : movements.filter((item) => item.journalId === code).length

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Diários</h1><p>Diários operacionais da empresa e respetiva utilização nos movimentos.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar diários</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar código ou descrição…" /></label><span className="catalog-count">{journals.length} diários</span><button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo diário</button></div>
    {editor?.mode === 'create' && <JournalEditor journal={null} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <JournalEditor journal={editor.journal} onClose={() => setEditor(null)} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Diário</th><th>Descrição</th><th className="numeric">Movimentos</th><th>Origem</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{journals.map((journal) => <tr key={journal.code}><td><Link className="inline-link" to={`/diarios/${journal.code}`}><Landmark size={14} /> {journal.code}</Link></td><td>{journal.name}</td><td className="numeric strong">{countFor(journal.code)}</td><td>{journal.sourceTemplateVersion ? 'Tabela predefinida' : 'Empresa'}</td><td><span className={`badge ${journal.isActive ? 'success' : 'warning'}`}>{journal.isActive ? 'Ativo' : 'Inativo'}</span></td><td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', journal })}>Editar</button></td></tr>)}</tbody></table></div></section>
  </div>
}

function DocumentTypeEditor({ documentType, journals, onClose }: { documentType: AccountingDocumentType | null; journals: AccountingJournal[]; onClose: () => void }) {
  const { createDocumentType, updateDocumentType } = useWorkContext()
  const isCreate = documentType === null
  const activeJournals = journals.filter((item) => item.isActive || item.code === documentType?.defaultJournalCode)
  const [code, setCode] = useState('')
  const [name, setName] = useState(documentType?.name ?? '')
  const [defaultJournalCode, setDefaultJournalCode] = useState(documentType?.defaultJournalCode ?? activeJournals[0]?.code ?? '')
  const [isActive, setIsActive] = useState(documentType?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      if (isCreate) await createDocumentType({ code, name, defaultJournalCode })
      else await updateDocumentType(documentType.code, { name, defaultJournalCode, isActive })
      onClose()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o documento.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>{isCreate ? 'Novo tipo de documento' : `Editar documento ${documentType.code}`}</h2><p>O diário predefinido tem de existir e estar ativo na empresa.</p></div><button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button></div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value)} placeholder="ex.: FT" autoFocus /></label>}
      <label><span>Descrição</span><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Descrição do documento" /></label>
      <label><span>Diário predefinido</span><select value={defaultJournalCode} onChange={(event) => setDefaultJournalCode(event.target.value)}>{activeJournals.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Documento ativo</strong><small>Desative para o retirar das novas seleções.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar documento'}</button>
      </footer>
    </form>
  </section>
}

export function DocumentTypesPage() {
  const { accountingSetup, setupSource } = useWorkContext()
  const [search, setSearch] = useState('')
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; documentType: AccountingDocumentType } | null>(null)
  const documents = useFiltered(accountingSetup.documentTypes, search, (item) => `${item.code} ${item.name} ${item.defaultJournalCode}`)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Documentos contabilísticos</h1><p>Tipos de documento e diário sugerido para o lançamento.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar documentos</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar documento ou diário…" /></label><span className="catalog-count">{documents.length} documentos</span><button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo documento</button></div>
    {editor?.mode === 'create' && <DocumentTypeEditor documentType={null} journals={accountingSetup.journals} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <DocumentTypeEditor documentType={editor.documentType} journals={accountingSetup.journals} onClose={() => setEditor(null)} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Documento</th><th>Descrição</th><th>Diário predefinido</th><th>Origem</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{documents.map((document) => <tr key={document.code}><td><span className="inline-link"><FileText size={14} /> {document.code}</span></td><td>{document.name}</td><td><Link className="inline-link" to={`/diarios/${document.defaultJournalCode}`}>{document.defaultJournalCode}</Link></td><td>{document.sourceTemplateVersion ? 'Tabela predefinida' : 'Empresa'}</td><td><span className={`badge ${document.isActive ? 'success' : 'warning'}`}>{document.isActive ? 'Ativo' : 'Inativo'}</span></td><td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', documentType: document })}>Editar</button></td></tr>)}</tbody></table></div></section>
  </div>
}

const STATUS_LABELS: Record<string, { label: string; badge: string }> = {
  Assigned: { label: 'Atribuída', badge: 'success' },
  SuggestionAvailable: { label: 'Sugestão disponível', badge: 'warning' },
  Unclassified: { label: 'Por atribuir', badge: 'warning' },
  ReferenceMismatch: { label: 'Referencial diferente', badge: 'warning' },
}

export function TaxonomiesPage() {
  const { activeCompany, taxonomyReview, taxonomyCatalog, setupSource, updateAccount, applyTaxonomySuggestions } = useWorkContext()
  const [search, setSearch] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const accounts = useFiltered(taxonomyReview.accounts, search, (item) => `${item.code} ${item.name} ${item.taxonomyCode ?? ''}`)
  const { paged, currentPage, totalPages, setPage } = usePagedList(accounts, search, activeCompany.settings.pageSize || 50)
  const missing = taxonomyReview.postingAccountCount - taxonomyReview.classifiedCount
  const suggestions = taxonomyReview.accounts.filter((item) => item.status === 'SuggestionAvailable').length

  async function run(key: string, action: () => Promise<unknown>) {
    setBusy(key)
    setError('')
    try {
      await action()
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy('')
    }
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Conformidade SAF-T</p><h1>Taxonomias</h1><p>{taxonomyReview.taxonomyName}, determinada pela norma da empresa.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <section className="taxonomy-summary panel"><div className="taxonomy-reference"><span className="heading-symbol"><Tags /></span><div><small>TaxonomyReference</small><strong>{taxonomyReview.reference}</strong><span>{taxonomyReview.taxonomyName}</span></div></div><div className="taxonomy-health"><span className={missing ? 'warning' : 'success'}>{missing ? <CircleAlert /> : <CircleCheck />}</span><div><small>Cobertura atual</small><strong>{taxonomyReview.classifiedCount} de {taxonomyReview.postingAccountCount}</strong><span>{missing ? `${missing} contas de movimento por classificar` : 'Todas as contas de movimento classificadas'}</span></div></div></section>
    <p className="metadata-strip"><small>Catálogo {taxonomyCatalog.reference} (Portaria 302/2016)</small> <strong>{taxonomyCatalog.codes.length} códigos</strong> — {taxonomyCatalog.disclaimer}</p>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar taxonomias</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou código de taxonomia…" /></label><button className="button button-primary" type="button" disabled={!suggestions || busy === 'apply-all'} onClick={() => run('apply-all', applyTaxonomySuggestions)}>Aplicar sugestões do referencial{suggestions ? ` (${suggestions})` : ''}</button><a className="button button-secondary" href={taxonomyReview.taxonomyLegalSource || undefined} target="_blank" rel="noreferrer">Fonte oficial</a></div>
    {error && <p className="save-status error">{error}</p>}
    <section className="panel"><div className="panel-heading"><div><h2>Classificação das contas de movimento</h2><p>O código é obrigatório no SAF-T apenas para contas classificadas como movimento.</p></div></div><div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th>Referencial</th><th>Código</th><th>Sugestão</th><th>Validação</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{paged.map((account) => {
      const meta = STATUS_LABELS[account.status] ?? STATUS_LABELS.Unclassified
      return <tr key={account.code}><td><Link className="primary-cell" to={`/contas/${account.code}`}>{account.code}</Link></td><td>{account.name}</td><td>{account.taxonomyReference ?? taxonomyReview.reference}</td><td>{account.taxonomyCode ?? '—'}</td><td>{account.suggestedCode ?? '—'}</td><td><span className={`badge ${meta.badge}`}>{meta.label}</span></td><td>{account.suggestedCode && account.taxonomyCode === null
        ? <button className="button button-secondary" type="button" disabled={busy === account.code} onClick={() => run(account.code, () => updateAccount(account.code, { name: account.name, isActive: true, taxonomyReference: taxonomyReview.reference, taxonomyCode: account.suggestedCode }))}>Aplicar {account.suggestedCode}</button>
        : account.taxonomyCode !== null
          ? <button className="button button-secondary" type="button" disabled={busy === account.code} onClick={() => run(account.code, () => updateAccount(account.code, { name: account.name, isActive: true, taxonomyReference: null, taxonomyCode: null }))}>Limpar</button>
          : <span className="muted-value">Sem sugestão</span>}</td></tr>
    })}</tbody></table></div><Pager currentPage={currentPage} totalPages={totalPages} onPage={setPage} /></section>
  </div>
}
