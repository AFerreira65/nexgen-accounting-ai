import { Database, Download, Layers, Search, TrendingUp } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { groupBySubclass, type SubclassGroup } from '../app/accountClasses'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { PeriodShortcuts } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; debit: number; credit: number }
type TrialBalance = { rows: (Row & { openingBalance: number; balance: number })[] }
type Group = SubclassGroup

function build(rows: Row[], klass: '6' | '7'): { groups: Group[]; total: number } {
  const sign = klass === '6' ? 1 : -1
  return groupBySubclass(rows
    .filter((row) => row.accountCode.charAt(0) === klass)
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: sign * (row.debit - row.credit) })))
}

function shiftYear(date: string, delta: number) {
  if (!date) return ''
  return `${Number(date.slice(0, 4)) + delta}${date.slice(4)}`
}

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): Row[] {
  const map = new Map<string, Row>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (from && movement.date < from) continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: nameOf(line.account), debit: 0, credit: 0 }
      row.debit += line.debit
      row.credit += line.credit
      map.set(line.account, row)
    }
  }
  return [...map.values()]
}

export function IncomeStatementPage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [compare, setCompare] = useState(false)
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [rows, setRows] = useState<Row[]>(() => fromMovements(movements, nameOf, '', ''))
  const [priorRows, setPriorRows] = useState<Row[]>([])

  useEffect(() => {
    const controller = new AbortController()
    const load = (from: string, to: string, apply: (rows: Row[]) => void) => {
      if (source !== 'database') {
        apply(fromMovements(movements, nameOf, from, to))
        return
      }
      const params = new URLSearchParams()
      if (from) params.set('from', from)
      if (to) params.set('to', to)
      void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
        .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
        .then((result) => apply(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, debit: row.debit, credit: row.credit }))))
        .catch(() => { if (!controller.signal.aborted) apply(fromMovements(movements, nameOf, from, to)) })
    }
    load(fromDate, toDate, setRows)
    if (compare && fromDate && toDate) {
      load(shiftYear(fromDate, -1), shiftYear(toDate, -1), setPriorRows)
    } else {
      setPriorRows([])
    }
    return () => controller.abort()
  }, [activeCompany.companyId, compare, fromDate, movements, nameOf, source, toDate, user.id])

  const income = useMemo(() => build(rows, '7'), [rows])
  const expenses = useMemo(() => build(rows, '6'), [rows])
  const result = income.total - expenses.total
  const priorIncome = useMemo(() => build(priorRows, '7'), [priorRows])
  const priorExpenses = useMemo(() => build(priorRows, '6'), [priorRows])
  const priorResult = priorIncome.total - priorExpenses.total
  const showPrior = compare && Boolean(fromDate) && Boolean(toDate)
  const priorAmountOf = (groups: Group[]) => new Map(groups.map((group) => [group.code, group.amount]))

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const match = (group: Group) => !term || `${group.code} ${group.name}`.toLocaleLowerCase('pt-PT').includes(term)
    || group.lines.some((line) => `${line.code} ${line.name}`.toLocaleLowerCase('pt-PT').includes(term))
  const shownIncome = income.groups.filter(match)
  const shownExpenses = expenses.groups.filter(match)

  const exportCsv = () => {
    const head = showPrior ? ['Rubrica', 'Descrição', 'Montante', 'Homólogo', 'Variação'] : ['Rubrica', 'Descrição', 'Montante']
    const body: (string | number)[][] = [head]
    const section = (title: string, groups: Group[], total: number, prior: Map<string, number>, priorTotal: number) => {
      body.push([title, '', ...(showPrior ? ['', '', ''] : [''])])
      for (const group of groups) {
        for (const line of group.lines) body.push([line.code, line.name, line.amount, ...(showPrior ? ['', ''] : [])])
        const priorAmount = prior.get(group.code) ?? 0
        body.push(showPrior
          ? [group.code, group.name, group.amount, priorAmount, group.amount - priorAmount]
          : [group.code, group.name, group.amount])
      }
      body.push(showPrior
        ? [`Total ${title.toLocaleLowerCase('pt-PT')}`, '', total, priorTotal, total - priorTotal]
        : [`Total ${title.toLocaleLowerCase('pt-PT')}`, '', total])
    }
    section('Rendimentos', income.groups, income.total, priorAmountOf(priorIncome.groups), priorIncome.total)
    section('Gastos', expenses.groups, expenses.total, priorAmountOf(priorExpenses.groups), priorExpenses.total)
    body.push(showPrior
      ? ['Resultado líquido do período', '', result, priorResult, result - priorResult]
      : ['Resultado líquido do período', '', result])
    downloadCsv(csvFilename('demonstracao-resultados', activeCompany.legalName), toCsv(body))
  }

  const renderSection = (groups: Group[], prior: Map<string, number>) => groups.map((group) => {
    const priorAmount = prior.get(group.code) ?? 0
    return <tbody key={group.code}>
      <tr className="group-row"><td colSpan={2}>{group.code} · {group.name}</td><td />{showPrior && <><td /><td /></>}</tr>
      {group.lines.map((line) => <tr key={line.code}>
        <td><Link className="primary-cell" to={`/contas/${line.code}`}>{line.code}</Link></td>
        <td>{line.name}</td>
        <td className="numeric">{euro.format(line.amount)}</td>
        {showPrior && <><td /><td /></>}
      </tr>)}
      <tr className="subtotal-row"><td colSpan={2}>Subtotal {group.code}</td><td className="numeric strong">{euro.format(group.amount)}</td>{showPrior && <><td className="numeric">{euro.format(priorAmount)}</td><td className="numeric strong">{euro.format(group.amount - priorAmount)}</td></>}</tr>
    </tbody>
  })

  const totalsRow = (label: string, current: number, prior: number) => <tr>
    <td colSpan={2}>{label}</td><td className="numeric strong">{euro.format(current)}</td>
    {showPrior && <><td className="numeric">{euro.format(prior)}</td><td className="numeric strong">{euro.format(current - prior)}</td></>}
  </tr>

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Demonstração de resultados</h1><p>Síntese por naturezas: rendimentos (classe 7) e gastos (classe 6) do período, sem os lançamentos anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><TrendingUp /></span><div><small>Rendimentos</small><strong>{euro.format(income.total)}</strong><span>{showPrior ? `Homólogo ${euro.format(priorIncome.total)}` : 'Classe 7'}</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><TrendingUp /></span><div><small>Gastos</small><strong>{euro.format(expenses.total)}</strong><span>{showPrior ? `Homólogo ${euro.format(priorExpenses.total)}` : 'Classe 6'}</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${result >= 0 ? 'blue' : 'rose'}`}><TrendingUp /></span><div><small>Resultado líquido do período</small><strong>{euro.format(result)}</strong><span>{showPrior ? `Homólogo ${euro.format(priorResult)}` : (result >= 0 ? 'Lucro' : 'Prejuízo')}</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar rubricas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar rubrica ou conta…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label className="toggle-inline"><input type="checkbox" checked={compare} onChange={(event) => setCompare(event.target.checked)} /><Layers size={15} /> Comparar com período homólogo</label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={income.groups.length === 0 && expenses.groups.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Rendimentos</h2><p>Classe 7, por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th>{showPrior && <><th className="numeric">Homólogo</th><th className="numeric">Variação</th></>}</tr></thead>
        {renderSection(shownIncome, priorAmountOf(priorIncome.groups))}
        <tfoot>{totalsRow('Total de rendimentos', income.total, priorIncome.total)}</tfoot>
      </table></div>
      {income.groups.length === 0 && <p className="empty-state">Sem rendimentos no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Gastos</h2><p>Classe 6, por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th>{showPrior && <><th className="numeric">Homólogo</th><th className="numeric">Variação</th></>}</tr></thead>
        {renderSection(shownExpenses, priorAmountOf(priorExpenses.groups))}
        <tfoot>
          {totalsRow('Total de gastos', expenses.total, priorExpenses.total)}
          {totalsRow('Resultado líquido do período', result, priorResult)}
        </tfoot>
      </table></div>
      {expenses.groups.length === 0 && <p className="empty-state">Sem gastos no período.</p>}
    </section>
  </div>
}
