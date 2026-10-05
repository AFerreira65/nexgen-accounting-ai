import { ChevronDown, ChevronRight, Database, Scale, Search, TriangleAlert } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { signalLoadFailure } from '../app/loadFailure'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

type Column = { code: string; label: string; percent: boolean }
type Row = { code: string; name: string; depth: number; parentCode: string | null; hasChildren: boolean; isPosting: boolean; values: (number | null)[] }
type ColumnSet = { key: string; label: string; description: string }
type Balancete = {
  columns: Column[]
  rows: Row[]
  totals: (number | null)[]
  totalDebit: number
  totalCredit: number
  accountCount: number
  maxDepth: number
  truncated: boolean
  warnings: string[]
  columnSets: ColumnSet[]
}

/** O servidor recusou o pedido (período ou colunas inválidos): é um erro de quem pediu, não uma falha. */
class RefusedError extends Error {}

const MONTHS = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
const ALL_LEVELS = 99
const amount = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })
const percent = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1, useGrouping: 'always' })

/** Um valor do balancete: o zero é um traço, para os números que contam sobressaírem. */
function balanceteCell(value: number | null, isPercent: boolean): string {
  if (value === null || value === undefined) return ''
  if (Math.round(value * 100) === 0) return '–'
  return isPercent ? `${percent.format(value)} %` : amount.format(value)
}

/**
 * O balancete em árvore (F4B-02): as contas arrumadas pelo plano, da classe à conta de
 * movimento, com as colunas à escolha. O servidor calcula tudo e devolve só os níveis
 * pedidos; abrir um ramo vai buscar as contas desse ramo.
 */
export function BalancetePage() {
  const { activeCompany, source, user } = useWorkContext()
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(fiscalYear)
  const [fromMonth, setFromMonth] = useState(1)
  const [toMonth, setToMonth] = useState(12)
  const [columnSet, setColumnSet] = useState('classico')
  const [withClosing, setWithClosing] = useState(true)
  const [level, setLevel] = useState(2)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [data, setData] = useState<Balancete | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  // Os ramos abertos à mão e as contas que cada um trouxe; os fechados à mão dentro do nível pedido.
  const [branches, setBranches] = useState<Record<string, Row[]>>({})
  const [opened, setOpened] = useState<Set<string>>(new Set())
  const [closed, setClosed] = useState<Set<string>>(new Set())
  const requestId = useRef(0)

  useEffect(() => setYear(fiscalYear), [fiscalYear, activeCompany.companyId])
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 300)
    return () => window.clearTimeout(timer)
  }, [search])

  const baseUrl = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/balancete`
  const params = useCallback((extra: Record<string, string>) => {
    const values = new URLSearchParams({ year: String(year), fromMonth: String(fromMonth), toMonth: String(toMonth), columns: columnSet })
    if (!withClosing) values.set('excludeClosing', 'true')
    for (const [key, value] of Object.entries(extra)) values.set(key, value)
    return values
  }, [columnSet, fromMonth, toMonth, withClosing, year])

  useEffect(() => {
    const controller = new AbortController()
    const current = ++requestId.current
    setLoading(true)
    setError('')
    void fetch(`${baseUrl}?${params(query ? { search: query } : { depth: String(level) })}`, { signal: controller.signal })
      .then(async (response) => {
        if (response.ok) return await response.json() as Balancete
        const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
        const refusal = Object.values(problem?.errors ?? {}).flat()[0]
        if (refusal) throw new RefusedError(refusal)
        throw new Error('Não foi possível calcular o balancete.')
      })
      .then((result) => {
        if (current !== requestId.current) return
        setData(result)
        setBranches({})
        setOpened(new Set())
        setClosed(new Set())
        setLoading(false)
      })
      .catch((problem: unknown) => {
        if (controller.signal.aborted || current !== requestId.current) return
        if (!(problem instanceof RefusedError)) signalLoadFailure()
        setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o balancete.')
        setLoading(false)
      })
    return () => controller.abort()
  }, [baseUrl, level, params, query])

  const searching = query !== ''
  // As linhas à vista, pela ordem da árvore: as do nível pedido, mais os ramos abertos à mão.
  const visible = useMemo(() => {
    if (!data) return []
    if (searching) return data.rows
    const result: Row[] = []
    const byParent = new Map<string, Row[]>()
    for (const row of data.rows) {
      const key = row.parentCode ?? ''
      const list = byParent.get(key)
      if (list) list.push(row)
      else byParent.set(key, [row])
    }
    const walk = (rows: Row[]) => {
      for (const row of rows) {
        result.push(row)
        if (!row.hasChildren || closed.has(row.code)) continue
        const loaded = byParent.get(row.code)
        if (loaded) walk(loaded)
        else if (opened.has(row.code) && branches[row.code]) walk(branches[row.code])
      }
    }
    walk(byParent.get('') ?? [])
    return result
  }, [branches, closed, data, opened, searching])

  const loadedParents = useMemo(() => new Set((data?.rows ?? []).map((row) => row.parentCode ?? '')), [data])
  const isOpen = (row: Row) => !closed.has(row.code) && (loadedParents.has(row.code) || (opened.has(row.code) && Boolean(branches[row.code])))

  function toggle(row: Row) {
    if (isOpen(row)) {
      setClosed((current) => new Set(current).add(row.code))
      return
    }
    setClosed((current) => { const next = new Set(current); next.delete(row.code); return next })
    setOpened((current) => new Set(current).add(row.code))
    if (loadedParents.has(row.code) || branches[row.code]) return
    const current = requestId.current
    void fetch(`${baseUrl}?${params({ parent: row.code })}`)
      .then(async (response) => response.ok ? await response.json() as Balancete : Promise.reject(new Error()))
      .then((result) => { if (current === requestId.current) setBranches((existing) => ({ ...existing, [row.code]: result.rows })) })
      .catch(() => signalLoadFailure())
  }

  const columns = data?.columns ?? []
  const balanced = data ? Math.round((data.totalDebit - data.totalCredit) * 100) === 0 : true
  const periodLabel = fromMonth === toMonth ? `${MONTHS[fromMonth - 1]} de ${year}` : `${MONTHS[fromMonth - 1]} a ${MONTHS[toMonth - 1]} de ${year}`
  const levels = Array.from({ length: Math.max(1, data?.maxDepth ?? 1) }, (_, index) => index + 1)

  // Exporta o que está à vista; se o ecrã cortou linhas, vai buscar todas ao servidor.
  async function exportRows() {
    if (!data) return
    let rows = visible
    if (data.truncated) {
      const response = await fetch(`${baseUrl}?${params(searching ? { search: query, all: 'true' } : { depth: String(level), all: 'true' })}`)
      if (!response.ok) throw new Error('Não foi possível ler todas as contas para exportar.')
      rows = (await response.json() as Balancete).rows
    }
    const body: (string | number)[][] = [['Conta', 'Descrição', ...columns.map((column) => column.label)]]
    for (const row of rows) body.push([row.code, `${'  '.repeat(searching ? 0 : row.depth - 1)}${row.name}`, ...row.values.map((value) => value ?? '')])
    body.push(['', 'Totais', ...data.totals.map((value) => value ?? '')])
    downloadCsv(csvFilename('balancete', activeCompany.legalName), toCsv(body))
  }

  const from = `${year}-${String(fromMonth).padStart(2, '0')}-01`
  const to = new Date(Date.UTC(year, toMonth, 0)).toISOString().slice(0, 10)

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Contabilidade</p><h1>Balancete</h1><p>As contas arrumadas pelo plano, da classe à conta de movimento — {periodLabel}. Sem os lançamentos anulados.</p></div>
      <span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />Dados da BD</span>
    </header>
    <ReportChrome reportKey="balancete" title="Balancete" from={from} to={to} />
    <section className="metric-grid compact-grid">
      <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Débito acumulado</small><strong>{amount.format(data?.totalDebit ?? 0)}</strong><span>{data?.accountCount ?? 0} contas com valores</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${balanced ? 'blue' : 'rose'}`}><Scale /></span><div><small>Crédito acumulado</small><strong>{amount.format(data?.totalCredit ?? 0)}</strong><span>{balanced ? 'Balancete equilibrado' : 'Débito e crédito diferentes'}</span></div></article>
    </section>
    <div className="filterbar balancete-filters">
      <label className="balancete-field"><span>Exercício</span><input type="number" min={1990} max={2100} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 1990 && next <= 2100) setYear(next) }} /></label>
      <label className="balancete-field"><span>De</span><select value={fromMonth} onChange={(event) => { const next = Number(event.target.value); setFromMonth(next); if (next > toMonth) setToMonth(next) }}>{MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}</select></label>
      <label className="balancete-field"><span>A</span><select value={toMonth} onChange={(event) => { const next = Number(event.target.value); setToMonth(next); if (next < fromMonth) setFromMonth(next) }}>{MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}</select></label>
      <label className="balancete-field"><span>Colunas</span><select value={columnSet} onChange={(event) => setColumnSet(event.target.value)} title={data?.columnSets.find((set) => set.key === columnSet)?.description}>
        {(data?.columnSets ?? [{ key: 'classico', label: 'Balancete clássico', description: '' }]).map((set) => <option key={set.key} value={set.key}>{set.label}</option>)}
      </select></label>
      <label className="balancete-field"><span>Nível</span><select value={level} onChange={(event) => setLevel(Number(event.target.value))} disabled={searching}>
        {levels.map((value) => <option key={value} value={value}>{value === 1 ? '1 · Classes' : `Até ao ${value}.º`}</option>)}
        <option value={ALL_LEVELS}>Todas as contas</option>
      </select></label>
      <label className="balancete-field"><span>Apuramento</span><select value={withClosing ? 'com' : 'sem'} onChange={(event) => setWithClosing(event.target.value === 'com')}>
        <option value="com">Com o apuramento de resultados</option>
        <option value="sem">Antes do apuramento</option>
      </select></label>
      <label><Search size={17} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou descrição…" /></label>
      <ExportButtons run={exportRows} disabled={!data || visible.length === 0} />
    </div>
    {data?.warnings.map((warning) => <p key={warning} className="save-status balancete-warning"><TriangleAlert size={15} /> {warning}</p>)}
    {error && <p className="save-status error" role="alert">{error}</p>}
    {data?.truncated && <p className="save-status">A mostrar as primeiras {data.rows.length} linhas. Escolha um nível mais acima, pesquise uma conta, ou exporte para ter todas.</p>}
    <section className="panel"><div className="table-wrap balancete-wrap"><table className={`balancete-table ${loading ? 'is-loading' : ''}`}>
      <thead><tr><th>Conta</th><th>Descrição</th>{columns.map((column) => <th key={column.code} className="numeric">{column.label}</th>)}</tr></thead>
      <tbody>
        {visible.map((row) => {
          const depth = searching ? 1 : row.depth
          return <tr key={row.code} className={searching ? (row.isPosting ? '' : 'balancete-aggregate') : `balancete-depth-${Math.min(depth, 4)} ${row.isPosting && !row.hasChildren ? '' : 'balancete-aggregate'}`}>
            <td className="balancete-code" style={{ paddingLeft: 10 + (depth - 1) * 16 }}>
              {!searching && row.hasChildren
                ? <button type="button" className="balancete-toggle" aria-expanded={isOpen(row)} aria-label={`${isOpen(row) ? 'Fechar' : 'Abrir'} a conta ${row.code}`} onClick={() => toggle(row)}>{isOpen(row) ? <ChevronDown size={14} /> : <ChevronRight size={14} />}</button>
                : <span className="balancete-toggle-space" />}
              {row.isPosting ? <Link className="primary-cell" to={`/contas/${row.code}`}>{row.code}</Link> : <span>{row.code}</span>}
            </td>
            <td className="balancete-name">{row.name}</td>
            {row.values.map((value, index) => <td key={columns[index]?.code ?? index} className={`numeric ${value !== null && value < 0 ? 'negative' : ''}`}>{balanceteCell(value, columns[index]?.percent ?? false)}</td>)}
          </tr>
        })}
      </tbody>
      {data && visible.length > 0 && <tfoot><tr><td colSpan={2}>Totais</td>{data.totals.map((value, index) => <td key={columns[index]?.code ?? index} className="numeric strong">{balanceteCell(value, columns[index]?.percent ?? false)}</td>)}</tr></tfoot>}
    </table></div>
    {!loading && data && visible.length === 0 && <p className="empty-state">{searching ? 'Nenhuma conta com valores corresponde à pesquisa.' : 'Sem movimentos neste período.'}</p>}
    {loading && !data && <p className="empty-state">A calcular o balancete…</p>}
    </section>
    <p className="balancete-foot">Nas colunas de saldo e de período, os valores a débito são positivos e os a crédito negativos. <Link className="text-link" to="/balancete/lista">Ver a lista por taxonomia</Link></p>
  </div>
}
