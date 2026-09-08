import { ChevronLeft, ChevronRight, Database, Download, GitCompare, Layers, Scale, Search, Tags } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { Pager } from '../app/Pager'
import { paginate } from '../app/pagination'
import { PeriodShortcuts } from '../app/periods'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; openingBalance: number; debit: number; credit: number; balance: number }
type TrialBalance = { totalOpeningBalance: number; totalDebit: number; totalCredit: number; rows: Row[] }
type ClassGroup = { digit: string; name: string; rows: Row[]; openingBalance: number; debit: number; credit: number; balance: number }
type TaxonomyGroup = { key: string; label: string; rows: Row[]; openingBalance: number; debit: number; credit: number; balance: number }

const CLASS_NAMES: Record<string, string> = {
  '1': 'Meios financeiros líquidos',
  '2': 'Contas a receber e a pagar',
  '3': 'Inventários e ativos biológicos',
  '4': 'Investimentos',
  '5': 'Capital, reservas e resultados transitados',
  '6': 'Gastos',
  '7': 'Rendimentos',
  '8': 'Resultados',
  '0': 'Contas extrapatrimoniais',
}

function classOf(code: string) {
  const digit = code.trim().charAt(0)
  return { digit, name: CLASS_NAMES[digit] ?? 'Outras contas' }
}

function shiftYear(date: string, delta: number) {
  return date ? `${Number(date.slice(0, 4)) + delta}${date.slice(4)}` : ''
}

function groupByClass(rows: Row[]): ClassGroup[] {
  const groups = new Map<string, ClassGroup>()
  for (const row of rows) {
    const { digit, name } = classOf(row.accountCode)
    let group = groups.get(digit)
    if (!group) {
      group = { digit, name, rows: [], openingBalance: 0, debit: 0, credit: 0, balance: 0 }
      groups.set(digit, group)
    }
    group.rows.push(row)
    group.openingBalance += row.openingBalance
    group.debit += row.debit
    group.credit += row.credit
    group.balance += row.balance
  }
  return [...groups.values()].sort((a, b) => a.digit.localeCompare(b.digit, 'pt-PT'))
}

function groupByTaxonomy(rows: Row[], taxonomyOf: Map<string, number | null>, nameOf: (code: number) => string): TaxonomyGroup[] {
  const groups = new Map<string, TaxonomyGroup>()
  for (const row of rows) {
    const code = taxonomyOf.get(row.accountCode) ?? null
    const key = code === null ? '' : String(code).padStart(6, '0')
    const label = code === null ? 'Sem taxonomia' : `${code} · ${nameOf(code)}`
    let group = groups.get(key)
    if (!group) {
      group = { key, label, rows: [], openingBalance: 0, debit: 0, credit: 0, balance: 0 }
      groups.set(key, group)
    }
    group.rows.push(row)
    group.openingBalance += row.openingBalance
    group.debit += row.debit
    group.credit += row.credit
    group.balance += row.balance
  }
  // Sem taxonomia (key vazia) fica sempre no fim; as restantes por código crescente.
  return [...groups.values()].sort((a, b) => {
    if (a.key === b.key) return 0
    if (a.key === '') return 1
    if (b.key === '') return -1
    return a.key.localeCompare(b.key, 'pt-PT')
  })
}

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): TrialBalance {
  const map = new Map<string, Row>()
  const rowFor = (code: string) => {
    let row = map.get(code)
    if (!row) {
      row = { accountCode: code, accountName: nameOf(code), openingBalance: 0, debit: 0, credit: 0, balance: 0 }
      map.set(code, row)
    }
    return row
  }
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (to && movement.date > to) continue
    const beforePeriod = Boolean(from) && movement.date < from
    for (const line of movement.lines) {
      const row = rowFor(line.account)
      if (beforePeriod) {
        row.openingBalance += line.debit - line.credit
      } else {
        row.debit += line.debit
        row.credit += line.credit
      }
      row.balance = row.openingBalance + row.debit - row.credit
    }
  }
  const rows = [...map.values()]
    .filter((row) => row.openingBalance !== 0 || row.debit !== 0 || row.credit !== 0)
    .sort((a, b) => a.accountCode.localeCompare(b.accountCode, 'pt-PT'))
  return {
    rows,
    totalOpeningBalance: rows.reduce((sum, row) => sum + row.openingBalance, 0),
    totalDebit: rows.reduce((sum, row) => sum + row.debit, 0),
    totalCredit: rows.reduce((sum, row) => sum + row.credit, 0),
  }
}

export function TrialBalancePage() {
  const { activeCompany, accountingSetup, taxonomyCatalog, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [byClass, setByClass] = useState(false)
  const [byTaxonomy, setByTaxonomy] = useState(false)
  const [compare, setCompare] = useState(false)
  const [page, setPage] = useState(1)
  const taxonomyOf = useMemo(() => new Map(accountingSetup.accounts.map((account) => [account.code, account.taxonomyCode] as const)), [accountingSetup.accounts])
  const taxonomyName = useMemo(() => {
    const names = new Map(taxonomyCatalog.codes.map((entry) => [entry.code, entry.description] as const))
    return (code: number) => names.get(code) ?? `Código ${code}`
  }, [taxonomyCatalog])
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [data, setData] = useState<TrialBalance>(() => fromMovements(movements, nameOf, '', ''))
  const [prior, setPrior] = useState<TrialBalance | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const load = (from: string, to: string, apply: (result: TrialBalance) => void) => {
      if (source !== 'database') {
        apply(fromMovements(movements, nameOf, from, to))
        return
      }
      const params = new URLSearchParams()
      if (from) params.set('from', from)
      if (to) params.set('to', to)
      void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
        .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
        .then(apply)
        .catch(() => { if (!controller.signal.aborted) apply(fromMovements(movements, nameOf, from, to)) })
    }
    load(fromDate, toDate, setData)
    if (compare && fromDate && toDate) {
      load(shiftYear(fromDate, -1), shiftYear(toDate, -1), setPrior)
    } else {
      setPrior(null)
    }
    return () => controller.abort()
  }, [activeCompany.companyId, compare, fromDate, movements, nameOf, source, toDate, user.id])

  const rows = useMemo(() => {
    const text = search.trim().toLocaleLowerCase('pt-PT')
    return text ? data.rows.filter((row) => `${row.accountCode} ${row.accountName}`.toLocaleLowerCase('pt-PT').includes(text)) : data.rows
  }, [data.rows, search])
  const groups = useMemo(() => groupByClass(rows), [rows])
  const taxonomyGroups = useMemo(() => groupByTaxonomy(rows, taxonomyOf, taxonomyName), [rows, taxonomyOf, taxonomyName])
  const priorBalanceOf = useMemo(() => new Map((prior?.rows ?? []).map((row) => [row.accountCode, row.balance])), [prior])
  const priorTotalBalance = (prior?.rows ?? []).reduce((sum, row) => sum + row.balance, 0)
  const balanced = Math.round((data.totalDebit - data.totalCredit) * 100) === 0
  const showOpening = Boolean(fromDate)
  const showPrior = compare && Boolean(fromDate) && Boolean(toDate)
  const priorOfRows = (list: Row[]) => list.reduce((sum, row) => sum + (priorBalanceOf.get(row.accountCode) ?? 0), 0)

  // Sem agrupamento, a lista plana pagina como as outras — uma empresa
  // grande (SAF-T com dezenas de milhares de contas) não cabe num só
  // desenho. Agrupada, mostram-se sempre os cabeçalhos e subtotais (poucas
  // dezenas de linhas); as contas de cada grupo só se desenham quando o
  // total cabe numa página — para o resumo por classe/taxonomia continuar
  // a abrir de imediato. Volta à primeira página quando um filtro muda.
  const grouped = byClass || byTaxonomy
  const pageSize = activeCompany.settings.pageSize || 50
  const detailInGroups = rows.length <= pageSize
  const { items: pagedRows, currentPage, totalPages } = paginate(rows, pageSize, page)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [groupPage, setGroupPage] = useState<Record<string, number>>({})
  useEffect(() => {
    setPage(1)
    setExpanded(new Set())
    setGroupPage({})
  }, [search, fromDate, toDate, byClass, byTaxonomy, compare])
  const toggleGroup = (key: string) => setExpanded((current) => {
    const next = new Set(current)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })
  const colCount = 5 + (showOpening ? 1 : 0) + (showPrior ? 2 : 0)

  const exportCsv = () => {
    const head = ['Conta', 'Descrição', ...(showOpening ? ['Abertura'] : []), 'Débito', 'Crédito', 'Saldo', ...(showPrior ? ['Saldo homólogo', 'Variação'] : [])]
    const cells = (row: Row) => [
      row.accountCode, row.accountName, ...(showOpening ? [row.openingBalance] : []), row.debit, row.credit, row.balance,
      ...(showPrior ? [priorBalanceOf.get(row.accountCode) ?? 0, row.balance - (priorBalanceOf.get(row.accountCode) ?? 0)] : []),
    ]
    const body: (string | number)[][] = []
    if (byClass) {
      body.push(['Classe', ...head.slice(1)])
      for (const group of groups) {
        for (const row of group.rows) body.push(['', ...cells(row).slice(1)])
        const groupPrior = priorOfRows(group.rows)
        body.push([`${group.digit} · ${group.name}`, 'Subtotal', ...(showOpening ? [group.openingBalance] : []), group.debit, group.credit, group.balance,
          ...(showPrior ? [groupPrior, group.balance - groupPrior] : [])])
      }
    } else if (byTaxonomy) {
      body.push(['Taxonomia', ...head.slice(1)])
      for (const group of taxonomyGroups) {
        for (const row of group.rows) body.push(['', ...cells(row).slice(1)])
        const groupPrior = priorOfRows(group.rows)
        body.push([group.label, 'Subtotal', ...(showOpening ? [group.openingBalance] : []), group.debit, group.credit, group.balance,
          ...(showPrior ? [groupPrior, group.balance - groupPrior] : [])])
      }
    } else {
      body.push(head)
      for (const row of rows) body.push(cells(row))
    }
    body.push(['Totais', '', ...(showOpening ? [data.totalOpeningBalance] : []), data.totalDebit, data.totalCredit, '',
      ...(showPrior ? [priorTotalBalance, ''] : [])])
    downloadCsv(csvFilename('balancete', activeCompany.legalName), toCsv(body))
  }

  const numericCells = (item: { openingBalance: number; debit: number; credit: number; balance: number }, priorBalance: number | null, strongBalance = false) => <>
    {showOpening && <td className="numeric">{euro.format(item.openingBalance)}</td>}
    <td className="numeric">{euro.format(item.debit)}</td>
    <td className="numeric">{euro.format(item.credit)}</td>
    <td className={`numeric ${strongBalance ? 'strong' : ''}`}>{euro.format(item.balance)}</td>
    {showPrior && <><td className="numeric">{euro.format(priorBalance ?? 0)}</td><td className="numeric strong">{euro.format(item.balance - (priorBalance ?? 0))}</td></>}
  </>

  const emptyNumericCells = <>{showOpening && <td />}<td /><td /><td />{showPrior && <><td /><td /></>}</>

  const renderGroup = (key: string, headerLabel: string, subtotalLabel: string, group: { rows: Row[]; openingBalance: number; debit: number; credit: number; balance: number }) => {
    const open = detailInGroups || expanded.has(key)
    const gp = paginate(group.rows, pageSize, groupPage[key] ?? 1)
    return <tbody key={key}>
      <tr className="group-row"><td colSpan={2}>{headerLabel}{!detailInGroups && <> · {group.rows.length} contas <button type="button" className="text-link group-toggle" onClick={() => toggleGroup(key)}>{expanded.has(key) ? 'ocultar' : 'ver contas'}</button></>}</td>{emptyNumericCells}</tr>
      {open && gp.items.map((row) => <tr key={row.accountCode}><td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td><td>{row.accountName}</td>{numericCells(row, priorBalanceOf.get(row.accountCode) ?? 0)}</tr>)}
      {open && gp.totalPages > 1 && <tr className="group-pager-row"><td colSpan={colCount}><Pager currentPage={gp.currentPage} totalPages={gp.totalPages} onPage={(next) => setGroupPage((current) => ({ ...current, [key]: next }))} /></td></tr>}
      <tr className="subtotal-row"><td colSpan={2}>{subtotalLabel}</td>{numericCells(group, priorOfRows(group.rows), true)}</tr>
    </tbody>
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Balancete</h1><p>Somas e saldos por conta de movimento, sem os lançamentos anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <ReportChrome reportKey="balancete" title="Balancete" from={fromDate} to={toDate} />
    <section className="metric-grid compact-grid">
      <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Total de débito</small><strong>{euro.format(data.totalDebit)}</strong><span>{data.rows.length} contas</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${balanced ? 'blue' : 'rose'}`}><Scale /></span><div><small>Total de crédito</small><strong>{euro.format(data.totalCredit)}</strong><span>{balanced ? 'Balancete equilibrado' : 'Débito e crédito diferentes'}</span></div></article>
    </section>
    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou descrição…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label className="toggle-inline"><input type="checkbox" checked={byClass} onChange={(event) => { setByClass(event.target.checked); if (event.target.checked) setByTaxonomy(false) }} /><Layers size={15} /> Agrupar por classe</label>
      <label className="toggle-inline"><input type="checkbox" checked={byTaxonomy} onChange={(event) => { setByTaxonomy(event.target.checked); if (event.target.checked) setByClass(false) }} /><Tags size={15} /> Agrupar por taxonomia</label>
      <label className="toggle-inline"><input type="checkbox" checked={compare} onChange={(event) => setCompare(event.target.checked)} /><GitCompare size={15} /> Comparar com período homólogo</label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={rows.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>
    {grouped && !detailInGroups && <p className="save-status"><Layers size={15} /> Agrupamento: a mostrar os subtotais das {(byClass ? groups.length : taxonomyGroups.length)} {byClass ? 'classes' : 'rubricas'}. Expanda uma para ver as suas contas, ou desligue o agrupamento (a lista fica paginada).</p>}
    <section className="panel"><div className="table-wrap"><table>
      <thead><tr><th>Conta</th><th>Descrição</th>{showOpening && <th className="numeric">Abertura</th>}<th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th>{showPrior && <><th className="numeric">Saldo homólogo</th><th className="numeric">Variação</th></>}</tr></thead>
      {byClass
        ? groups.map((group) => renderGroup(group.digit, `${group.digit} · ${group.name}`, `Subtotal classe ${group.digit}`, group))
        : byTaxonomy
        ? taxonomyGroups.map((group) => renderGroup(group.key || 'sem-taxonomia', group.label, `Subtotal ${group.label}`, group))
        : <tbody>
          {pagedRows.map((row) => <tr key={row.accountCode}><td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td><td>{row.accountName}</td>{numericCells(row, priorBalanceOf.get(row.accountCode) ?? 0, true)}</tr>)}
        </tbody>}
      <tfoot><tr><td colSpan={2}>Totais</td>{showOpening && <td className="numeric strong">{euro.format(data.totalOpeningBalance)}</td>}<td className="numeric strong">{euro.format(data.totalDebit)}</td><td className="numeric strong">{euro.format(data.totalCredit)}</td><td />{showPrior && <><td className="numeric">{euro.format(priorTotalBalance)}</td><td /></>}</tr></tfoot>
    </table></div>
    {!grouped && totalPages > 1 && <div className="pager">
      <button className="button button-secondary" type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}><ChevronLeft size={15} /> Anterior</button>
      <span>Página {currentPage} de {totalPages}</span>
      <button className="button button-secondary" type="button" disabled={currentPage === totalPages} onClick={() => setPage(currentPage + 1)}>Seguinte <ChevronRight size={15} /></button>
    </div>}
    {data.rows.length === 0 && <p className="empty-state">Sem movimentos para apresentar.</p>}</section>
  </div>
}
