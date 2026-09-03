import { Database, Plus, Search, Trash2, X } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { useWorkContext, type MovementLineInput } from '../app/WorkContext'

const today = () => new Date().toISOString().slice(0, 10)
const emptyLine = (): MovementLineInput => ({ accountCode: '', description: '', debit: 0, credit: 0 })

function MovementEditor({ movement, onClose }: { movement: Movement | null; onClose: () => void }) {
  const { accountingSetup, createMovement, reviseMovement } = useWorkContext()
  const isEdit = movement !== null
  const journals = accountingSetup.journals.filter((item) => item.isActive || item.code === movement?.journalId)
  const postingAccounts = accountingSetup.accounts.filter((item) => item.allowsPosting && item.isActive)
  const documentTypes = accountingSetup.documentTypes.filter((item) => item.isActive)
  const [documentTypeCode, setDocumentTypeCode] = useState('')
  const [journalCode, setJournalCode] = useState(movement?.journalId ?? journals[0]?.code ?? '')
  const [entryDate, setEntryDate] = useState(movement?.date ?? today())
  const [documentReference, setDocumentReference] = useState(movement?.document ?? '')
  const [description, setDescription] = useState(movement?.description ?? '')
  const [reviewed, setReviewed] = useState(movement?.status === 'Conferido')
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

  function handleFormKeyDown(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key !== 'Enter' || event.shiftKey) return
    const target = event.target as HTMLElement
    const key = target.dataset.fieldKey
    if (!key) return // botões e outros controlos mantêm o comportamento normal
    event.preventDefault()

    if (key === `line-${lines.length - 1}-credit`) {
      setLines((current) => [...current, emptyLine()])
      setFocusLineAccount(lines.length)
      return
    }

    const keys = orderedKeys()
    const next = keys[keys.indexOf(key) + 1]
    if (next) fieldRefs.current.get(next)?.focus()
  }

  const totalDebit = lines.reduce((sum, line) => sum + (Number(line.debit) || 0), 0)
  const totalCredit = lines.reduce((sum, line) => sum + (Number(line.credit) || 0), 0)
  const difference = Math.round((totalDebit - totalCredit) * 100) / 100

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
    <div className="panel-heading"><div><h2>{isEdit ? `Corrigir ${movement.id.replaceAll('-', ' ')}` : 'Novo lançamento'}</h2><p>Débito igual a crédito, pelo menos duas linhas. Enter avança para o campo seguinte; no último campo da última linha, cria uma linha nova. {isEdit ? 'Só um lançamento por conferir pode ser corrigido.' : 'A referência é gerada pelo diário e exercício.'}</p></div><button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button></div>
    <form onSubmit={submit} onKeyDown={handleFormKeyDown}>
      <datalist id="posting-accounts">{postingAccounts.map((account) => <option key={account.code} value={account.code}>{account.name}</option>)}</datalist>
      <div className="settings-grid">
        {!isEdit && <label><span>Tipo de documento</span><select data-field-key="doctype" ref={register('doctype')} value={documentTypeCode} onChange={(event) => { const code = event.target.value; setDocumentTypeCode(code); const chosen = documentTypes.find((item) => item.code === code); if (chosen) setJournalCode(chosen.defaultJournalCode) }}><option value="">Sem tipo · numerar pelo diário</option>{documentTypes.map((document) => <option key={document.code} value={document.code}>{document.code} · {document.name}</option>)}</select></label>}
        <label><span>Diário</span><select data-field-key="journal" ref={register('journal')} value={journalCode} onChange={(event) => setJournalCode(event.target.value)}>{journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
        <label><span>Data</span><input data-field-key="date" ref={register('date')} type="date" value={entryDate} onChange={(event) => setEntryDate(event.target.value)} /></label>
        <label><span>Documento</span><input data-field-key="document" ref={register('document')} value={documentReference} onChange={(event) => setDocumentReference(event.target.value)} placeholder="ex.: FT 2026/1" /></label>
        <label><span>Descrição</span><input data-field-key="description" ref={register('description')} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label className="toggle-field"><input data-field-key="reviewed" ref={register('reviewed')} type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} /><span><strong>Conferido</strong><small>Caso contrário fica por conferir.</small></span></label>
      </div>

      <div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {lines.map((line, index) => <tr key={index}>
          <td><input data-field-key={`line-${index}-account`} ref={register(`line-${index}-account`)} aria-label={`Conta da linha ${index + 1}`} list="posting-accounts" value={line.accountCode} onChange={(event) => patchLine(index, { accountCode: event.target.value })} /></td>
          <td><input data-field-key={`line-${index}-description`} ref={register(`line-${index}-description`)} aria-label={`Descrição da linha ${index + 1}`} value={line.description} onChange={(event) => patchLine(index, { description: event.target.value })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-debit`} ref={register(`line-${index}-debit`)} aria-label={`Débito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.debit || ''} onChange={(event) => patchLine(index, { debit: Number(event.target.value), credit: 0 })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-credit`} ref={register(`line-${index}-credit`)} aria-label={`Crédito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.credit || ''} onChange={(event) => patchLine(index, { credit: Number(event.target.value), debit: 0 })} /></td>
          <td>{lines.length > 2 && <button className="icon-button" type="button" aria-label={`Remover linha ${index + 1}`} onClick={() => setLines((current) => current.filter((_, position) => position !== index))}><Trash2 size={16} /></button>}</td>
        </tr>)}
      </tbody><tfoot><tr><td colSpan={2}><button className="button button-secondary" type="button" onClick={() => setLines((current) => [...current, emptyLine()])}><Plus size={16} /> Linha</button></td><td className="numeric strong">{euro.format(totalDebit)}</td><td className="numeric strong">{euro.format(totalCredit)}</td><td /></tr></tfoot></table></div>

      <footer className="settings-actions">
        <span className={`save-status ${difference === 0 ? 'success' : 'error'}`}>{difference === 0 ? 'Equilibrado' : `Diferença de ${euro.format(difference)}`}</span>
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving' || difference !== 0}>{status === 'saving' ? 'A guardar…' : (isEdit ? 'Guardar correção' : 'Registar lançamento')}</button>
      </footer>
    </form>
  </section>
}

export function MovementsPage() {
  const { accountingLoading, accountingSource, movements, reviewMovement, voidMovement } = useWorkContext()
  const [editing, setEditing] = useState<Movement | null | undefined>(undefined)
  const [busy, setBusy] = useState('')

  async function act(reference: string, action: () => Promise<void>) {
    setBusy(reference)
    try { await action() } catch { /* a página volta a carregar o estado */ } finally { setBusy('') }
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Movimentos contabilísticos</h1><p>Consulte, percorra e registe lançamentos sem perder o contexto.</p></div><span className={`connection-badge ${accountingSource}`}><Database size={15} />{accountingSource === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <div className="filterbar"><label><Search size={17} /><input aria-label="Pesquisar movimentos" placeholder="Pesquisar movimento ou descrição…" /></label><button className="button button-primary" type="button" onClick={() => setEditing(null)}><Plus size={16} /> Novo lançamento</button></div>
    {editing !== undefined && <MovementEditor movement={editing} onClose={() => setEditing(undefined)} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{movements.map((movement) => <tr key={movement.id}>
      <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td>
      <td>{movement.description}<small>{movement.documentType ? `${movement.documentType} · ` : ''}{movement.document}</small></td>
      <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td>
      <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : movement.status === 'Anulado' ? 'neutral' : 'warning'}`}>{movement.status}</span></td>
      <td className="numeric strong">{euro.format(movement.amount)}</td>
      <td><div className="row-actions">
        {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => setEditing(movement)}>Corrigir</button>}
        {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => act(movement.id, () => reviewMovement(movement.id))}>Conferir</button>}
        {movement.status !== 'Anulado' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => act(movement.id, () => voidMovement(movement.id))}>Anular</button>}
      </div></td>
    </tr>)}</tbody></table></div>{accountingLoading && <p className="empty-state">A carregar movimentos da base de dados…</p>}{!accountingLoading && movements.length === 0 && <p className="empty-state">Esta empresa ainda não tem movimentos.</p>}</section>
  </div>
}
