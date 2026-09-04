import { Database, Download, Plus, Search, StickyNote, Trash2, X } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { PeriodShortcuts } from '../app/periods'
import { useWorkContext, type MovementLineInput } from '../app/WorkContext'

const today = () => new Date().toISOString().slice(0, 10)
const emptyLine = (): MovementLineInput => ({ accountCode: '', description: '', debit: 0, credit: 0 })

type PostingAccount = { code: string; name: string }

// Campo de conta com sugestões: mostra apenas contas de movimento e permite
// escolher com o rato ou com as setas + Enter.
function AccountField({ value, accounts, fieldKey, register, ariaLabel, invalid, onChange, onCommit }: {
  value: string
  accounts: PostingAccount[]
  fieldKey: string
  register: (key: string) => (element: HTMLElement | null) => void
  ariaLabel: string
  invalid: boolean
  onChange: (code: string) => void
  onCommit: () => void
}) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const query = value.trim().toLowerCase()
  const matches = (query
    ? accounts.filter((account) => account.code.toLowerCase().startsWith(query) || account.name.toLowerCase().includes(query))
    : accounts
  ).slice(0, 8)
  const showList = open && matches.length > 0

  function pick(code: string, advance: boolean) {
    onChange(code)
    setOpen(false)
    if (advance) onCommit()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') { event.preventDefault(); setOpen(true); setActive((index) => Math.min(index + 1, matches.length - 1)) }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActive((index) => Math.max(index - 1, 0)) }
    else if (event.key === 'Escape') { setOpen(false) }
    else if (event.key === 'Enter' && showList && matches[active]) {
      event.preventDefault()
      event.stopPropagation() // o formulário não trata este Enter
      pick(matches[active].code, true)
    }
  }

  return <div className="account-field">
    <input
      data-field-key={fieldKey}
      ref={register(fieldKey)}
      aria-label={ariaLabel}
      role="combobox"
      aria-expanded={showList}
      autoComplete="off"
      className={invalid ? 'invalid' : undefined}
      value={value}
      onChange={(event) => { onChange(event.target.value); setOpen(true); setActive(0) }}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
      onKeyDown={handleKeyDown}
    />
    {showList && <ul className="account-suggest">
      {matches.map((account, index) => <li
        key={account.code}
        aria-selected={index === active}
        onMouseDown={(event) => { event.preventDefault(); pick(account.code, false) }}
      ><span className="code">{account.code}</span><span className="name">{account.name}</span></li>)}
    </ul>}
  </div>
}

function MovementEditor({ movement, duplicate = false, onClose }: { movement: Movement | null; duplicate?: boolean; onClose: () => void }) {
  const { accountingSetup, activeCompany, createMovement, reviseMovement, source, user } = useWorkContext()
  const isEdit = movement !== null && !duplicate
  // Em duplicação, o movimento de origem semeia os campos mas o lançamento é novo.
  const template = duplicate ? movement : null
  const [closedYears, setClosedYears] = useState<number[]>([])
  const [postingLockDate, setPostingLockDate] = useState<string | null>(null)

  useEffect(() => {
    if (source !== 'database') return
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/closed-years`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as number[] : []))
      .then(setClosedYears)
      .catch(() => { /* aviso opcional */ })
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/posting-lock-date`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as { lockDate: string | null } : { lockDate: null }))
      .then((payload) => setPostingLockDate(payload.lockDate))
      .catch(() => { /* aviso opcional */ })
    return () => controller.abort()
  }, [activeCompany.companyId, source, user.id])
  const journals = accountingSetup.journals.filter((item) => item.isActive || item.code === movement?.journalId)
  const postingAccounts = accountingSetup.accounts.filter((item) => item.allowsPosting && item.isActive)
  // Contas de integração conhecidas (agregadoras/razão): não podem ser lançadas.
  const aggregateCodes = new Set(accountingSetup.accounts.filter((item) => !item.allowsPosting).map((item) => item.code.toUpperCase()))
  const documentTypes = accountingSetup.documentTypes.filter((item) => item.isActive)
  const [documentTypeCode, setDocumentTypeCode] = useState(template?.documentType ?? '')
  const [journalCode, setJournalCode] = useState(movement?.journalId ?? journals[0]?.code ?? '')
  const [entryDate, setEntryDate] = useState(isEdit && movement ? movement.date : today())
  const [documentReference, setDocumentReference] = useState(isEdit && movement ? movement.document : '')
  const [description, setDescription] = useState(movement?.description ?? '')
  const [notes, setNotes] = useState(movement?.notes ?? '')
  const [reviewed, setReviewed] = useState(isEdit && movement?.status === 'Conferido')
  const [lines, setLines] = useState<MovementLineInput[]>(
    movement ? movement.lines.map((line) => ({ accountCode: line.account, description: line.description, debit: line.debit, credit: line.credit })) : [emptyLine(), emptyLine()],
  )
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  // Navegação por Enter: cada campo regista-se aqui pela sua chave e o
  // formulário passa o foco ao campo seguinte quando se tecla Enter.
  const fieldRefs = useRef(new Map<string, HTMLElement>())
  const [focusLineAccount, setFocusLineAccount] = useState<number | null>(null)
  const register = (key: string) => (element: HTMLElement | null) => {
    if (element) fieldRefs.current.set(key, element)
    else fieldRefs.current.delete(key)
  }

  const orderedKeys = () => [
    ...(isEdit ? [] : ['doctype']),
    'journal', 'date', 'document', 'description', 'reviewed',
    ...lines.flatMap((_, index) => [`line-${index}-account`, `line-${index}-description`, `line-${index}-debit`, `line-${index}-credit`]),
  ]

  useEffect(() => {
    if (focusLineAccount === null) return
    fieldRefs.current.get(`line-${focusLineAccount}-account`)?.focus()
    setFocusLineAccount(null)
  }, [focusLineAccount])

  function focusNextField(key: string) {
    const keys = orderedKeys()
    const next = keys[keys.indexOf(key) + 1]
    if (next) fieldRefs.current.get(next)?.focus()
  }

  function handleFormKeyDown(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key !== 'Enter' || event.shiftKey) return
    const target = event.target as HTMLElement
    const key = target.dataset.fieldKey
    if (!key) return // botões e outros controlos mantêm o comportamento normal
    event.preventDefault()

    // Numa linha cuja conta seja de integração (agregadora), o cursor não avança.
    const accountLine = key.match(/^line-(\d+)-account$/)
    if (accountLine) {
      const code = lines[Number(accountLine[1])]?.accountCode.trim().toUpperCase()
      if (code && aggregateCodes.has(code)) return
    }

    if (key === `line-${lines.length - 1}-credit`) {
      setLines((current) => [...current, emptyLine()])
      setFocusLineAccount(lines.length)
      return
    }

    focusNextField(key)
  }

  const totalDebit = lines.reduce((sum, line) => sum + (Number(line.debit) || 0), 0)
  const totalCredit = lines.reduce((sum, line) => sum + (Number(line.credit) || 0), 0)
  const difference = Math.round((totalDebit - totalCredit) * 100) / 100
  const isInvalidAccount = (code: string) => aggregateCodes.has(code.trim().toUpperCase())
  const hasInvalidAccount = lines.some((line) => isInvalidAccount(line.accountCode))
  const entryYear = Number(entryDate.slice(0, 4))
  const yearClosed = closedYears.includes(entryYear)
  const periodLocked = postingLockDate !== null && entryDate <= postingLockDate

  function patchLine(index: number, patch: Partial<MovementLineInput>) {
    setLines((current) => current.map((line, position) => position === index ? { ...line, ...patch } : line))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      const payload = {
        journalCode,
        documentTypeCode: documentTypeCode || null,
        entryDate,
        documentReference,
        description,
        notes: notes.trim() || null,
        reviewed,
        lines: lines
          .filter((line) => line.accountCode.trim())
          .map((line) => ({ ...line, accountCode: line.accountCode.trim().toUpperCase(), debit: Number(line.debit) || 0, credit: Number(line.credit) || 0 })),
      }
      if (isEdit) await reviseMovement(movement.id, payload)
      else await createMovement(payload)
      onClose()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o lançamento.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>{isEdit ? `Corrigir ${movement.id.replaceAll('-', ' ')}` : (duplicate ? `Novo lançamento a partir de ${movement!.id.replaceAll('-', ' ')}` : 'Novo lançamento')}</h2><p>Débito igual a crédito, pelo menos duas linhas. Enter avança para o campo seguinte; no último campo da última linha, cria uma linha nova. {isEdit ? 'Só um lançamento por conferir pode ser corrigido.' : 'A referência é gerada pelo diário e exercício.'}</p></div><button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button></div>
    <form onSubmit={submit} onKeyDown={handleFormKeyDown}>
      <div className="settings-grid">
        {!isEdit && <label><span>Tipo de documento</span><select data-field-key="doctype" ref={register('doctype')} value={documentTypeCode} onChange={(event) => { const code = event.target.value; setDocumentTypeCode(code); const chosen = documentTypes.find((item) => item.code === code); if (chosen) setJournalCode(chosen.defaultJournalCode) }}><option value="">Sem tipo · numerar pelo diário</option>{documentTypes.map((document) => <option key={document.code} value={document.code}>{document.code} · {document.name}</option>)}</select></label>}
        <label><span>Diário</span><select data-field-key="journal" ref={register('journal')} value={journalCode} onChange={(event) => setJournalCode(event.target.value)}>{journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
        <label><span>Data</span><input data-field-key="date" ref={register('date')} type="date" value={entryDate} onChange={(event) => setEntryDate(event.target.value)} /></label>
        <label><span>Documento</span><input data-field-key="document" ref={register('document')} value={documentReference} onChange={(event) => setDocumentReference(event.target.value)} placeholder="ex.: FT 2026/1" /></label>
        <label><span>Descrição</span><input data-field-key="description" ref={register('description')} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label className="toggle-field"><input data-field-key="reviewed" ref={register('reviewed')} type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} /><span><strong>Conferido</strong><small>Caso contrário fica por conferir.</small></span></label>
      </div>

      <label className="field-block"><span>Observações <small>(opcional, uso interno)</small></span><textarea value={notes} rows={2} maxLength={2000} onChange={(event) => setNotes(event.target.value)} placeholder="Notas internas sobre este lançamento" /></label>

      <div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {lines.map((line, index) => <tr key={index}>
          <td><AccountField
            fieldKey={`line-${index}-account`}
            register={register}
            ariaLabel={`Conta da linha ${index + 1}`}
            accounts={postingAccounts}
            value={line.accountCode}
            invalid={isInvalidAccount(line.accountCode)}
            onChange={(code) => patchLine(index, { accountCode: code })}
            onCommit={() => focusNextField(`line-${index}-account`)}
          /></td>
          <td><input data-field-key={`line-${index}-description`} ref={register(`line-${index}-description`)} aria-label={`Descrição da linha ${index + 1}`} value={line.description} onChange={(event) => patchLine(index, { description: event.target.value })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-debit`} ref={register(`line-${index}-debit`)} aria-label={`Débito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.debit || ''} onChange={(event) => patchLine(index, { debit: Number(event.target.value), credit: 0 })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-credit`} ref={register(`line-${index}-credit`)} aria-label={`Crédito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.credit || ''} onChange={(event) => patchLine(index, { credit: Number(event.target.value), debit: 0 })} /></td>
          <td>{lines.length > 2 && <button className="icon-button" type="button" aria-label={`Remover linha ${index + 1}`} onClick={() => setLines((current) => current.filter((_, position) => position !== index))}><Trash2 size={16} /></button>}</td>
        </tr>)}
      </tbody><tfoot><tr><td colSpan={2}><button className="button button-secondary" type="button" onClick={() => setLines((current) => [...current, emptyLine()])}><Plus size={16} /> Linha</button></td><td className="numeric strong">{euro.format(totalDebit)}</td><td className="numeric strong">{euro.format(totalCredit)}</td><td /></tr></tfoot></table></div>

      <footer className="settings-actions">
        <span className={`save-status ${difference === 0 ? 'success' : 'error'}`}>{difference === 0 ? 'Equilibrado' : `Diferença de ${euro.format(difference)}`}</span>
        {yearClosed && <span className="save-status error">O exercício de {entryYear} está apurado. Reabra-o para lançar com esta data.</span>}
        {periodLocked && !yearClosed && <span className="save-status error">O período até {postingLockDate} está bloqueado para lançamentos.</span>}
        {hasInvalidAccount && <span className="save-status error">Há linhas com uma conta de integração; só contas de movimento podem ser lançadas.</span>}
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving' || difference !== 0 || yearClosed || periodLocked || hasInvalidAccount}>{status === 'saving' ? 'A guardar…' : (isEdit ? 'Guardar correção' : 'Registar lançamento')}</button>
      </footer>
    </form>
  </section>
}

export function MovementsPage() {
  const { accountingLoading, accountingSetup, accountingSource, activeCompany, movements, reopenMovement, reviewMovement, source, user, voidMovement } = useWorkContext()
  const [editing, setEditing] = useState<{ movement: Movement | null; duplicate: boolean } | undefined>(undefined)
  const [busy, setBusy] = useState('')
  const [search, setSearch] = useState('')
  const [journalFilter, setJournalFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [closedYears, setClosedYears] = useState<number[]>([])
  const [postingLockDate, setPostingLockDate] = useState<string | null>(null)

  useEffect(() => {
    if (source !== 'database') { setClosedYears([]); setPostingLockDate(null); return }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/closed-years`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as number[] : []))
      .then(setClosedYears)
      .catch(() => { /* aviso opcional */ })
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/posting-lock-date`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as { lockDate: string | null } : { lockDate: null }))
      .then((payload) => setPostingLockDate(payload.lockDate))
      .catch(() => { /* aviso opcional */ })
    return () => controller.abort()
  }, [activeCompany.companyId, source, user.id])

  // Motivo pelo qual um movimento com esta data não pode ser corrigido nem anulado.
  // Os lançamentos de sistema (apuramento e abertura) ficam isentos, tal como na API.
  function blockedReason(movement: Movement): string | null {
    if (movement.documentType === 'AP' || movement.documentType === 'AB') return null
    if (closedYears.includes(Number(movement.date.slice(0, 4)))) return 'Exercício apurado'
    if (postingLockDate !== null && movement.date <= postingLockDate) return 'Período bloqueado'
    return null
  }

  async function act(reference: string, action: () => Promise<void>) {
    setBusy(reference)
    try { await action() } catch { /* a página volta a carregar o estado */ } finally { setBusy('') }
  }

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const visible = movements.filter((movement) =>
    (!term || `${movement.id} ${movement.description} ${movement.document ?? ''} ${movement.notes ?? ''}`.toLocaleLowerCase('pt-PT').includes(term))
    && (!journalFilter || movement.journalId === journalFilter)
    && (!statusFilter || movement.status === statusFilter)
    && (!fromDate || movement.date >= fromDate)
    && (!toDate || movement.date <= toDate))

  const exportCsv = () => {
    const header = ['Movimento', 'Data', 'Diário', 'Tipo de documento', 'Documento', 'Descrição', 'Notas', 'Estado', 'Conta', 'Nome da conta', 'Descrição da linha', 'Débito', 'Crédito']
    const rows = visible.flatMap((movement) => movement.lines.map((line) => [
      movement.id, movement.date, movement.journalId, movement.documentType ?? '', movement.document, movement.description, movement.notes ?? '', movement.status,
      line.account, line.accountName, line.description, line.debit, line.credit,
    ]))
    downloadCsv(csvFilename('movimentos', activeCompany.legalName), toCsv([header, ...rows]))
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Movimentos contabilísticos</h1><p>Consulte, percorra e registe lançamentos sem perder o contexto.</p></div><span className={`connection-badge ${accountingSource}`}><Database size={15} />{accountingSource === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <div className="filterbar">
      <label><Search size={17} /><input aria-label="Pesquisar movimentos" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar movimento ou descrição…" /></label>
      <label><span className="sr-only">Diário</span><select value={journalFilter} onChange={(event) => setJournalFilter(event.target.value)}><option value="">Todos os diários</option>{accountingSetup.journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
      <label><span className="sr-only">Estado</span><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="">Todos os estados</option><option value="Por conferir">Por conferir</option><option value="Conferido">Conferido</option><option value="Anulado">Anulado</option></select></label>
      <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      {(fromDate || toDate) && <button className="button button-secondary" type="button" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>}
      <span className="catalog-count">{visible.length} de {movements.length}</span>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={visible.length === 0}><Download size={15} /> Exportar CSV</button>
      <button className="button button-primary" type="button" onClick={() => setEditing({ movement: null, duplicate: false })}><Plus size={16} /> Novo lançamento</button>
    </div>
    {editing !== undefined && <MovementEditor movement={editing.movement} duplicate={editing.duplicate} onClose={() => setEditing(undefined)} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{visible.map((movement) => <tr key={movement.id}>
      <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td>
      <td>{movement.description}{movement.notes ? <span className="note-flag" title={movement.notes}><StickyNote size={13} /> nota</span> : null}<small>{movement.documentType ? `${movement.documentType} · ` : ''}{movement.document}</small></td>
      <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td>
      <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : movement.status === 'Anulado' ? 'neutral' : 'warning'}`}>{movement.status}</span></td>
      <td className="numeric strong">{euro.format(movement.amount)}</td>
      <td><div className="row-actions">
        {(() => {
          const blocked = blockedReason(movement)
          return <>
            {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => setEditing({ movement, duplicate: false })}>Corrigir</button>}
            {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => act(movement.id, () => reviewMovement(movement.id))}>Conferir</button>}
            {movement.status === 'Conferido' && movement.documentType !== 'AP' && movement.documentType !== 'AB' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => act(movement.id, () => reopenMovement(movement.id))}>Reabrir</button>}
            {movement.status !== 'Anulado' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => act(movement.id, () => voidMovement(movement.id))}>Anular</button>}
            {movement.documentType !== 'AP' && movement.documentType !== 'AB' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => setEditing({ movement, duplicate: true })}>Duplicar</button>}
            {blocked !== null && movement.status !== 'Anulado' && <small className="row-actions-note">{blocked}</small>}
          </>
        })()}
      </div></td>
    </tr>)}</tbody></table></div>{accountingLoading && <p className="empty-state">A carregar movimentos da base de dados…</p>}{!accountingLoading && movements.length === 0 && <p className="empty-state">Esta empresa ainda não tem movimentos.</p>}{!accountingLoading && movements.length > 0 && visible.length === 0 && <p className="empty-state">Nenhum movimento corresponde aos filtros.</p>}</section>
  </div>
}
