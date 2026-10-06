import { ArrowLeft, ChevronDown, ChevronRight, Copy, Database, Download, Pencil, Play, Plus, Save, Star, Trash2, X } from 'lucide-react'
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Chart, type ChartTone } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { signalLoadFailure } from '../app/loadFailure'
import {
  COUNT_MEASURE, DATE_TOKEN_LABELS, FORMAT_OPERATORS, MAX_GROUP_LEVELS, OPERATOR_LABELS, PARAMETER_TYPE_LABELS, SCOPE_LABELS, TONE_LABELS, toneOf,
  cloneDefinition, emptyDefinition, newCondition,
  type CatalogField, type CatalogSource, type ListingCatalog, type ListingColumn, type ListingDefinition, type ListingFilterGroup, type ListingTemplate,
  type ListingFormatRule, type ListingFormatTone, type ListingParameter, type ListingPivot, type ListingResult, type ListingRow, type ListingScope, type SavedListing,
} from '../app/listingLabels'
import { useWorkContext } from '../app/WorkContext'

const amount = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })
const PAGE = 200

async function refusal(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback
}

function format(column: ListingColumn, value: string | number | null): string {
  if (value === null || value === undefined || value === '') return ''
  if (column.type === 'money') return amount.format(Number(value))
  if (column.type === 'date') return new Date(`${String(value)}T00:00:00`).toLocaleDateString('pt-PT')
  return String(value)
}

/**
 * Uma listagem à medida (F4D-01): abre-se uma guardada (`/listagens/:id`) ou desenha-se uma
 * nova (`/listagens/nova`). A definição é só dados — origem, colunas, filtros, ordenação,
 * agrupamento e parâmetros —; o servidor gera a consulta. Os grupos abrem-se um nível de cada vez.
 */
export function ListingPage() {
  const { id, template: templateKey } = useParams()
  const navigate = useNavigate()
  const { activeCompany, user, source } = useWorkContext()
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/listings`

  const [catalog, setCatalog] = useState<ListingCatalog | null>(null)
  const [saved, setSaved] = useState<SavedListing | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [scope, setScope] = useState<ListingScope>('private')
  const [definition, setDefinition] = useState<ListingDefinition | null>(null)
  const [editing, setEditing] = useState(!id)
  const [values, setValues] = useState<Record<string, string>>({})
  const [result, setResult] = useState<ListingResult | null>(null)
  const [levels, setLevels] = useState<Record<string, ListingResult>>({})
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [running, setRunning] = useState(false)
  const [pivotSpec, setPivotSpec] = useState({ rowField: '', columnField: '', measure: '' })
  const [pivot, setPivot] = useState<ListingPivot | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const sourceInfo: CatalogSource | undefined = useMemo(
    () => catalog?.sources.find((item) => item.key === definition?.source) ?? catalog?.sources[0],
    [catalog, definition?.source],
  )
  const fieldOf = useCallback((key: string): CatalogField | undefined => sourceInfo?.fields.find((field) => field.key === key), [sourceInfo])

  // O catálogo e, se há identificador, a listagem guardada.
  useEffect(() => {
    if (source !== 'database') return
    let cancelled = false
    async function load() {
      const [catalogResponse, listResponse] = await Promise.all([fetch(`${root}/catalog`), fetch(root)])
      if (cancelled) return
      if (!catalogResponse.ok || !listResponse.ok) { signalLoadFailure(); setError('Não foi possível ler as listagens.'); return }
      const loaded = await catalogResponse.json() as ListingCatalog
      const list = await listResponse.json() as SavedListing[]
      if (cancelled) return
      setCatalog(loaded)
      if (templateKey) {
        const templates = await fetch(`${root}/templates`).then(async (response) => response.ok ? await response.json() as ListingTemplate[] : [])
        const found = templates.find((item) => item.key === templateKey)
        if (cancelled) return
        if (!found) { setError('O modelo já não existe.'); return }
        setSaved(null); setName(found.name); setDescription(found.description); setScope('private'); setDefinition(cloneDefinition(found.definition))
        setEditing(false)
      } else if (id) {
        const found = list.find((item) => item.id === id)
        if (!found) { setError('A listagem já não existe.'); return }
        setSaved(found); setName(found.name); setDescription(found.description ?? ''); setScope(found.scope); setDefinition(cloneDefinition(found.definition))
        setEditing(false)
      } else {
        setSaved(null); setName(''); setDescription(''); setScope('private'); setDefinition(emptyDefinition(loaded.sources[0]))
        setEditing(true)
      }
    }
    void load()
    return () => { cancelled = true }
  }, [root, id, templateKey, source])

  const post = useCallback(async (path: string, body: unknown): Promise<Response> =>
    fetch(`${root}/${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }), [root])

  const fetchLevel = useCallback(async (groupPath: (string | null)[], skip: number): Promise<ListingResult | null> => {
    if (!definition) return null
    const response = await post('run', { definition, parameters: values, groupPath, skip, take: PAGE })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível correr a listagem.')); return null }
    return await response.json() as ListingResult
  }, [definition, post, values])

  async function run() {
    setRunning(true); setError(''); setNotice(''); setLevels({}); setOpen(new Set())
    try { setResult(await fetchLevel([], 0)) } finally { setRunning(false) }
  }

  async function toggle(path: (string | null)[]) {
    const key = JSON.stringify(path)
    const next = new Set(open)
    if (next.has(key)) { next.delete(key); setOpen(next); return }
    next.add(key); setOpen(next)
    if (!levels[key]) {
      const level = await fetchLevel(path, 0)
      if (level) setLevels((current) => ({ ...current, [key]: level }))
    }
  }

  async function more(path: (string | null)[]) {
    const key = JSON.stringify(path)
    const current = path.length === 0 ? result : levels[key]
    if (!current) return
    const next = await fetchLevel(path, current.rows.length)
    if (!next) return
    const merged = { ...current, rows: [...current.rows, ...next.rows] }
    if (path.length === 0) setResult(merged); else setLevels((existing) => ({ ...existing, [key]: merged }))
  }

  /** A tabela cruzada: os filtros e os parâmetros da listagem, com as linhas e colunas escolhidas aqui. */
  async function runPivot() {
    if (!definition || !sourceInfo) return
    const groupable = sourceInfo.fields.filter((field) => field.groupable)
    const measures = sourceInfo.fields.filter((field) => field.measure)
    const spec = {
      rowField: pivotSpec.rowField || groupable[0]?.key || '',
      columnField: pivotSpec.columnField || groupable.find((field) => field.key !== (pivotSpec.rowField || groupable[0]?.key))?.key || '',
      measure: pivotSpec.measure || measures[0]?.key || COUNT_MEASURE,
    }
    setError(''); setPivot(null)
    const response = await post('pivot', { definition, parameters: values, ...spec })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível gerar a tabela cruzada.')); return }
    setPivot(await response.json() as ListingPivot)
  }

  function exportPivot() {
    if (!pivot) return
    const header = [fieldOf(pivot.rowField)?.label ?? pivot.rowField, ...pivot.columns.map((column) => column.label), 'Total']
    const body = pivot.rows.map((row, index) => [row.label, ...pivot.values[index], pivot.rowTotals[index]])
    downloadCsv(csvFilename('tabela-cruzada', activeCompany.legalName), toCsv([header, ...body, ['Total', ...pivot.columnTotals, pivot.grandTotal]]))
  }

  async function download(kind: 'xlsx' | 'pdf') {
    if (!definition) return
    setError('')
    const response = await post(`export?format=${kind}`, { definition, parameters: values, title: name || 'Listagem' })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível exportar.')); return }
    const url = URL.createObjectURL(await response.blob())
    const anchor = document.createElement('a')
    anchor.href = url; anchor.download = `${name || 'listagem'}.${kind}`
    document.body.appendChild(anchor); anchor.click(); anchor.remove(); URL.revokeObjectURL(url)
  }

  async function save(asCopy = false) {
    if (!definition) return
    setError(''); setNotice('')
    const copy = asCopy || !saved
    const response = await fetch(copy ? root : `${root}/${saved!.id}`, {
      method: copy ? 'POST' : 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scope, name: asCopy ? `${name} (cópia)` : name, description: description || null, definition }),
    })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível guardar a listagem.')); return }
    const result = await response.json() as SavedListing
    setNotice('Listagem guardada.')
    if (copy) navigate(`/listagens/${result.id}`); else setSaved(result)
  }

  async function remove() {
    if (!saved || !window.confirm(`Apagar a listagem «${saved.name}»?`)) return
    const response = await fetch(`${root}/${saved.id}`, { method: 'DELETE' })
    if (!response.ok) { setError('Não foi possível apagar a listagem.'); return }
    navigate('/listagens')
  }

  async function favorite() {
    if (!saved) return
    const response = await fetch(`${root}/${saved.id}/favorite?value=${!saved.isFavorite}`, { method: 'POST' })
    if (response.ok) setSaved({ ...saved, isFavorite: !saved.isFavorite })
  }

  const change = (patch: Partial<ListingDefinition>) => { if (definition) { setDefinition({ ...definition, ...patch }); setResult(null) } }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Listagens à medida</h1><p>As listagens precisam da base de dados; em demonstração não estão disponíveis.</p></div></header></div>
  }

  const parameters = definition?.parameters ?? []
  const canSave = !saved || saved.mine || saved.scope === 'company'

  const body = (path: (string | null)[], level: ListingResult, depth: number) => <>
    {level.kind === 'groups' && level.groups.map((group) => {
      const childPath = [...path, group.key]
      const key = JSON.stringify(childPath)
      const expanded = open.has(key)
      const child = levels[key]
      return <Fragment key={key}>
        <tr className="listing-group">
          <td colSpan={1}>
            <button type="button" className="link-button" style={{ paddingLeft: depth * 16 }} aria-expanded={expanded} onClick={() => void toggle(childPath)}>
              {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />} {fieldOf(level.groupBy[path.length])?.label}: <strong>{group.label}</strong> <small>({group.count})</small>
            </button>
          </td>
          {level.columns.slice(1).map((column) => <td key={column.key} className="numeric">{column.measure ? amount.format(group.sums[column.key] ?? 0) : ''}</td>)}
        </tr>
        {expanded && child && body(childPath, child, depth + 1)}
        {expanded && !child && <tr><td colSpan={level.columns.length}><small style={{ paddingLeft: (depth + 1) * 16 }}>A ler…</small></td></tr>}
      </Fragment>
    })}
    {level.kind === 'rows' && level.rows.map((row, index) => <tr key={`${JSON.stringify(path)}-${index}`}>
      {level.columns.map((column, position) => <td key={column.key} className={[column.type === 'money' || column.type === 'integer' ? 'numeric' : '', toneOf(definition?.formatting, column.key, row.cells[position]) ? `tone-${toneOf(definition?.formatting, column.key, row.cells[position])}` : ''].filter(Boolean).join(' ')} style={position === 0 ? { paddingLeft: depth * 16 } : undefined}>
        {linkCell(column, row, format(column, row.cells[position]))}
      </td>)}
    </tr>)}
    {level.kind === 'rows' && level.rows.length < level.totalRows && <tr><td colSpan={level.columns.length}>
      <button type="button" className="button button-secondary" onClick={() => void more(path)}>Mostrar mais ({level.totalRows - level.rows.length} por mostrar)</button>
    </td></tr>}
  </>

  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div>
        <p className="eyebrow"><Link to="/listagens"><ArrowLeft size={13} /> Listagens à medida</Link></p>
        <h1>{saved ? saved.name : templateKey ? name : 'Nova listagem'}</h1>
        <p>{sourceInfo ? `${sourceInfo.label}. ${sourceInfo.description}` : 'A ler…'}</p>
      </div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert">{error}</p>}
    {notice && <p className="save-status success" role="status">{notice}</p>}

    <section className="panel no-print">
      <div className="filterbar">
        {parameters.map((parameter) => <label key={parameter.name} className="inline-check">{parameter.label || parameter.name}
          <input
            aria-label={parameter.label || parameter.name}
            type={parameter.type === 'date' && /^\d{4}-\d{2}-\d{2}$/.test(values[parameter.name] ?? '') ? 'date' : 'text'}
            value={values[parameter.name] ?? ''}
            placeholder={parameter.default ? (DATE_TOKEN_LABELS[parameter.default] ?? parameter.default) : ''}
            onChange={(event) => setValues({ ...values, [parameter.name]: event.target.value })} />
        </label>)}
        <button type="button" className="button button-primary" disabled={!definition || running} onClick={() => void run()}><Play size={15} /> {running ? 'A correr…' : 'Executar'}</button>
        <button type="button" className="button button-secondary" onClick={() => setEditing(!editing)}><Pencil size={15} /> {editing ? 'Fechar o desenho' : 'Desenhar'}</button>
        <button type="button" className="button button-secondary" disabled={!result} onClick={() => void download('xlsx')}><Download size={15} /> Excel</button>
        <button type="button" className="button button-secondary" disabled={!result} onClick={() => void download('pdf')}><Download size={15} /> PDF</button>
        {saved && <button type="button" className="button button-secondary" aria-pressed={saved.isFavorite} disabled={!saved.mine} onClick={() => void favorite()}><Star size={15} /> {saved.isFavorite ? 'Favorita' : 'Marcar favorita'}</button>}
      </div>
    </section>

    {editing && definition && sourceInfo && <section className="panel no-print" aria-label="Desenho da listagem">
      <div className="panel-heading"><h2>Desenho</h2></div>
      <div className="filterbar">
        <label className="inline-check">Origem dos dados
          <select aria-label="Origem dos dados" value={definition.source} disabled={!!saved}
            onChange={(event) => {
              const next = catalog?.sources.find((item) => item.key === event.target.value)
              if (next) { setDefinition(emptyDefinition(next)); setResult(null); setValues({}) }
            }}>
            {(catalog?.sources ?? []).map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}
          </select>
        </label>
        <label className="inline-check">Nome <input aria-label="Nome da listagem" value={name} maxLength={120} onChange={(event) => setName(event.target.value)} /></label>
        <label className="inline-check">Quem a vê
          <select aria-label="Quem vê a listagem" value={scope} disabled={!!saved && !saved.mine} onChange={(event) => setScope(event.target.value as ListingScope)}>
            {(Object.keys(SCOPE_LABELS) as ListingScope[]).map((key) => <option key={key} value={key}>{SCOPE_LABELS[key]}</option>)}
          </select>
        </label>
        <label className="inline-check">Descrição <input aria-label="Descrição da listagem" value={description} maxLength={500} onChange={(event) => setDescription(event.target.value)} /></label>
      </div>

      <h3>Colunas</h3>
      <div className="listing-columns">
        {sourceInfo.fields.map((field) => <label key={field.key} className="inline-check">
          <input type="checkbox" checked={definition.columns.includes(field.key)}
            onChange={(event) => change({ columns: event.target.checked ? [...definition.columns, field.key] : definition.columns.filter((key) => key !== field.key) })} /> {field.label}
        </label>)}
      </div>

      <h3>Agrupar por</h3>
      <div className="filterbar">
        {Array.from({ length: Math.min(MAX_GROUP_LEVELS, (definition.groupBy?.length ?? 0) + 1) }, (_, level) => <label key={level} className="inline-check">{level + 1}.º nível
          <select aria-label={`Agrupamento, ${level + 1}.º nível`} value={definition.groupBy?.[level] ?? ''}
            onChange={(event) => {
              const next = [...(definition.groupBy ?? [])]
              if (event.target.value) next[level] = event.target.value; else next.splice(level)
              change({ groupBy: next })
            }}>
            <option value="">(nenhum)</option>
            {sourceInfo.fields.filter((field) => field.groupable && !(definition.groupBy ?? []).some((key, index) => key === field.key && index !== level)).map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
          </select>
        </label>)}
      </div>

      <h3>Ordenar por</h3>
      <div className="filterbar">
        {(definition.sort ?? []).map((item, index) => <span key={index} className="inline-check">
          <select aria-label={`Ordenação ${index + 1}`} value={item.field} onChange={(event) => change({ sort: (definition.sort ?? []).map((entry, position) => position === index ? { ...entry, field: event.target.value } : entry) })}>
            {sourceInfo.fields.filter((field) => field.sortable).map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
          </select>
          <label className="inline-check"><input type="checkbox" checked={item.descending} onChange={(event) => change({ sort: (definition.sort ?? []).map((entry, position) => position === index ? { ...entry, descending: event.target.checked } : entry) })} /> decrescente</label>
          <button type="button" className="icon-button" aria-label={`Tirar a ordenação ${index + 1}`} onClick={() => change({ sort: (definition.sort ?? []).filter((_, position) => position !== index) })}><X size={14} /></button>
        </span>)}
        {(definition.sort?.length ?? 0) < 5 && <button type="button" className="button button-secondary" onClick={() => change({ sort: [...(definition.sort ?? []), { field: sourceInfo.fields.find((field) => field.sortable)!.key, descending: false }] })}><Plus size={14} /> Ordenação</button>}
      </div>

      <h3>Filtros</h3>
      {definition.filter
        ? <GroupEditor group={definition.filter} fields={sourceInfo.fields} parameters={parameters} tokens={catalog?.dateTokens ?? []} onChange={(filter) => change({ filter })} onRemove={() => change({ filter: null })} depth={1} />
        : <button type="button" className="button button-secondary" onClick={() => change({ filter: { op: 'and', conditions: [], groups: [] } })}><Plus size={14} /> Filtrar</button>}

      <h3>Formatação condicional</h3>
      <FormatEditor rules={definition.formatting ?? []} columns={definition.columns.map((key) => fieldOf(key)).filter((field): field is CatalogField => !!field && field.type !== 'text')} onChange={(next) => change({ formatting: next })} />

      <h3>Parâmetros pedidos ao abrir</h3>
      <ParameterEditor parameters={parameters} tokens={catalog?.dateTokens ?? []} onChange={(next) => change({ parameters: next })} />

      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <button type="button" className="button button-primary" disabled={!canSave || !name.trim()} onClick={() => void save()}><Save size={15} /> Guardar</button>
        {saved && <button type="button" className="button button-secondary" disabled={!name.trim()} onClick={() => void save(true)}><Copy size={15} /> Guardar cópia</button>}
        {saved && saved.mine && <button type="button" className="button button-secondary" onClick={() => void remove()}><Trash2 size={15} /> Apagar</button>}
      </div>
    </section>}

    {definition && sourceInfo && <section className="panel no-print" aria-label="Tabela cruzada">
      <div className="panel-heading"><h2>Tabela cruzada</h2></div>
      <p className="statement-note">Cruza dois campos de agrupamento: um nas linhas e outro nas colunas, somando uma medida. Usa os filtros e os parâmetros desta listagem.</p>
      <div className="filterbar">
        <label className="inline-check">Linhas
          <select aria-label="Campo das linhas" value={pivotSpec.rowField} onChange={(event) => setPivotSpec({ ...pivotSpec, rowField: event.target.value })}>
            <option value="">(o primeiro)</option>
            {sourceInfo.fields.filter((field) => field.groupable).map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
          </select>
        </label>
        <label className="inline-check">Colunas
          <select aria-label="Campo das colunas" value={pivotSpec.columnField} onChange={(event) => setPivotSpec({ ...pivotSpec, columnField: event.target.value })}>
            <option value="">(o segundo)</option>
            {sourceInfo.fields.filter((field) => field.groupable).map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
          </select>
        </label>
        <label className="inline-check">Medida
          <select aria-label="Medida da tabela cruzada" value={pivotSpec.measure} onChange={(event) => setPivotSpec({ ...pivotSpec, measure: event.target.value })}>
            <option value="">(a primeira)</option>
            {sourceInfo.fields.filter((field) => field.measure).map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
            <option value={COUNT_MEASURE}>Número de linhas</option>
          </select>
        </label>
        <button type="button" className="button button-primary" onClick={() => void runPivot()}>Gerar tabela cruzada</button>
        <ExportButtons run={exportPivot} disabled={!pivot} />
      </div>
      {pivot && <>
        <div className="table-wrap"><table className="dense-table" aria-label="Resultado da tabela cruzada">
          <thead><tr><th>{fieldOf(pivot.rowField)?.label} \ {fieldOf(pivot.columnField)?.label}</th>{pivot.columns.map((column) => <th key={column.label} className="numeric">{column.label}</th>)}<th className="numeric">Total</th></tr></thead>
          <tbody>{pivot.rows.map((row, index) => <tr key={row.label}>
            <td>{row.label}</td>
            {pivot.values[index].map((value, position) => <td key={position} className="numeric">{pivot.measure === COUNT_MEASURE ? value : amount.format(value)}</td>)}
            <td className="numeric"><strong>{pivot.measure === COUNT_MEASURE ? pivot.rowTotals[index] : amount.format(pivot.rowTotals[index])}</strong></td>
          </tr>)}</tbody>
          <tfoot><tr><td><strong>Total</strong></td>
            {pivot.columnTotals.map((value, position) => <td key={position} className="numeric"><strong>{pivot.measure === COUNT_MEASURE ? value : amount.format(value)}</strong></td>)}
            <td className="numeric"><strong>{pivot.measure === COUNT_MEASURE ? pivot.grandTotal : amount.format(pivot.grandTotal)}</strong></td></tr></tfoot>
        </table></div>
        {pivot.columns.length > 1 && <Chart
          title={`${pivot.measureLabel} por ${fieldOf(pivot.columnField)?.label ?? ''}`}
          labels={pivot.columns.map((column) => column.label)}
          series={pivot.rows
            .map((row, index) => ({ row, index, weight: Math.abs(pivot.rowTotals[index]) }))
            .sort((a, b) => b.weight - a.weight)
            .slice(0, 5)
            .map((item, position) => ({ name: item.row.label, kind: 'bar' as const, tone: (['primary', 'accent', 'warning', 'danger', 'muted'] as ChartTone[])[position], values: pivot.values[item.index] }))}
          format={(value) => pivot.measure === COUNT_MEASURE ? String(value) : amount.format(value)} />}
      </>}
    </section>}

    {result && <section className="panel" aria-label="Resultado">
      <div className="panel-heading"><h2>{result.totalRows.toLocaleString('pt-PT')} linhas</h2></div>
      <div className="table-wrap"><table className="dense-table">
        <thead><tr>{result.columns.map((column) => <th key={column.key} className={column.type === 'money' || column.type === 'integer' ? 'numeric' : ''}>{column.label}</th>)}</tr></thead>
        <tbody>{body([], result, 0)}</tbody>
        <tfoot><tr>{result.columns.map((column, position) => <td key={column.key} className="numeric"><strong>{position === 0 ? 'Total' : column.measure ? amount.format(result.totals[column.key] ?? 0) : ''}</strong></td>)}</tr></tfoot>
      </table></div>
    </section>}
    {!result && definition && !running && <p className="empty-state">Escolha os parâmetros, se houver, e carregue em «Executar».</p>}
  </div>
}

/** Uma célula: o lançamento, o terceiro e a conta ligam ao registo de onde vieram. */
function linkCell(column: ListingColumn, row: ListingRow, text: string) {
  if (text === '') return text
  if (column.key === 'lancamento' && row.link.entryReference) return <Link to={`/movimentos/${encodeURIComponent(row.link.entryReference)}`}>{text}</Link>
  if (column.key === 'terceiro' && row.link.partyId) return <Link to={`/terceiros/${row.link.partyId}`}>{text}</Link>
  if (column.key === 'conta') return <Link to={`/contas/${encodeURIComponent(text)}`}>{text}</Link>
  return text
}

type GroupEditorProps = {
  group: ListingFilterGroup
  fields: CatalogField[]
  parameters: ListingParameter[]
  tokens: string[]
  depth: number
  onChange: (group: ListingFilterGroup) => void
  onRemove: () => void
}

/** Condições ligadas por «e» ou «ou», com grupos dentro de grupos (os parêntesis). */
function GroupEditor({ group, fields, parameters, tokens, depth, onChange, onRemove }: GroupEditorProps) {
  const filterable = fields.filter((field) => field.filterable)
  const conditions = group.conditions ?? []
  const groups = group.groups ?? []
  const suggestions = [...parameters.map((parameter) => `@${parameter.name}`), ...tokens]
  const listId = `valores-${depth}-${parameters.length}`
  return <div className="listing-group-editor" style={{ marginLeft: (depth - 1) * 12 }}>
    <div className="filterbar">
      <label className="inline-check">Todas as condições
        <select aria-label="Ligação das condições" value={group.op} onChange={(event) => onChange({ ...group, op: event.target.value as 'and' | 'or' })}>
          <option value="and">têm de se verificar (e)</option>
          <option value="or">basta que uma se verifique (ou)</option>
        </select>
      </label>
      <button type="button" className="icon-button" aria-label="Tirar o grupo de filtros" onClick={onRemove}><X size={14} /></button>
    </div>
    <datalist id={listId}>{suggestions.map((value) => <option key={value} value={value} />)}</datalist>
    {conditions.map((condition, index) => {
      const field = filterable.find((item) => item.key === condition.field) ?? filterable[0]
      const update = (patch: Partial<typeof condition>) => onChange({ ...group, conditions: conditions.map((entry, position) => position === index ? { ...entry, ...patch } : entry) })
      return <div key={index} className="filterbar">
        <select aria-label={`Campo da condição ${index + 1}`} value={field.key} onChange={(event) => {
          const next = filterable.find((item) => item.key === event.target.value)!
          update({ field: next.key, operator: next.operators.includes(condition.operator) ? condition.operator : next.operators[0] })
        }}>{filterable.map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select>
        <select aria-label={`Operador da condição ${index + 1}`} value={condition.operator} onChange={(event) => update({ operator: event.target.value })}>
          {field.operators.map((operator) => <option key={operator} value={operator}>{OPERATOR_LABELS[operator] ?? operator}</option>)}
        </select>
        {condition.operator !== 'empty' && condition.operator !== 'notempty' &&
          <input aria-label={`Valor da condição ${index + 1}`} list={listId} value={condition.value ?? ''} placeholder={field.type === 'date' ? 'AAAA-MM-DD, @parâmetro…' : '@parâmetro ou valor'} onChange={(event) => update({ value: event.target.value })} />}
        {condition.operator === 'between' &&
          <input aria-label={`Segundo valor da condição ${index + 1}`} list={listId} value={condition.value2 ?? ''} placeholder="e" onChange={(event) => update({ value2: event.target.value })} />}
        <button type="button" className="icon-button" aria-label={`Tirar a condição ${index + 1}`} onClick={() => onChange({ ...group, conditions: conditions.filter((_, position) => position !== index) })}><X size={14} /></button>
      </div>
    })}
    {groups.map((child, index) => <GroupEditor key={index} group={child} fields={fields} parameters={parameters} tokens={tokens} depth={depth + 1}
      onChange={(next) => onChange({ ...group, groups: groups.map((entry, position) => position === index ? next : entry) })}
      onRemove={() => onChange({ ...group, groups: groups.filter((_, position) => position !== index) })} />)}
    <div className="filterbar">
      <button type="button" className="button button-secondary" onClick={() => onChange({ ...group, conditions: [...conditions, newCondition(filterable[0])] })}><Plus size={14} /> Condição</button>
      {depth < 4 && <button type="button" className="button button-secondary" onClick={() => onChange({ ...group, groups: [...groups, { op: 'or', conditions: [newCondition(filterable[0])], groups: [] }] })}><Plus size={14} /> Grupo</button>}
    </div>
  </div>
}

function ParameterEditor({ parameters, tokens, onChange }: { parameters: ListingParameter[]; tokens: string[]; onChange: (parameters: ListingParameter[]) => void }) {
  const update = (index: number, patch: Partial<ListingParameter>) => onChange(parameters.map((entry, position) => position === index ? { ...entry, ...patch } : entry))
  return <div>
    {parameters.map((parameter, index) => <div key={index} className="filterbar">
      <label className="inline-check">Nome <input aria-label={`Nome do parâmetro ${index + 1}`} value={parameter.name} maxLength={30} onChange={(event) => update(index, { name: event.target.value.replace(/[^A-Za-z0-9]/g, '') })} /></label>
      <label className="inline-check">Pergunta <input aria-label={`Pergunta do parâmetro ${index + 1}`} value={parameter.label} onChange={(event) => update(index, { label: event.target.value })} /></label>
      <label className="inline-check">Tipo
        <select aria-label={`Tipo do parâmetro ${index + 1}`} value={parameter.type} onChange={(event) => update(index, { type: event.target.value as ListingParameter['type'], default: '' })}>
          {(Object.keys(PARAMETER_TYPE_LABELS) as ListingParameter['type'][]).map((type) => <option key={type} value={type}>{PARAMETER_TYPE_LABELS[type]}</option>)}
        </select>
      </label>
      <label className="inline-check">Valor por omissão
        {parameter.type === 'date'
          ? <select aria-label={`Valor por omissão do parâmetro ${index + 1}`} value={parameter.default ?? ''} onChange={(event) => update(index, { default: event.target.value })}>
            <option value="">(nenhum)</option>
            {tokens.map((token) => <option key={token} value={token}>{DATE_TOKEN_LABELS[token] ?? token}</option>)}
          </select>
          : <input aria-label={`Valor por omissão do parâmetro ${index + 1}`} value={parameter.default ?? ''} onChange={(event) => update(index, { default: event.target.value })} />}
      </label>
      <button type="button" className="icon-button" aria-label={`Tirar o parâmetro ${index + 1}`} onClick={() => onChange(parameters.filter((_, position) => position !== index))}><X size={14} /></button>
    </div>)}
    {parameters.length < 10 && <button type="button" className="button button-secondary" onClick={() => onChange([...parameters, { name: `p${parameters.length + 1}`, label: '', type: 'date', default: '' }])}><Plus size={14} /> Parâmetro</button>}
  </div>
}

function FormatEditor({ rules, columns, onChange }: { rules: ListingFormatRule[]; columns: CatalogField[]; onChange: (rules: ListingFormatRule[]) => void }) {
  const update = (index: number, patch: Partial<ListingFormatRule>) => onChange(rules.map((entry, position) => position === index ? { ...entry, ...patch } : entry))
  return <div>
    {rules.map((rule, index) => <div key={index} className="filterbar">
      <select aria-label={`Coluna da regra ${index + 1}`} value={rule.column} onChange={(event) => update(index, { column: event.target.value })}>
        {columns.map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}
      </select>
      <select aria-label={`Operador da regra ${index + 1}`} value={rule.operator} onChange={(event) => update(index, { operator: event.target.value })}>
        {FORMAT_OPERATORS.map((operator) => <option key={operator} value={operator}>{OPERATOR_LABELS[operator]}</option>)}
      </select>
      <input aria-label={`Valor da regra ${index + 1}`} value={rule.value} onChange={(event) => update(index, { value: event.target.value })} />
      <select aria-label={`Cor da regra ${index + 1}`} value={rule.tone} onChange={(event) => update(index, { tone: event.target.value as ListingFormatTone })}>
        {(Object.keys(TONE_LABELS) as ListingFormatTone[]).map((tone) => <option key={tone} value={tone}>{TONE_LABELS[tone]}</option>)}
      </select>
      <button type="button" className="icon-button" aria-label={`Tirar a regra ${index + 1}`} onClick={() => onChange(rules.filter((_, position) => position !== index))}><X size={14} /></button>
    </div>)}
    {rules.length < 10 && columns.length > 0 && <button type="button" className="button button-secondary" onClick={() => onChange([...rules, { column: columns[0].key, operator: 'lt', value: '0', tone: 'danger' }])}><Plus size={14} /> Regra</button>}
  </div>
}
