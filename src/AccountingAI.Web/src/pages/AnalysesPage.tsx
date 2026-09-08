import { CircleAlert, Database, Download, Play, Plus, Printer, Save, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type AnalysisCatalog, type AnalysisCatalogField, type AnalysisDefinition, type AnalysisResult, type SavedAnalysisView } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const AGG_FUNCTIONS: { value: AnalysisDefinition['aggregates'][number]['function']; label: string }[] = [
  { value: 'sum', label: 'Soma' },
  { value: 'count', label: 'Contagem' },
  { value: 'average', label: 'Média' },
  { value: 'min', label: 'Mínimo' },
  { value: 'max', label: 'Máximo' },
]

const OP_LABELS: Record<string, string> = {
  eq: 'igual a', neq: 'diferente de', contains: 'contém', startsWith: 'começa por',
  gt: 'maior que', gte: 'maior ou igual', lt: 'menor que', lte: 'menor ou igual',
}

function emptyDefinition(): AnalysisDefinition {
  return { source: 'movimentos', columns: [], filters: [], groupBy: [], sort: [], aggregates: [], includeGrandTotal: true }
}

export function AnalysesPage() {
  const { activeCompany, analysisCatalog, savedAnalyses, saveAnalysis, updateAnalysis, deleteAnalysis, runAnalysis, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [catalog, setCatalog] = useState<AnalysisCatalog | null>(null)
  const [saved, setSaved] = useState<SavedAnalysisView[]>([])
  const [def, setDef] = useState<AnalysisDefinition>(emptyDefinition)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [loadedId, setLoadedId] = useState<string | null>(null)
  const [result, setResult] = useState<AnalysisResult | null>(null)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)

  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, companyId)

  useEffect(() => {
    void analysisCatalog().then(setCatalog).catch((problem: unknown) =>
      setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o catálogo.'))
  }, [analysisCatalog])

  useEffect(() => {
    if (!onDatabase) { setSaved([]); return }
    void savedAnalyses(companyId).then(setSaved).catch(() => setSaved([]))
  }, [companyId, savedAnalyses, onDatabase, reload])

  const fields: AnalysisCatalogField[] = catalog?.sources[0]?.fields ?? []
  const fieldByKey = useMemo(() => new Map(fields.map((field) => [field.key, field])), [fields])
  const operatorsFor = (key: string) => {
    const type = fieldByKey.get(key)?.type
    return type ? catalog?.operatorsByType[type] ?? [] : []
  }

  function patch(next: Partial<AnalysisDefinition>) {
    setDef((current) => ({ ...current, ...next }))
  }

  function toggle(list: 'columns' | 'groupBy', key: string) {
    setDef((current) => {
      const has = current[list].includes(key)
      return { ...current, [list]: has ? current[list].filter((k) => k !== key) : [...current[list], key] }
    })
  }

  function loadSaved(analysis: SavedAnalysisView) {
    setDef(analysis.definition)
    setName(analysis.name)
    setDescription(analysis.description ?? '')
    setLoadedId(analysis.id)
    setResult(null)
    setStatus('')
  }

  function newAnalysis() {
    setDef(emptyDefinition())
    setName('')
    setDescription('')
    setLoadedId(null)
    setResult(null)
    setStatus('')
  }

  async function run() {
    setBusy(true)
    setError('')
    try {
      const body = await runAnalysis(companyId, { definition: def }, { from: fromDate || undefined, to: toDate || undefined })
      setResult(body)
    } catch (problem) {
      setResult(null)
      setError(problem instanceof Error ? problem.message : 'Não foi possível correr a análise.')
    } finally {
      setBusy(false)
    }
  }

  async function persist(asNew: boolean) {
    if (!name.trim()) { setError('Indique um nome para a análise.'); return }
    setBusy(true)
    setError('')
    try {
      const payload = { name: name.trim(), description: description.trim() || undefined, definition: def }
      const stored = loadedId && !asNew
        ? await updateAnalysis(companyId, loadedId, payload)
        : await saveAnalysis(companyId, payload)
      setLoadedId(stored.id)
      setStatus(`Análise «${stored.name}» guardada.`)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar.')
    } finally {
      setBusy(false)
    }
  }

  async function remove(analysis: SavedAnalysisView) {
    await deleteAnalysis(companyId, analysis.id).catch(() => undefined)
    if (loadedId === analysis.id) newAnalysis()
    setReload((value) => value + 1)
  }

  const aggLabel = (key: string) => {
    const [field, fn] = key.split(':')
    return `${fieldByKey.get(field)?.label ?? field} · ${AGG_FUNCTIONS.find((f) => f.value === fn)?.label ?? fn}`
  }
  const formatAgg = (key: string, value: number) => {
    const [field, fn] = key.split(':')
    if (fn === 'count') return String(value)
    return fieldByKey.get(field)?.type === 'Money' ? euro.format(value) : String(value)
  }
  const formatCell = (columnKey: string, value: unknown) => {
    const type = fieldByKey.get(columnKey)?.type
    if (value === null || value === undefined) return '—'
    if (type === 'Money') return euro.format(Number(value))
    if (type === 'Date') return formatMovementDate(String(value))
    return String(value)
  }

  function exportCsv() {
    if (!result) return
    const header = result.columns.map((column) => column.label)
    const rows = result.rows.map((row) => result.columns.map((column) => {
      const raw = row[column.key]
      return typeof raw === 'number' ? raw : String(raw ?? '')
    }))
    const totals: (string | number)[][] = [[], ['Total geral', ...Object.entries(result.grandTotal).map(([k, v]) => `${aggLabel(k)}: ${formatAgg(k, v)}`)]]
    downloadCsv(csvFilename('analise', name || 'movimentos'), toCsv([header, ...rows, ...totals]))
  }

  const canRun = def.columns.length > 0 && !busy

  if (!catalog && error) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Análises</h1></div></header>
      <section className="panel"><p className="save-status error"><CircleAlert size={15} /> {error}</p></section>
    </div>
  }

  return <div className="page-stack analyses">
    <header className="page-heading no-print"><div><p className="eyebrow">Mapas</p><h1>Análises</h1><p>Componha um mapa sobre os movimentos: colunas, filtros, agrupamentos e totais. Semelhante às Análises Avançadas do PHC.</p></div><span className={`connection-badge ${onDatabase ? 'database' : 'demonstration'}`}><Database size={15} /> {onDatabase ? 'Ligado ao SQL Server' : 'Modo de demonstração'}</span></header>

    <div className="analyses-layout no-print">
      <section className="panel">
        <div className="panel-heading"><div><h2>Guardadas</h2></div><button className="button button-secondary" type="button" onClick={newAnalysis}><Plus size={14} /> Nova</button></div>
        {!onDatabase && <p className="save-status"><CircleAlert size={15} /> Guardar análises requer ligação ao SQL Server; pode compô-las e corrê-las à mesma.</p>}
        {saved.length === 0 ? <p className="empty-state">Sem análises guardadas.</p> : <ul className="analyses-saved">
          {saved.map((analysis) => <li key={analysis.id} className={analysis.id === loadedId ? 'active' : undefined}>
            <button type="button" onClick={() => loadSaved(analysis)}><strong>{analysis.name}</strong>{analysis.description ? <small>{analysis.description}</small> : null}</button>
            <button className="icon-button" type="button" aria-label={`Apagar ${analysis.name}`} onClick={() => void remove(analysis)}><Trash2 size={15} /></button>
          </li>)}
        </ul>}
      </section>

      <section className="panel analyses-builder">
        <div className="panel-heading"><div><h2>{loadedId ? 'Editar análise' : 'Nova análise'}</h2><p>Origem: movimentos da contabilidade.</p></div></div>

        <div className="builder-grid">
          <label className="field-block"><span>Nome</span><input value={name} maxLength={120} onChange={(event) => setName(event.target.value)} placeholder="Ex.: Gastos por conta e mês" /></label>
          <label className="field-block"><span>Descrição <small>(opcional)</small></span><input value={description} maxLength={500} onChange={(event) => setDescription(event.target.value)} /></label>
        </div>

        <fieldset className="builder-section">
          <legend>Colunas</legend>
          <div className="chip-picker">
            {fields.map((field) => <label key={field.key} className={def.columns.includes(field.key) ? 'on' : undefined}>
              <input type="checkbox" checked={def.columns.includes(field.key)} onChange={() => toggle('columns', field.key)} /> {field.label}
            </label>)}
          </div>
        </fieldset>

        <fieldset className="builder-section">
          <legend>Agrupar por</legend>
          <div className="chip-picker">
            {fields.filter((field) => field.groupable).map((field) => <label key={field.key} className={def.groupBy.includes(field.key) ? 'on' : undefined}>
              <input type="checkbox" checked={def.groupBy.includes(field.key)} onChange={() => toggle('groupBy', field.key)} /> {field.label}
            </label>)}
          </div>
        </fieldset>

        <fieldset className="builder-section">
          <legend>Filtros</legend>
          {def.filters.map((filter, index) => <div key={index} className="builder-row">
            <select value={filter.field} onChange={(event) => {
              const field = event.target.value
              const ops = operatorsFor(field)
              patch({ filters: def.filters.map((f, i) => i === index ? { ...f, field, operator: ops[0] ?? f.operator } : f) })
            }}>{fields.map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}</select>
            <select value={filter.operator} onChange={(event) => patch({ filters: def.filters.map((f, i) => i === index ? { ...f, operator: event.target.value } : f) })}>
              {operatorsFor(filter.field).map((op) => <option key={op} value={op}>{OP_LABELS[op] ?? op}</option>)}
            </select>
            <input value={filter.value} onChange={(event) => patch({ filters: def.filters.map((f, i) => i === index ? { ...f, value: event.target.value } : f) })} placeholder="Valor" />
            <button className="icon-button" type="button" aria-label="Remover filtro" onClick={() => patch({ filters: def.filters.filter((_, i) => i !== index) })}><Trash2 size={15} /></button>
          </div>)}
          <button className="button button-secondary" type="button" onClick={() => {
            const field = fields[0]?.key ?? ''
            patch({ filters: [...def.filters, { field, operator: operatorsFor(field)[0] ?? 'eq', value: '' }] })
          }}><Plus size={14} /> Filtro</button>
        </fieldset>

        <fieldset className="builder-section">
          <legend>Ordenar por</legend>
          {def.sort.map((sort, index) => <div key={index} className="builder-row">
            <select value={sort.field} onChange={(event) => patch({ sort: def.sort.map((s, i) => i === index ? { ...s, field: event.target.value } : s) })}>{fields.map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}</select>
            <select value={sort.descending ? 'desc' : 'asc'} onChange={(event) => patch({ sort: def.sort.map((s, i) => i === index ? { ...s, descending: event.target.value === 'desc' } : s) })}><option value="asc">Ascendente</option><option value="desc">Descendente</option></select>
            <button className="icon-button" type="button" aria-label="Remover ordenação" onClick={() => patch({ sort: def.sort.filter((_, i) => i !== index) })}><Trash2 size={15} /></button>
          </div>)}
          <button className="button button-secondary" type="button" onClick={() => patch({ sort: [...def.sort, { field: fields[0]?.key ?? '', descending: false }] })}><Plus size={14} /> Ordenação</button>
        </fieldset>

        <fieldset className="builder-section">
          <legend>Totais</legend>
          {def.aggregates.map((aggregate, index) => <div key={index} className="builder-row">
            <select value={aggregate.field} onChange={(event) => patch({ aggregates: def.aggregates.map((a, i) => i === index ? { ...a, field: event.target.value } : a) })}>{fields.map((field) => <option key={field.key} value={field.key}>{field.label}</option>)}</select>
            <select value={aggregate.function} onChange={(event) => patch({ aggregates: def.aggregates.map((a, i) => i === index ? { ...a, function: event.target.value as typeof a.function } : a) })}>{AGG_FUNCTIONS.map((fn) => <option key={fn.value} value={fn.value}>{fn.label}</option>)}</select>
            <button className="icon-button" type="button" aria-label="Remover total" onClick={() => patch({ aggregates: def.aggregates.filter((_, i) => i !== index) })}><Trash2 size={15} /></button>
          </div>)}
          <button className="button button-secondary" type="button" onClick={() => patch({ aggregates: [...def.aggregates, { field: fields.find((f) => f.aggregatable)?.key ?? fields[0]?.key ?? '', function: 'sum' }] })}><Plus size={14} /> Total</button>
          <label className="toggle-inline"><input type="checkbox" checked={def.includeGrandTotal} onChange={(event) => patch({ includeGrandTotal: event.target.checked })} /> Mostrar total geral</label>
        </fieldset>

        <div className="filterbar">
          <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
          <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
          <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
        </div>

        <footer className="settings-actions">
          {error && <span className="save-status error"><CircleAlert size={15} /> {error}</span>}
          {status && !error && <span className="save-status success">{status}</span>}
          <button className="button button-secondary" type="button" disabled={!onDatabase || busy} onClick={() => void persist(false)}><Save size={15} /> {loadedId ? 'Guardar' : 'Guardar'}</button>
          {loadedId && <button className="button button-secondary" type="button" disabled={!onDatabase || busy} onClick={() => void persist(true)}>Guardar como nova</button>}
          <button className="button button-primary" type="button" disabled={!canRun} onClick={() => void run()}><Play size={15} /> Correr</button>
        </footer>
      </section>
    </div>

    {result && <section className="panel analyses-result">
      <div className="print-only"><h1>{name || 'Análise de movimentos'}</h1><p>{fromDate || '…'} a {toDate || '…'} · {result.rowCount} linhas{result.truncated ? ' (truncado)' : ''}</p></div>
      <div className="panel-heading no-print"><div><h2>Resultado</h2><p>{result.rowCount} {result.rowCount === 1 ? 'linha' : 'linhas'}{result.truncated ? ' — truncado no limite de 5000' : ''}</p></div>
        <div className="settings-actions" style={{ margin: 0 }}>
          <button className="button button-secondary" type="button" onClick={() => window.print()}><Printer size={15} /> Imprimir / PDF</button>
          <button className="button button-secondary" type="button" onClick={exportCsv}><Download size={15} /> Exportar CSV</button>
        </div>
      </div>
      {result.truncated && <p className="save-status error no-print"><CircleAlert size={15} /> A análise foi truncada às 5000 linhas. Estreite o período ou os filtros.</p>}
      <AnalysisTable result={result} def={def} formatCell={formatCell} aggLabel={aggLabel} formatAgg={formatAgg} />
    </section>}
  </div>
}

function AnalysisTable({ result, def, formatCell, aggLabel, formatAgg }: {
  result: AnalysisResult
  def: AnalysisDefinition
  formatCell: (key: string, value: unknown) => string
  aggLabel: (key: string) => string
  formatAgg: (key: string, value: number) => string
}) {
  const groupAt = useMemo(() => {
    const map = new Map<string, AnalysisResult['groups'][number]>()
    for (const group of result.groups) {
      map.set(group.keys.map((k) => String(k.value)).join(''), group)
    }
    return map
  }, [result.groups])

  const subtotalLine = (group: AnalysisResult['groups'][number]) => {
    const keyLabel = group.keys.map((k) => `${k.label}: ${k.value}`).join(' · ')
    const values = Object.entries(group.subtotals).map(([k, v]) => `${aggLabel(k)}: ${formatAgg(k, v)}`).join('   ')
    return `Subtotal (${keyLabel}) — ${values || `${group.rowCount} linhas`}`
  }

  const items: { type: 'row' | 'subtotal' | 'grand'; row?: Record<string, unknown>; text?: string; level?: number }[] = []
  let prev: string[] | null = null
  for (const row of result.rows) {
    const prefix = def.groupBy.map((field) => String(row[field] ?? ''))
    if (prev) {
      let firstDiff = prefix.findIndex((value, index) => value !== prev![index])
      if (firstDiff === -1) firstDiff = prefix.length
      for (let level = def.groupBy.length - 1; level >= firstDiff; level--) {
        const group = groupAt.get(prev.slice(0, level + 1).join(''))
        if (group) items.push({ type: 'subtotal', text: subtotalLine(group), level: level + 1 })
      }
    }
    items.push({ type: 'row', row })
    prev = prefix
  }
  if (prev) {
    for (let level = def.groupBy.length - 1; level >= 0; level--) {
      const group = groupAt.get(prev.slice(0, level + 1).join(''))
      if (group) items.push({ type: 'subtotal', text: subtotalLine(group), level: level + 1 })
    }
  }
  if (def.includeGrandTotal && Object.keys(result.grandTotal).length > 0) {
    items.push({ type: 'grand', text: Object.entries(result.grandTotal).map(([k, v]) => `${aggLabel(k)}: ${formatAgg(k, v)}`).join('   ') })
  }

  return <div className="table-wrap"><table><thead><tr>
    {result.columns.map((column) => <th key={column.key} className={column.type === 'Money' || column.type === 'Number' ? 'numeric' : undefined}>{column.label}</th>)}
  </tr></thead><tbody>
    {items.map((item, index) => {
      if (item.type === 'row') {
        return <tr key={index}>{result.columns.map((column) => <td key={column.key} className={column.type === 'Money' || column.type === 'Number' ? 'numeric' : undefined}>{formatCell(column.key, item.row![column.key])}</td>)}</tr>
      }
      return <tr key={index} className={item.type === 'grand' ? 'analyses-grand' : 'analyses-subtotal'}>
        <td colSpan={result.columns.length}>{item.type === 'grand' ? `Total geral — ${item.text}` : item.text}</td>
      </tr>
    })}
    {result.rows.length === 0 && <tr><td colSpan={Math.max(1, result.columns.length)} className="empty-state">Sem linhas no período.</td></tr>}
  </tbody></table></div>
}
