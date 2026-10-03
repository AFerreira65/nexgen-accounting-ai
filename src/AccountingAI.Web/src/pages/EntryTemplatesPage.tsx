import { Database, LayoutTemplate, Plus, Trash2, X } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { type EntryTemplateLineRow, type EntryTemplateRow, type VatCodeRow } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const LINE_KINDS: { value: string; label: string }[] = [
  { value: 'Base', label: 'Base' },
  { value: 'Vat', label: 'IVA' },
  { value: 'Balance', label: 'Contrapartida' },
]

const LINE_SIDES: { value: string; label: string }[] = [
  { value: 'Debit', label: 'Débito' },
  { value: 'Credit', label: 'Crédito' },
]

const kindLabel = (value: string) => LINE_KINDS.find((item) => item.value === value)?.label ?? value
const sideLabel = (value: string) => LINE_SIDES.find((item) => item.value === value)?.label ?? value

type LineDraft = { kind: string; side: string; accountCode: string; vatCode: string; description: string }

const purchaseDraft = (): LineDraft[] => [
  { kind: 'Base', side: 'Debit', accountCode: '', vatCode: '', description: '' },
  { kind: 'Vat', side: 'Debit', accountCode: '', vatCode: '', description: '' },
  { kind: 'Balance', side: 'Credit', accountCode: '', vatCode: '', description: '' },
]

const toDraft = (line: EntryTemplateLineRow): LineDraft => ({ kind: line.kind, side: line.side, accountCode: line.accountCode, vatCode: line.vatCode ?? '', description: line.description ?? '' })

function EntryTemplateEditor({ template, vatCodeList, onSaved, onClose }: { template: EntryTemplateRow | null; vatCodeList: VatCodeRow[]; onSaved: () => void; onClose: () => void }) {
  const { activeCompany, accountingSetup, saveEntryTemplate } = useWorkContext()
  const isCreate = template === null
  const journals = accountingSetup.journals.filter((item) => item.isActive || item.code === template?.journalCode)
  const documentTypes = accountingSetup.documentTypes.filter((item) => item.isActive || item.code === template?.documentTypeCode)
  const postingAccounts = accountingSetup.accounts.filter((item) => item.allowsPosting && item.isActive)
  const [code, setCode] = useState('')
  const [name, setName] = useState(template?.name ?? '')
  const [journalCode, setJournalCode] = useState(template?.journalCode ?? journals[0]?.code ?? '')
  const [documentTypeCode, setDocumentTypeCode] = useState(template?.documentTypeCode ?? '')
  const [description, setDescription] = useState(template?.description ?? '')
  const [isActive, setIsActive] = useState(template?.isActive ?? true)
  const [lines, setLines] = useState<LineDraft[]>(template ? template.lines.map(toDraft) : purchaseDraft())
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  const patchLine = (index: number, patch: Partial<LineDraft>) =>
    setLines((current) => current.map((line, position) => (position === index ? { ...line, ...patch } : line)))

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      await saveEntryTemplate(activeCompany.companyId, isCreate ? code : template.code, {
        name,
        journalCode,
        documentTypeCode: documentTypeCode || null,
        description: description.trim() || null,
        isActive,
        lines: lines.map((line) => ({ kind: line.kind, side: line.side, accountCode: line.accountCode.trim(), vatCode: line.vatCode || null, description: line.description.trim() || null })),
      })
      onSaved()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o lançamento-tipo.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Novo lançamento-tipo' : `Editar ${template.code}`}</h2><p>Uma linha de base, as linhas de IVA que dela se calculam pela percentagem do código, e uma contrapartida que recebe o que falta para equilibrar.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form onSubmit={submit}>
      <div className="settings-grid">
        {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} maxLength={12} placeholder="ex.: COMPRA-SRV" autoFocus /></label>}
        <label><span>Nome</span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={120} /></label>
        <label><span>Diário</span><select value={journalCode} onChange={(event) => setJournalCode(event.target.value)}>{journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
        <label><span>Tipo de documento</span><select value={documentTypeCode} onChange={(event) => setDocumentTypeCode(event.target.value)}><option value="">Sem tipo</option>{documentTypes.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
        <label><span>Descrição proposta</span><input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={500} placeholder="Opcional" /></label>
        {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Ativo</strong><small>Desative para o retirar do editor de lançamentos.</small></span></label>}
      </div>

      <div className="table-wrap"><table>
        <thead><tr><th>Linha</th><th>Lado</th><th>Conta</th><th>Código de IVA</th><th>Descrição</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{lines.map((line, index) => <tr key={index}>
          <td><select aria-label={`Tipo da linha ${index + 1}`} className="vat-code-select" value={line.kind} onChange={(event) => patchLine(index, { kind: event.target.value })}>{LINE_KINDS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></td>
          <td><select aria-label={`Lado da linha ${index + 1}`} className="vat-code-select" value={line.side} onChange={(event) => patchLine(index, { side: event.target.value })}>{LINE_SIDES.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></td>
          <td><input aria-label={`Conta da linha ${index + 1}`} list="template-accounts" value={line.accountCode} onChange={(event) => patchLine(index, { accountCode: event.target.value })} /></td>
          <td><select aria-label={`Código de IVA da linha ${index + 1}`} className="vat-code-select" value={line.vatCode} onChange={(event) => patchLine(index, { vatCode: event.target.value })}>
            <option value="">—</option>
            {line.vatCode && !vatCodeList.some((item) => item.code === line.vatCode) && <option value={line.vatCode}>{line.vatCode}</option>}
            {vatCodeList.map((item) => <option key={item.code} value={item.code}>{item.code}</option>)}
          </select></td>
          <td><input aria-label={`Descrição da linha ${index + 1}`} value={line.description} onChange={(event) => patchLine(index, { description: event.target.value })} placeholder="A do lançamento" /></td>
          <td>{lines.length > 2 && <button className="icon-button" type="button" aria-label={`Remover linha ${index + 1}`} onClick={() => setLines((current) => current.filter((_, position) => position !== index))}><Trash2 size={16} /></button>}</td>
        </tr>)}</tbody>
        <tfoot><tr><td colSpan={6}><button className="button button-secondary" type="button" onClick={() => setLines((current) => [...current, { kind: 'Vat', side: 'Debit', accountCode: '', vatCode: '', description: '' }])}><Plus size={16} /> Linha</button></td></tr></tfoot>
      </table></div>
      <datalist id="template-accounts">{postingAccounts.slice(0, 1500).map((account) => <option key={account.code} value={account.code}>{account.name}</option>)}</datalist>

      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar lançamento-tipo'}</button>
      </footer>
    </form>
  </section>
}

export function EntryTemplatesPage() {
  const { activeCompany, source, entryTemplates, vatCodes } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'

  const [rows, setRows] = useState<EntryTemplateRow[]>([])
  const [vatCodeList, setVatCodeList] = useState<VatCodeRow[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; template: EntryTemplateRow } | null>(null)
  usePublishRecord(editor?.mode === 'edit' ? 'lancamentos-tipo' : undefined, editor?.mode === 'edit' ? editor.template.code : undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    setError('')
    void entryTemplates(companyId, includeInactive)
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os lançamentos-tipo.') })
    void vatCodes(companyId, false)
      .then((value) => { if (!controller.signal.aborted) setVatCodeList(value) })
      .catch(() => { /* os códigos só servem o editor */ })
    return () => controller.abort()
  }, [companyId, onDatabase, entryTemplates, vatCodes, includeInactive, reload])

  const saved = () => { setEditor(null); setReload((value) => value + 1) }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Lançamentos-tipo</h1><p>Modelos de lançamentos repetitivos que calculam o IVA a partir da base.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><LayoutTemplate /></span><h2>Requer ligação ao SQL Server</h2><p>Os lançamentos-tipo são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Lançamentos-tipo</h1><p>Modelos de lançamentos repetitivos — uma compra, uma venda. No editor de lançamentos escolhe-se o modelo, indica-se o valor e as linhas ficam preenchidas com o IVA calculado.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} /> Incluir inativos</label>
      <span className="catalog-count">{rows.length} {rows.length === 1 ? 'lançamento-tipo' : 'lançamentos-tipo'}</span>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo lançamento-tipo</button>
    </div>

    {editor?.mode === 'create' && <EntryTemplateEditor template={null} vatCodeList={vatCodeList} onSaved={saved} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <EntryTemplateEditor key={editor.template.code} template={editor.template} vatCodeList={vatCodeList} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Código</th><th>Nome</th><th>Diário</th><th>Linhas</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.code}>
          <td><span className="primary-cell">{row.code}</span></td>
          <td>{row.name}{row.documentTypeCode && <small>Documento {row.documentTypeCode}</small>}</td>
          <td>{row.journalCode}</td>
          <td>{row.lines.map((line) => <small key={`${line.kind}-${line.side}-${line.accountCode}`}>{kindLabel(line.kind)} · {sideLabel(line.side)} · {line.accountCode}{line.vatCode ? ` · ${line.vatCode}` : ''}</small>)}</td>
          <td><span className={`badge ${row.isActive ? 'success' : 'warning'}`}>{row.isActive ? 'Ativo' : 'Inativo'}</span></td>
          <td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', template: row })}>Editar</button></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Ainda não há lançamentos-tipo. Crie primeiro os códigos de IVA e depois o primeiro modelo.</p>}
    </section>
  </div>
}
