import { Database, Download, GitCompare, Layers, Scale, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; openingBalance: number; debit: number; credit: number; balance: number }
type TrialBalance = { totalOpeningBalance: number; totalDebit: number; totalCredit: number; rows: Row[] }
type ClassGroup = { digit: string; name: string; rows: Row[]; openingBalance: number; debit: number; credit: number; balance: number }

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
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [byClass, setByClass] = useState(false)
  const [compare, setCompare] = useState(false)
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
  const priorBalanceOf = useMemo(() => new Map((prior?.rows ?? []).map((row) => [row.accountCode, row.balance])), [prior])
  const priorTotalBalance = (prior?.rows ?? []).reduce((sum, row) => sum + row.balance, 0)
  const balanced = Math.round((data.totalDebit - data.totalCredit) * 100) === 0
  const showOpening = Boolean(fromDate)
  const showPrior = compare && Boolean(fromDate) && Boolean(toDate)
  const priorOfRows = (list: Row[]) => list.reduce((sum, row) => sum + (priorBalanceOf.get(row.accountCode) ?? 0), 0)

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

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Balancete</h1><p>Somas e saldos por conta de movimento, sem os lançamentos anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <section className="metric-grid compact-grid">
      <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Total de débito</small><strong>{euro.format(data.totalDebit)}</strong><span>{data.rows.length} contas</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${balanced ? 'blue' : 'rose'}`}><Scale /></span><div><small>Total de crédito</small><strong>{euro.format(data.totalCredit)}</strong><span>{balanced ? 'Balancete equilibrado' : 'Débito e crédito diferentes'}</span></div></article>
    </section>
    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou descrição…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <label className="toggle-inline"><input type="checkbox" checked={byClass} onChange={(event) => setByClass(event.target.checked)} /><Layers size={15} /> Agrupar por classe</label>
      <label className="toggle-inline"><input type="checkbox" checked={compare} onChange={(event) => setCompare(event.target.checked)} /><GitCompare size={15} /> Comparar com período homólogo</label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={rows.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>
    <section className="panel"><div className="table-wrap"><table>
      <thead><tr><th>Conta</th><th>Descrição</th>{showOpening && <th className="numeric">Abertura</th>}<th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th>{showPrior && <><th className="numeric">Saldo homólogo</th><th className="numeric">Variação</th></>}</tr></thead>
      {byClass
        ? groups.map((group) => <tbody key={group.digit}>
          <tr className="group-row"><td colSpan={2}>{group.digit} · {group.name}</td>{emptyNumericCells}</tr>
          {group.rows.map((row) => <tr key={row.accountCode}><td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td><td>{row.accountName}</td>{numericCells(row, priorBalanceOf.get(row.accountCode) ?? 0)}</tr>)}
          <tr className="subtotal-row"><td colSpan={2}>Subtotal classe {group.digit}</td>{numericCells(group, priorOfRows(group.rows), true)}</tr>
        </tbody>)
        : <tbody>
          {rows.map((row) => <tr key={row.accountCode}><td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td><td>{row.accountName}</td>{numericCells(row, priorBalanceOf.get(row.accountCode) ?? 0, true)}</tr>)}
        </tbody>}
      <tfoot><tr><td colSpan={2}>Totais</td>{showOpening && <td className="numeric strong">{euro.format(data.totalOpeningBalance)}</td>}<td className="numeric strong">{euro.format(data.totalDebit)}</td><td className="numeric strong">{euro.format(data.totalCredit)}</td><td />{showPrior && <><td className="numeric">{euro.format(priorTotalBalance)}</td><td /></>}</tr></tfoot>
    </table></div>{data.rows.length === 0 && <p className="empty-state">Sem movimentos para apresentar.</p>}</section>
  </div>
}
