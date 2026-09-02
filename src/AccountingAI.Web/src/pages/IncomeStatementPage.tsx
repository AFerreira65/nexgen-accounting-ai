import { Database, Download, Search, TrendingUp } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; debit: number; credit: number }
type TrialBalance = { rows: (Row & { openingBalance: number; balance: number })[] }
type Line = { code: string; name: string; amount: number }
type Group = { code: string; name: string; amount: number; lines: Line[] }

const SUBCLASS_NAMES: Record<string, string> = {
  '61': 'Custo das mercadorias vendidas e das matérias consumidas',
  '62': 'Fornecimentos e serviços externos',
  '63': 'Gastos com o pessoal',
  '64': 'Gastos de depreciação e de amortização',
  '65': 'Perdas por imparidade',
  '66': 'Perdas por reduções de justo valor',
  '67': 'Provisões do período',
  '68': 'Outros gastos',
  '69': 'Gastos e perdas de financiamento',
  '71': 'Vendas',
  '72': 'Prestações de serviços',
  '73': 'Variações nos inventários da produção',
  '74': 'Trabalhos para a própria entidade',
  '75': 'Subsídios à exploração',
  '76': 'Reversões',
  '77': 'Ganhos por aumentos de justo valor',
  '78': 'Outros rendimentos',
  '79': 'Juros, dividendos e outros rendimentos similares',
}

function build(rows: Row[], klass: '6' | '7'): { groups: Group[]; total: number } {
  const sign = klass === '6' ? 1 : -1
  const byGroup = new Map<string, Group>()
  for (const row of rows) {
    if (row.accountCode.charAt(0) !== klass) continue
    const amount = sign * (row.debit - row.credit)
    if (amount === 0 && row.debit === 0 && row.credit === 0) continue
    const code = row.accountCode.slice(0, 2)
    let group = byGroup.get(code)
    if (!group) {
      group = { code, name: SUBCLASS_NAMES[code] ?? `Subclasse ${code}`, amount: 0, lines: [] }
      byGroup.set(code, group)
    }
    group.lines.push({ code: row.accountCode, name: row.accountName, amount })
    group.amount += amount
  }
  const groups = [...byGroup.values()].sort((a, b) => a.code.localeCompare(b.code, 'pt-PT'))
  groups.forEach((group) => group.lines.sort((a, b) => a.code.localeCompare(b.code, 'pt-PT')))
  return { groups, total: groups.reduce((sum, group) => sum + group.amount, 0) }
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
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [rows, setRows] = useState<Row[]>(() => fromMovements(movements, nameOf, '', ''))

  useEffect(() => {
    if (source !== 'database') {
      setRows(fromMovements(movements, nameOf, fromDate, toDate))
      return
    }
    const params = new URLSearchParams()
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
      .then((result) => setRows(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, debit: row.debit, credit: row.credit }))))
      .catch(() => { if (!controller.signal.aborted) setRows(fromMovements(movements, nameOf, fromDate, toDate)) })
    return () => controller.abort()
  }, [activeCompany.companyId, fromDate, movements, nameOf, source, toDate, user.id])

  const income = useMemo(() => build(rows, '7'), [rows])
  const expenses = useMemo(() => build(rows, '6'), [rows])
  const result = income.total - expenses.total

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const match = (group: Group) => !term || `${group.code} ${group.name}`.toLocaleLowerCase('pt-PT').includes(term)
    || group.lines.some((line) => `${line.code} ${line.name}`.toLocaleLowerCase('pt-PT').includes(term))
  const shownIncome = income.groups.filter(match)
  const shownExpenses = expenses.groups.filter(match)

  const exportCsv = () => {
    const body: (string | number)[][] = [['Rubrica', 'Descrição', 'Montante']]
    const section = (title: string, groups: Group[], total: number) => {
      body.push([title, '', ''])
      for (const group of groups) {
        for (const line of group.lines) body.push([line.code, line.name, line.amount])
        body.push([group.code, group.name, group.amount])
      }
      body.push([`Total ${title.toLocaleLowerCase('pt-PT')}`, '', total])
    }
    section('Rendimentos', income.groups, income.total)
    section('Gastos', expenses.groups, expenses.total)
    body.push(['Resultado líquido do período', '', result])
    downloadCsv(csvFilename('demonstracao-resultados', activeCompany.legalName), toCsv(body))
  }

  const renderSection = (groups: Group[]) => groups.map((group) => <tbody key={group.code}>
    <tr className="group-row"><td colSpan={2}>{group.code} · {group.name}</td><td /></tr>
    {group.lines.map((line) => <tr key={line.code}>
      <td><Link className="primary-cell" to={`/contas/${line.code}`}>{line.code}</Link></td>
      <td>{line.name}</td>
      <td className="numeric">{euro.format(line.amount)}</td>
    </tr>)}
    <tr className="subtotal-row"><td colSpan={2}>Subtotal {group.code}</td><td className="numeric strong">{euro.format(group.amount)}</td></tr>
  </tbody>)

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Demonstração de resultados</h1><p>Síntese por naturezas: rendimentos (classe 7) e gastos (classe 6) do período, sem os lançamentos anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><TrendingUp /></span><div><small>Rendimentos</small><strong>{euro.format(income.total)}</strong><span>Classe 7</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><TrendingUp /></span><div><small>Gastos</small><strong>{euro.format(expenses.total)}</strong><span>Classe 6</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${result >= 0 ? 'blue' : 'rose'}`}><TrendingUp /></span><div><small>Resultado líquido do período</small><strong>{euro.format(result)}</strong><span>{result >= 0 ? 'Lucro' : 'Prejuízo'}</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar rubricas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar rubrica ou conta…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={income.groups.length === 0 && expenses.groups.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Rendimentos</h2><p>Classe 7, por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
        {renderSection(shownIncome)}
        <tfoot><tr><td colSpan={2}>Total de rendimentos</td><td className="numeric strong">{euro.format(income.total)}</td></tr></tfoot>
      </table></div>
      {income.groups.length === 0 && <p className="empty-state">Sem rendimentos no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Gastos</h2><p>Classe 6, por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
        {renderSection(shownExpenses)}
        <tfoot>
          <tr><td colSpan={2}>Total de gastos</td><td className="numeric strong">{euro.format(expenses.total)}</td></tr>
          <tr><td colSpan={2}>Resultado líquido do período</td><td className="numeric strong">{euro.format(result)}</td></tr>
        </tfoot>
      </table></div>
      {expenses.groups.length === 0 && <p className="empty-state">Sem gastos no período.</p>}
    </section>
  </div>
}
