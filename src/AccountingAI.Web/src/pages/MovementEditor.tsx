import { Plus, Trash2, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { euro, type EntryTemplateRow, type Movement, type PartyRow, type VatClassificationRow, type VatCodeRow } from '../app/demo-data'
import { useWorkContext, type MovementLineInput } from '../app/WorkContext'

const today = () => new Date().toISOString().slice(0, 10)
// A linha no editor: além do que segue para a API, o texto do terceiro tal
// como está escrito («500584095» ou «ES B12345678») e o nome para confirmação.
type EditorLine = MovementLineInput & { partyText: string; partyName: string }

const emptyLine = (): EditorLine => ({ accountCode: '', description: '', debit: 0, credit: 0, vatCode: '', partyText: '', partyName: '' })

const partyText = (countryCode?: string | null, taxNumber?: string | null) =>
  taxNumber ? `${countryCode && countryCode !== 'PT' ? `${countryCode} ` : ''}${taxNumber}` : ''

// «ES B12345678» → país ES; só o contribuinte → Portugal.
function parsePartyText(text: string): { partyTaxNumber: string | null; partyCountryCode: string | null } {
  const trimmed = text.trim()
  if (!trimmed) return { partyTaxNumber: null, partyCountryCode: null }
  const foreign = /^([A-Za-z]{2})\s+(\S.*)$/.exec(trimmed)
  return foreign
    ? { partyTaxNumber: foreign[2], partyCountryCode: foreign[1].toUpperCase() }
    : { partyTaxNumber: trimmed, partyCountryCode: 'PT' }
}

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

// Campo de terceiro com sugestões pedidas ao servidor (nome ou contribuinte).
// Fica fora da navegação por Enter: chega-se com Tab, e é opcional.
function PartyField({ value, name, ariaLabel, search, onChange }: {
  value: string
  name: string
  ariaLabel: string
  search: ((term: string) => Promise<PartyRow[]>) | null
  onChange: (text: string, name: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [matches, setMatches] = useState<PartyRow[]>([])
  const term = value.trim()

  useEffect(() => {
    if (!search || !open || term.length < 2) { setMatches([]); return }
    let cancelled = false
    const timer = window.setTimeout(() => {
      void search(term).then((rows) => { if (!cancelled) { setMatches(rows); setActive(0) } }).catch(() => { /* sugestões opcionais */ })
    }, 200)
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [search, open, term])

  const showList = open && matches.length > 0
  const pick = (party: PartyRow) => { onChange(partyText(party.countryCode, party.taxRegistrationNumber), party.name); setOpen(false) }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') { event.preventDefault(); setOpen(true); setActive((index) => Math.min(index + 1, matches.length - 1)) }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActive((index) => Math.max(index - 1, 0)) }
    else if (event.key === 'Escape') { setOpen(false) }
    else if (event.key === 'Enter' && showList && matches[active]) {
      event.preventDefault()
      event.stopPropagation() // o formulário não trata este Enter
      pick(matches[active])
    }
  }

  return <div className="account-field party-field">
    <input
      aria-label={ariaLabel}
      role="combobox"
      aria-expanded={showList}
      autoComplete="off"
      placeholder="NIF ou nome"
      title={name || undefined}
      value={value}
      onChange={(event) => { onChange(event.target.value, ''); setOpen(true) }}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
      onKeyDown={handleKeyDown}
    />
    {name && <small className="party-name">{name}</small>}
    {showList && <ul className="account-suggest">
      {matches.map((party, index) => <li
        key={party.id}
        aria-selected={index === active}
        onMouseDown={(event) => { event.preventDefault(); pick(party) }}
      ><span className="code">{partyText(party.countryCode, party.taxRegistrationNumber)}</span><span className="name">{party.name}</span></li>)}
    </ul>}
  </div>
}

export function MovementEditor({ movement, duplicate = false, onClose }: { movement: Movement | null; duplicate?: boolean; onClose: () => void }) {
  const { accountingSetup, activeCompany, applyEntryTemplate, createMovement, entryTemplates, parties, reviseMovement, source, user, vatClassification, vatCodes } = useWorkContext()
  const companyId = activeCompany.companyId
  // Códigos de IVA ativos e a classificação por conta, que propõe o código.
  const [vatCodeList, setVatCodeList] = useState<VatCodeRow[]>([])
  const [accountVat, setAccountVat] = useState<VatClassificationRow[]>([])
  // Lançamentos-tipo: escolhe-se o modelo e o valor, e as linhas ficam preenchidas.
  const [templateList, setTemplateList] = useState<EntryTemplateRow[]>([])
  const [templateCode, setTemplateCode] = useState('')
  const [templateAmount, setTemplateAmount] = useState('')
  const [templateIncludesVat, setTemplateIncludesVat] = useState(false)
  const [templateError, setTemplateError] = useState('')
  useEffect(() => {
    if (source !== 'database') return
    let cancelled = false
    void vatCodes(companyId, false).then((rows) => { if (!cancelled) setVatCodeList(rows) }).catch(() => { /* coluna opcional */ })
    void vatClassification(companyId).then((rows) => { if (!cancelled) setAccountVat(rows) }).catch(() => { /* proposta opcional */ })
    void entryTemplates(companyId, false).then((rows) => { if (!cancelled) setTemplateList(rows) }).catch(() => { /* barra opcional */ })
    return () => { cancelled = true }
  }, [companyId, entryTemplates, source, vatClassification, vatCodes])
  // O código proposto para uma conta: o único código ativo com a mesma taxa,
  // natureza e região da classificação dessa conta.
  const proposedVatCode = (accountCode: string) => {
    const classification = accountVat.find((row) => row.accountCode === accountCode.trim().toUpperCase() && row.classified)
    if (!classification) return ''
    const matches = vatCodeList.filter((code) => code.rate === classification.rate && code.nature === classification.nature && code.region === classification.region)
    return matches.length === 1 ? matches[0].code : ''
  }
  const searchParties = useMemo(
    () => source === 'database'
      ? (term: string) => parties(companyId, { search: term, role: 'Todos', includeInactive: false, page: 1, pageSize: 8 }).then((result) => result.items)
      : null,
    [companyId, parties, source],
  )
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
  const [documentDate, setDocumentDate] = useState(isEdit && movement ? movement.documentDate ?? '' : '')
  const [description, setDescription] = useState(movement?.description ?? '')
  const [notes, setNotes] = useState(movement?.notes ?? '')
  const [reviewed, setReviewed] = useState(isEdit && movement?.status === 'Conferido')
  const [lines, setLines] = useState<EditorLine[]>(
    movement ? movement.lines.map((line) => ({ accountCode: line.account, description: line.description, debit: line.debit, credit: line.credit, vatCode: line.vatCode ?? '', partyText: partyText(line.partyCountryCode, line.partyTaxNumber), partyName: line.partyName ?? '' })) : [emptyLine(), emptyLine()],
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

  async function fillFromTemplate() {
    const amount = Number(templateAmount.replace(',', '.'))
    if (!templateCode || !(amount > 0)) { setTemplateError('Escolha o lançamento-tipo e indique um valor positivo.'); return }
    setTemplateError('')
    try {
      const applied = await applyEntryTemplate(companyId, templateCode, amount, templateIncludesVat)
      setJournalCode(applied.journalCode)
      if (applied.documentTypeCode) setDocumentTypeCode(applied.documentTypeCode)
      if (applied.description && !description.trim()) setDescription(applied.description)
      // O terceiro é do documento, não do modelo: o que já estava escrito
      // passa para a contrapartida (o cliente, o fornecedor).
      const party = lines.find((line) => line.partyText.trim())
      setLines(applied.lines.map((line) => ({
        accountCode: line.accountCode, description: line.description, debit: line.debit, credit: line.credit, vatCode: line.vatCode ?? '',
        partyText: line.kind === 'Balance' ? party?.partyText ?? '' : '',
        partyName: line.kind === 'Balance' ? party?.partyName ?? '' : '',
      })))
    } catch (problem) {
      setTemplateError(problem instanceof Error ? problem.message : 'Não foi possível aplicar o lançamento-tipo.')
    }
  }

  function patchLine(index: number, patch: Partial<EditorLine>) {
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
        documentDate: documentDate || null,
        description,
        notes: notes.trim() || null,
        reviewed,
        lines: lines
          .filter((line) => line.accountCode.trim())
          .map((line): MovementLineInput => ({ accountCode: line.accountCode.trim().toUpperCase(), description: line.description, debit: Number(line.debit) || 0, credit: Number(line.credit) || 0, vatCode: line.vatCode || null, ...parsePartyText(line.partyText) })),
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
    {!isEdit && templateList.length > 0 && <div className="builder-row template-bar">
      <label>Lançamento-tipo <select aria-label="Lançamento-tipo" value={templateCode} onChange={(event) => setTemplateCode(event.target.value)}><option value="">Escolher…</option>{templateList.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      <label>Valor <input aria-label="Valor do lançamento-tipo" inputMode="decimal" value={templateAmount} onChange={(event) => setTemplateAmount(event.target.value)} placeholder="0,00" /></label>
      <label className="inline-check"><input type="checkbox" checked={templateIncludesVat} onChange={(event) => setTemplateIncludesVat(event.target.checked)} /> Valor com IVA incluído</label>
      <button className="button button-secondary" type="button" onClick={() => void fillFromTemplate()}>Preencher linhas</button>
      {templateError && <span className="save-status error">{templateError}</span>}
    </div>}
    <form onSubmit={submit} onKeyDown={handleFormKeyDown}>
      <div className="settings-grid">
        {!isEdit && <label><span>Tipo de documento</span><select data-field-key="doctype" ref={register('doctype')} value={documentTypeCode} onChange={(event) => { const code = event.target.value; setDocumentTypeCode(code); const chosen = documentTypes.find((item) => item.code === code); if (chosen) setJournalCode(chosen.defaultJournalCode) }}><option value="">Sem tipo · numerar pelo diário</option>{documentTypes.map((document) => <option key={document.code} value={document.code}>{document.code} · {document.name}</option>)}</select></label>}
        <label><span>Diário</span><select data-field-key="journal" ref={register('journal')} value={journalCode} onChange={(event) => setJournalCode(event.target.value)}>{journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
        <label><span>Data</span><input data-field-key="date" ref={register('date')} type="date" value={entryDate} onChange={(event) => setEntryDate(event.target.value)} /></label>
        <label><span>Documento</span><input data-field-key="document" ref={register('document')} value={documentReference} onChange={(event) => setDocumentReference(event.target.value)} placeholder="ex.: FT 2026/1" /></label>
        <label><span>Data do documento <small>(opcional)</small></span><input aria-label="Data do documento" type="date" value={documentDate} onChange={(event) => setDocumentDate(event.target.value)} /></label>
        <label><span>Descrição</span><input data-field-key="description" ref={register('description')} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label className="toggle-field"><input data-field-key="reviewed" ref={register('reviewed')} type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} /><span><strong>Conferido</strong><small>Caso contrário fica por conferir.</small></span></label>
      </div>

      <label className="field-block"><span>Observações <small>(opcional, uso interno)</small></span><textarea value={notes} rows={2} maxLength={2000} onChange={(event) => setNotes(event.target.value)} placeholder="Notas internas sobre este lançamento" /></label>

      <div className="table-wrap"><table><thead><tr><th>Conta</th><th>Terceiro</th><th>IVA</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>
        {lines.map((line, index) => <tr key={index}>
          <td><AccountField
            fieldKey={`line-${index}-account`}
            register={register}
            ariaLabel={`Conta da linha ${index + 1}`}
            accounts={postingAccounts}
            value={line.accountCode}
            invalid={isInvalidAccount(line.accountCode)}
            onChange={(code) => patchLine(index, line.vatCode ? { accountCode: code } : { accountCode: code, vatCode: proposedVatCode(code) })}
            onCommit={() => focusNextField(`line-${index}-account`)}
          /></td>
          <td><PartyField
            ariaLabel={`Terceiro da linha ${index + 1}`}
            search={searchParties}
            value={line.partyText}
            name={line.partyName}
            onChange={(text, name) => patchLine(index, { partyText: text, partyName: name })}
          /></td>
          <td><select aria-label={`Código de IVA da linha ${index + 1}`} className="vat-code-select" value={line.vatCode ?? ''} title={vatCodeList.find((code) => code.code === line.vatCode)?.name} onChange={(event) => patchLine(index, { vatCode: event.target.value })}>
            <option value="">—</option>
            {line.vatCode && !vatCodeList.some((code) => code.code === line.vatCode) && <option value={line.vatCode}>{line.vatCode}</option>}
            {vatCodeList.map((code) => <option key={code.code} value={code.code}>{code.code}</option>)}
          </select></td>
          <td><input data-field-key={`line-${index}-description`} ref={register(`line-${index}-description`)} aria-label={`Descrição da linha ${index + 1}`} value={line.description} onChange={(event) => patchLine(index, { description: event.target.value })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-debit`} ref={register(`line-${index}-debit`)} aria-label={`Débito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.debit || ''} onChange={(event) => patchLine(index, { debit: Number(event.target.value), credit: 0 })} /></td>
          <td className="numeric"><input data-field-key={`line-${index}-credit`} ref={register(`line-${index}-credit`)} aria-label={`Crédito da linha ${index + 1}`} type="number" step="0.01" min="0" value={line.credit || ''} onChange={(event) => patchLine(index, { credit: Number(event.target.value), debit: 0 })} /></td>
          <td>{lines.length > 2 && <button className="icon-button" type="button" aria-label={`Remover linha ${index + 1}`} onClick={() => setLines((current) => current.filter((_, position) => position !== index))}><Trash2 size={16} /></button>}</td>
        </tr>)}
      </tbody><tfoot><tr><td colSpan={4}><button className="button button-secondary" type="button" onClick={() => setLines((current) => [...current, emptyLine()])}><Plus size={16} /> Linha</button></td><td className="numeric strong">{euro.format(totalDebit)}</td><td className="numeric strong">{euro.format(totalCredit)}</td><td /></tr></tfoot></table></div>

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
