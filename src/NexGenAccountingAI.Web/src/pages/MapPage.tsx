import { ArrowDown, ArrowUp, ChevronLeft, Database, Pencil, Play, Plus, Save, Trash2, TriangleAlert, X } from 'lucide-react'
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { signalLoadFailure } from '../app/loadFailure'
import {
  COLUMN_KIND_LABELS, MEASURE_LABELS, MONTH_NAMES, ROW_KIND_LABELS, SCOPE_LABELS, SHOW_LABELS, SIGN_LABELS,
  emptyColumn, emptyRow, nextCode,
  type MapColumn, type MapColumnKind, type MapDefinition, type MapMeasure, type MapRow, type MapRowKind, type MapScope, type MapShow, type MapSign,
} from '../app/mapLabels'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

type ResultRow = { code: string; label: string; kind: string; level: number; bold: boolean; values: (number | null)[] }
type MapResult = { columns: MapColumn[]; rows: ResultRow[]; warnings: string[] }
type Contribution = { accountCode: string; accountName: string; values: (number | null)[] }
type ColumnSet = { key: string; label: string; description: string; columns: MapColumn[] }
type Loaded = { name: string; description: string; definition: MapDefinition; excludeClosing: boolean; savedId: string | null }

const amount = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })
const percent = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1, useGrouping: 'always' })

function cell(value: number | null | undefined, isPercent: boolean): string {
  if (value === null || value === undefined) return ''
  if (Math.round(value * 100) === 0) return '–'
  return isPercent ? `${percent.format(value)} %` : amount.format(value)
}

async function readRefusal(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback
}

/**
 * Um mapa à medida (F4B-03): lê-se, explica-se linha a linha, e desenha-se. Abre um
 * mapa guardado (`/mapas/:id`), um modelo do sistema (`/mapas/modelo/:key`) ou um
 * mapa em branco (`/mapas/novo`).
 */
export function MapPage() {
  const { id, template: templateKey } = useParams()
  const navigate = useNavigate()
  const { activeCompany, user } = useWorkContext()
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/maps`

  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [columnSets, setColumnSets] = useState<ColumnSet[]>([])
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [definition, setDefinition] = useState<MapDefinition>({ rows: [], columns: [] })
  const [year, setYear] = useState(fiscalYear)
  const [fromMonth, setFromMonth] = useState(1)
  const [toMonth, setToMonth] = useState(12)
  const [withClosing, setWithClosing] = useState(false)
  const [editing, setEditing] = useState(false)
  const [result, setResult] = useState<MapResult | null>(null)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [explained, setExplained] = useState<{ row: ResultRow; accounts: Contribution[] } | null>(null)

  useEffect(() => setYear(fiscalYear), [fiscalYear, activeCompany.companyId])

  // O mapa de partida: guardado, modelo ou em branco.
  useEffect(() => {
    let cancelled = false
    async function load() {
      const listing = await fetch(root).then(async (response) => response.ok ? await response.json() as { maps: (Loaded & { id: string })[]; columnSets: ColumnSet[] } : null)
      if (cancelled) return
      if (!listing) { signalLoadFailure(); setError('Não foi possível ler os mapas.'); return }
      setColumnSets(listing.columnSets)
      let start: Loaded
      if (id) {
        const saved = listing.maps.find((map) => map.id === id)
        if (!saved) { setError('O mapa já não existe.'); return }
        start = { name: saved.name, description: saved.description ?? '', definition: saved.definition, excludeClosing: true, savedId: saved.id }
      } else if (templateKey) {
        const template = await fetch(`${root}/templates/${templateKey}`).then(async (response) => response.ok ? await response.json() as { name: string; description: string; excludeClosing: boolean; definition: MapDefinition } : null)
        if (cancelled) return
        if (!template) { setError('O modelo não existe.'); return }
        start = { name: template.name, description: template.description, definition: template.definition, excludeClosing: template.excludeClosing, savedId: null }
      } else {
        const columns = listing.columnSets.find((set) => set.key === 'periodo')?.columns ?? []
        start = { name: '', description: '', definition: { rows: [emptyRow('L10')], columns }, excludeClosing: true, savedId: null }
        setEditing(true)
      }
      setLoaded(start)
      setName(start.name)
      setDescription(start.description)
      setDefinition(start.definition)
      setWithClosing(!start.excludeClosing)
    }
    void load()
    return () => { cancelled = true }
  }, [id, root, templateKey])

  const run = useCallback(async (current: MapDefinition) => {
    setRunning(true)
    setError('')
    setExplained(null)
    try {
      const response = await fetch(`${root}/run`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ definition: current, year, fromMonth, toMonth, includeClosing: withClosing }),
      })
      if (!response.ok) throw new Error(await readRefusal(response, 'Não foi possível calcular o mapa.'))
      setResult(await response.json() as MapResult)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o mapa.')
    } finally {
      setRunning(false)
    }
  }, [fromMonth, root, toMonth, withClosing, year])

  // Corre sozinho quando o mapa abre e quando o período muda; no desenhador só a pedido.
  useEffect(() => {
    if (loaded && !editing && loaded.definition.rows.length > 0) void run(loaded.definition)
  }, [editing, loaded, run])

  async function explain(row: ResultRow) {
    if (row.kind !== 'Accounts') return
    if (explained?.row.code === row.code) { setExplained(null); return }
    const response = await fetch(`${root}/run`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ definition, year, fromMonth, toMonth, includeClosing: withClosing, rowCode: row.code }),
    })
    if (!response.ok) { setError(await readRefusal(response, 'Não foi possível explicar a linha.')); return }
    setExplained({ row, accounts: await response.json() as Contribution[] })
  }

  async function save() {
    setError('')
    setNotice('')
    const body = JSON.stringify({ name, description: description || null, definition })
    const response = await fetch(loaded?.savedId ? `${root}/${loaded.savedId}` : root, {
      method: loaded?.savedId ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body,
    })
    if (!response.ok) { setError(await readRefusal(response, 'Não foi possível guardar o mapa.')); return }
    const saved = await response.json() as { id: string; name: string }
    setNotice(`Mapa «${saved.name}» guardado.`)
    setLoaded({ name, description, definition, excludeClosing: !withClosing, savedId: saved.id })
    setEditing(false)
    if (!loaded?.savedId) navigate(`/mapas/${saved.id}`, { replace: true })
  }

  function updateRow(index: number, patch: Partial<MapRow>) {
    setDefinition((current) => ({ ...current, rows: current.rows.map((row, position) => position === index ? { ...row, ...patch } : row) }))
  }
  function updateColumn(index: number, patch: Partial<MapColumn>) {
    setDefinition((current) => ({ ...current, columns: current.columns.map((column, position) => position === index ? { ...column, ...patch } : column) }))
  }
  function move<T>(list: T[], index: number, delta: number): T[] {
    const target = index + delta
    if (target < 0 || target >= list.length) return list
    const next = [...list]
    const [item] = next.splice(index, 1)
    next.splice(target, 0, item)
    return next
  }

  const columns = result?.columns ?? definition.columns
  const periodLabel = fromMonth === toMonth ? `${MONTH_NAMES[fromMonth - 1]} de ${year}` : `${MONTH_NAMES[fromMonth - 1]} a ${MONTH_NAMES[toMonth - 1]} de ${year}`
  const dirty = useMemo(() => loaded !== null && (name !== loaded.name || description !== loaded.description || JSON.stringify(definition) !== JSON.stringify(loaded.definition)), [definition, description, loaded, name])

  function exportRows() {
    if (!result) return
    const body: (string | number)[][] = [['Código', 'Descrição', ...result.columns.map((column) => column.label)]]
    for (const row of result.rows) body.push([row.code, `${'  '.repeat(row.level)}${row.label}`, ...row.values.map((value) => value ?? '')])
    downloadCsv(csvFilename(name || 'mapa', activeCompany.legalName), toCsv(body))
  }

  const from = `${year}-${String(fromMonth).padStart(2, '0')}-01`
  const to = new Date(Date.UTC(year, toMonth, 0)).toISOString().slice(0, 10)

  return <div className="page-stack report-page map-page">
    <header className="page-heading detail-heading">
      <div>
        <p className="eyebrow"><Link to="/mapas" className="text-link"><ChevronLeft size={13} /> Mapas à medida</Link></p>
        <h1>{name || 'Novo mapa'}</h1>
        <p>{description || `Mapa calculado sobre a contabilidade — ${periodLabel}.`}</p>
      </div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    <ReportChrome reportKey={`mapa-${loaded?.savedId ?? templateKey ?? 'novo'}`} title={name || 'Mapa'} from={from} to={to} />
    <div className="filterbar balancete-filters no-print">
      <label className="balancete-field"><span>Exercício</span><input type="number" min={1990} max={2100} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 1990 && next <= 2100) setYear(next) }} /></label>
      <label className="balancete-field"><span>De</span><select value={fromMonth} onChange={(event) => { const next = Number(event.target.value); setFromMonth(next); if (next > toMonth) setToMonth(next) }}>{MONTH_NAMES.map((month, index) => <option key={month} value={index + 1}>{month}</option>)}</select></label>
      <label className="balancete-field"><span>A</span><select value={toMonth} onChange={(event) => { const next = Number(event.target.value); setToMonth(next); if (next < fromMonth) setFromMonth(next) }}>{MONTH_NAMES.map((month, index) => <option key={month} value={index + 1}>{month}</option>)}</select></label>
      <label className="balancete-field"><span>Apuramento</span><select value={withClosing ? 'com' : 'sem'} onChange={(event) => setWithClosing(event.target.value === 'com')}>
        <option value="sem">Antes do apuramento</option>
        <option value="com">Com o apuramento de resultados</option>
      </select></label>
      <span className="map-actions">
        {!editing && <button type="button" className="button button-secondary" onClick={() => setEditing(true)}><Pencil size={15} /> Desenhar</button>}
        {editing && <button type="button" className="button button-secondary" disabled={running} onClick={() => void run(definition)}><Play size={15} /> {running ? 'A calcular…' : 'Testar'}</button>}
        {(editing || dirty || !loaded?.savedId) && <button type="button" className="button button-primary" disabled={!name.trim()} onClick={() => void save()}><Save size={15} /> {loaded?.savedId ? 'Guardar' : 'Guardar como mapa da empresa'}</button>}
        {editing && <button type="button" className="button button-secondary" onClick={() => { setEditing(false); if (loaded) { setDefinition(loaded.definition); setName(loaded.name); setDescription(loaded.description) } }}><X size={15} /> Fechar o desenhador</button>}
        <ExportButtons run={exportRows} disabled={!result || result.rows.length === 0} />
      </span>
    </div>
    {notice && <p className="save-status success">{notice}</p>}
    {error && <p className="save-status error" role="alert">{error}</p>}
    {result?.warnings.map((warning) => <p key={warning} className="save-status balancete-warning"><TriangleAlert size={15} /> {warning}</p>)}

    {editing && <section className="panel map-designer no-print">
      <div className="builder-grid">
        <label className="field-block"><span>Nome do mapa</span><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Por exemplo: Demonstração de resultados resumida" /></label>
        <label className="field-block"><span>Descrição</span><input value={description} onChange={(event) => setDescription(event.target.value)} /></label>
      </div>
      <fieldset className="builder-section">
        <legend>Linhas</legend>
        <p className="map-help">Uma linha de contas soma as contas que começam pelos prefixos indicados (<code>61 + 62 - 6221</code>, intervalos <code>61..63</code>). Uma linha de fórmula calcula a partir dos códigos de outras linhas (<code>L10 - L20</code>; <code>A % B</code> é A em percentagem de B). O sinal invertido faz os rendimentos saírem positivos; uma linha oculta entra nas fórmulas sem aparecer.</p>
        <div className="table-wrap"><table className="map-editor">
          <thead><tr><th>Código</th><th>Descrição</th><th>Tipo</th><th>Contas ou fórmula</th><th>Medida</th><th>Sinal</th><th>Mostrar</th><th>Nível</th><th>Negrito</th><th>Oculta</th><th className="sr-only">Ações</th></tr></thead>
          <tbody>{definition.rows.map((row, index) => <tr key={index}>
            <td><input aria-label={`Código da linha ${index + 1}`} value={row.code} onChange={(event) => updateRow(index, { code: event.target.value.toUpperCase() })} /></td>
            <td><input aria-label={`Descrição da linha ${row.code}`} value={row.label} onChange={(event) => updateRow(index, { label: event.target.value })} /></td>
            <td><select aria-label={`Tipo da linha ${row.code}`} value={row.kind} onChange={(event) => updateRow(index, { kind: event.target.value as MapRowKind })}>{Object.entries(ROW_KIND_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></td>
            <td>{row.kind === 'accounts' && <input aria-label={`Contas da linha ${row.code}`} value={row.accounts ?? ''} onChange={(event) => updateRow(index, { accounts: event.target.value })} placeholder="71 + 72" />}
              {row.kind === 'formula' && <input aria-label={`Fórmula da linha ${row.code}`} value={row.formula ?? ''} onChange={(event) => updateRow(index, { formula: event.target.value })} placeholder="L10 - L20" />}</td>
            <td>{row.kind === 'accounts' && <select aria-label={`Medida da linha ${row.code}`} value={row.measure} onChange={(event) => updateRow(index, { measure: event.target.value as MapMeasure })}>{Object.entries(MEASURE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>}</td>
            <td>{row.kind !== 'title' && <select aria-label={`Sinal da linha ${row.code}`} value={row.sign} onChange={(event) => updateRow(index, { sign: event.target.value as MapSign })}>{Object.entries(SIGN_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>}</td>
            <td>{row.kind !== 'title' && <select aria-label={`Quando mostrar a linha ${row.code}`} value={row.show} onChange={(event) => updateRow(index, { show: event.target.value as MapShow })}>{Object.entries(SHOW_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>}</td>
            <td><input aria-label={`Nível da linha ${row.code}`} type="number" min={0} max={6} value={row.level} onChange={(event) => updateRow(index, { level: Number(event.target.value) })} className="map-level" /></td>
            <td><input aria-label={`Linha ${row.code} a negrito`} type="checkbox" checked={row.bold} onChange={(event) => updateRow(index, { bold: event.target.checked })} /></td>
            <td><input aria-label={`Linha ${row.code} oculta`} type="checkbox" checked={row.hidden} onChange={(event) => updateRow(index, { hidden: event.target.checked })} /></td>
            <td className="map-row-actions">
              <button type="button" className="icon-button" aria-label={`Subir a linha ${row.code}`} onClick={() => setDefinition((current) => ({ ...current, rows: move(current.rows, index, -1) }))}><ArrowUp size={14} /></button>
              <button type="button" className="icon-button" aria-label={`Descer a linha ${row.code}`} onClick={() => setDefinition((current) => ({ ...current, rows: move(current.rows, index, 1) }))}><ArrowDown size={14} /></button>
              <button type="button" className="icon-button" aria-label={`Apagar a linha ${row.code}`} onClick={() => setDefinition((current) => ({ ...current, rows: current.rows.filter((_, position) => position !== index) }))}><Trash2 size={14} /></button>
            </td>
          </tr>)}</tbody>
        </table></div>
        <button type="button" className="button button-secondary" onClick={() => setDefinition((current) => ({ ...current, rows: [...current.rows, emptyRow(nextCode('L', current.rows))] }))}><Plus size={15} /> Acrescentar linha</button>
      </fieldset>
      <fieldset className="builder-section">
        <legend>Colunas</legend>
        <p className="map-help">Parta de um conjunto pronto e altere o que precisar. Uma coluna de valores lê o período pedido, o acumulado do exercício, o saldo inicial ou um mês fixo, deste exercício ou de um anterior; a medida da coluna, se preenchida, substitui a das linhas (as colunas «Débito» e «Crédito» de um balancete). Uma coluna de fórmula calcula a partir dos códigos das outras.</p>
        <div className="builder-row">
          <label className="toggle-inline">Conjunto pronto
            <select aria-label="Conjunto de colunas" value="" onChange={(event) => { const set = columnSets.find((item) => item.key === event.target.value); if (set) setDefinition((current) => ({ ...current, columns: set.columns })) }}>
              <option value="">Escolher…</option>
              {columnSets.map((set) => <option key={set.key} value={set.key}>{set.label}</option>)}
            </select>
          </label>
        </div>
        <div className="table-wrap"><table className="map-editor">
          <thead><tr><th>Código</th><th>Descrição</th><th>Tipo</th><th>Âmbito</th><th>Exercício</th><th>Mês</th><th>Fórmula</th><th>Medida</th><th>%</th><th className="sr-only">Ações</th></tr></thead>
          <tbody>{definition.columns.map((column, index) => <tr key={index}>
            <td><input aria-label={`Código da coluna ${index + 1}`} value={column.code} onChange={(event) => updateColumn(index, { code: event.target.value.toUpperCase() })} /></td>
            <td><input aria-label={`Descrição da coluna ${column.code}`} value={column.label} onChange={(event) => updateColumn(index, { label: event.target.value })} /></td>
            <td><select aria-label={`Tipo da coluna ${column.code}`} value={column.kind} onChange={(event) => updateColumn(index, { kind: event.target.value as MapColumnKind })}>{Object.entries(COLUMN_KIND_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></td>
            <td>{column.kind === 'value' && <select aria-label={`Âmbito da coluna ${column.code}`} value={column.scope} onChange={(event) => updateColumn(index, { scope: event.target.value as MapScope, month: event.target.value === 'month' ? (column.month ?? 1) : null })}>{Object.entries(SCOPE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>}</td>
            <td>{column.kind === 'value' && <select aria-label={`Exercício da coluna ${column.code}`} value={column.yearOffset} onChange={(event) => updateColumn(index, { yearOffset: Number(event.target.value) })}>
              <option value={0}>Pedido</option><option value={-1}>Anterior</option><option value={-2}>Há dois anos</option><option value={-3}>Há três anos</option>
            </select>}</td>
            <td>{column.kind === 'value' && column.scope === 'month' && <select aria-label={`Mês da coluna ${column.code}`} value={column.month ?? 1} onChange={(event) => updateColumn(index, { month: Number(event.target.value) })}>{MONTH_NAMES.map((month, position) => <option key={month} value={position + 1}>{month}</option>)}</select>}</td>
            <td>{column.kind === 'formula' && <input aria-label={`Fórmula da coluna ${column.code}`} value={column.formula ?? ''} onChange={(event) => updateColumn(index, { formula: event.target.value })} placeholder="N - N1" />}</td>
            <td>{column.kind === 'value' && <select aria-label={`Medida da coluna ${column.code}`} value={column.measure ?? ''} onChange={(event) => updateColumn(index, { measure: (event.target.value || null) as MapMeasure | null })}>
              <option value="">A das linhas</option>{Object.entries(MEASURE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>}</td>
            <td><input aria-label={`Coluna ${column.code} em percentagem`} type="checkbox" checked={column.percent} onChange={(event) => updateColumn(index, { percent: event.target.checked })} /></td>
            <td className="map-row-actions">
              <button type="button" className="icon-button" aria-label={`Mover a coluna ${column.code} para a esquerda`} onClick={() => setDefinition((current) => ({ ...current, columns: move(current.columns, index, -1) }))}><ArrowUp size={14} /></button>
              <button type="button" className="icon-button" aria-label={`Mover a coluna ${column.code} para a direita`} onClick={() => setDefinition((current) => ({ ...current, columns: move(current.columns, index, 1) }))}><ArrowDown size={14} /></button>
              <button type="button" className="icon-button" aria-label={`Apagar a coluna ${column.code}`} onClick={() => setDefinition((current) => ({ ...current, columns: current.columns.filter((_, position) => position !== index) }))}><Trash2 size={14} /></button>
            </td>
          </tr>)}</tbody>
        </table></div>
        <button type="button" className="button button-secondary" onClick={() => setDefinition((current) => ({ ...current, columns: [...current.columns, emptyColumn(nextCode('C', current.columns, 1))] }))}><Plus size={15} /> Acrescentar coluna</button>
      </fieldset>
    </section>}

    <section className="panel"><div className="table-wrap"><table className={`map-table ${running ? 'is-loading' : ''}`}>
      <thead><tr><th>Rubrica</th>{columns.map((column) => <th key={column.code} className="numeric">{column.label}</th>)}</tr></thead>
      <tbody>{(result?.rows ?? []).map((row) => <Fragment key={row.code}>
        <tr className={`map-row-${row.kind.toLowerCase()} ${row.bold ? 'map-row-bold' : ''} ${explained?.row.code === row.code ? 'map-row-open' : ''}`} onClick={() => void explain(row)} title={row.kind === 'Accounts' ? 'Ver as contas desta linha' : undefined}>
          <td style={{ paddingLeft: 12 + row.level * 16 }}>{row.label}</td>
          {row.values.map((value, index) => <td key={columns[index]?.code ?? index} className={`numeric ${value !== null && value < 0 ? 'negative' : ''}`}>{cell(value, columns[index]?.percent ?? false)}</td>)}
        </tr>
        {explained?.row.code === row.code && <tr className="map-explain"><td colSpan={columns.length + 1}>
          <div className="map-explain-head"><strong>Contas de «{row.label}»</strong> · {explained.accounts.length} com valores</div>
          {explained.accounts.length === 0 ? <p className="empty-state">Nenhuma conta com valores neste período.</p> : <table>
            <thead><tr><th>Conta</th><th>Descrição</th>{columns.map((column) => <th key={column.code} className="numeric">{column.label}</th>)}</tr></thead>
            <tbody>{explained.accounts.map((account) => <tr key={account.accountCode}>
              <td><Link className="primary-cell" to={`/contas/${account.accountCode}`}>{account.accountCode}</Link></td><td>{account.accountName}</td>
              {account.values.map((value, index) => <td key={columns[index]?.code ?? index} className={`numeric ${value !== null && value < 0 ? 'negative' : ''}`}>{cell(value, columns[index]?.percent ?? false)}</td>)}
            </tr>)}</tbody>
          </table>}
        </td></tr>}
      </Fragment>)}</tbody>
    </table></div>
    {result && result.rows.length === 0 && <p className="empty-state">O mapa não tem linhas para mostrar neste período.</p>}
    {!result && !error && loaded && definition.rows.length > 0 && <p className="empty-state">{editing ? 'Carregue em «Testar» para calcular o mapa.' : 'A calcular o mapa…'}</p>}
    </section>
  </div>
}
