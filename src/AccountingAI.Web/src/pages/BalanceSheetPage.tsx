import { Database, Download, Scale, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { groupBySubclass, type SubclassGroup, type SubclassLine } from '../app/accountClasses'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; balance: number }
type TrialBalance = { rows: (Row & { openingBalance: number; debit: number; credit: number })[] }

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): Row[] {
  const map = new Map<string, Row & { debit: number; credit: number }>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: nameOf(line.account), balance: 0, debit: 0, credit: 0 }
      row.debit += line.debit
      row.credit += line.credit
      map.set(line.account, row)
    }
  }
  return [...map.values()].map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, balance: row.debit - row.credit }))
}

export function BalanceSheetPage() {
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
      .then((result) => setRows(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, balance: row.balance }))))
      .catch(() => { if (!controller.signal.aborted) setRows(fromMovements(movements, nameOf, fromDate, toDate)) })
    return () => controller.abort()
  }, [activeCompany.companyId, fromDate, movements, nameOf, source, toDate, user.id])

  const assets = useMemo(() => groupBySubclass(rows
    .filter((row) => ['1', '3', '4'].includes(row.accountCode.charAt(0)) || (row.accountCode.charAt(0) === '2' && row.balance > 0))
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: row.balance }))), [rows])
  const liabilities = useMemo(() => groupBySubclass(rows
    .filter((row) => row.accountCode.charAt(0) === '2' && row.balance < 0)
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: -row.balance }))), [rows])
  const equityGroups = useMemo(() => groupBySubclass(rows
    .filter((row) => row.accountCode.charAt(0) === '5')
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: -row.balance }))), [rows])
  const netResult = useMemo(() => -rows
    .filter((row) => ['6', '7'].includes(row.accountCode.charAt(0)))
    .reduce((sum, row) => sum + row.balance, 0), [rows])

  const totalAssets = assets.total
  const totalLiabilities = liabilities.total
  const totalEquity = equityGroups.total + netResult
  const balanced = Math.round((totalAssets - totalLiabilities - totalEquity) * 100) === 0

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const match = (group: SubclassGroup) => !term
    || `${group.code} ${group.name}`.toLocaleLowerCase('pt-PT').includes(term)
    || group.lines.some((line) => `${line.code} ${line.name}`.toLocaleLowerCase('pt-PT').includes(term))

  const exportCsv = () => {
    const body: (string | number)[][] = [['Rubrica', 'Descrição', 'Montante']]
    const section = (title: string, groups: SubclassGroup[]) => {
      body.push([title, '', ''])
      for (const group of groups) {
        for (const line of group.lines) body.push([line.code, line.name, line.amount])
        body.push([group.code, group.name, group.amount])
      }
    }
    section('Ativo', assets.groups)
    body.push(['Total do ativo', '', totalAssets])
    section('Passivo', liabilities.groups)
    body.push(['Total do passivo', '', totalLiabilities])
    section('Capital próprio', equityGroups.groups)
    body.push(['Resultado líquido do período', '', netResult])
    body.push(['Total do capital próprio', '', totalEquity])
    body.push(['Total do passivo e capital próprio', '', totalLiabilities + totalEquity])
    downloadCsv(csvFilename('balanco', activeCompany.legalName), toCsv(body))
  }

  const renderGroups = (groups: SubclassGroup[]) => groups.filter(match).map((group) => <tbody key={group.code}>
    <tr className="group-row"><td colSpan={2}>{group.code} · {group.name}</td><td /></tr>
    {group.lines.map((line: SubclassLine) => <tr key={line.code}>
      <td><Link className="primary-cell" to={`/contas/${line.code}`}>{line.code}</Link></td>
      <td>{line.name}</td>
      <td className="numeric">{euro.format(line.amount)}</td>
    </tr>)}
    <tr className="subtotal-row"><td colSpan={2}>Subtotal {group.code}</td><td className="numeric strong">{euro.format(group.amount)}</td></tr>
  </tbody>)

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Balanço</h1><p>Síntese patrimonial: ativo (classes 1, 3, 4 e saldos devedores da 2), passivo (saldos credores da 2) e capital próprio (classe 5) com o resultado do período.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Total do ativo</small><strong>{euro.format(totalAssets)}</strong><span>Classes 1, 3, 4 e 2 devedora</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><Scale /></span><div><small>Total do passivo</small><strong>{euro.format(totalLiabilities)}</strong><span>Classe 2 credora</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><Scale /></span><div><small>Capital próprio</small><strong>{euro.format(totalEquity)}</strong><span>Classe 5 e resultado</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${balanced ? 'blue' : 'rose'}`}><Scale /></span><div><small>Equilíbrio</small><strong>{balanced ? 'Ativo = Passivo + CP' : 'Desequilíbrio'}</strong><span>{euro.format(totalAssets - totalLiabilities - totalEquity)}</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar rubricas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar rubrica ou conta…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={rows.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Ativo</h2><p>Por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
        {renderGroups(assets.groups)}
        <tfoot><tr><td colSpan={2}>Total do ativo</td><td className="numeric strong">{euro.format(totalAssets)}</td></tr></tfoot>
      </table></div>
      {assets.groups.length === 0 && <p className="empty-state">Sem ativo no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Passivo</h2><p>Por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
        {renderGroups(liabilities.groups)}
        <tfoot><tr><td colSpan={2}>Total do passivo</td><td className="numeric strong">{euro.format(totalLiabilities)}</td></tr></tfoot>
      </table></div>
      {liabilities.groups.length === 0 && <p className="empty-state">Sem passivo no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Capital próprio</h2><p>Classe 5 e resultado líquido do período.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
        {renderGroups(equityGroups.groups)}
        <tbody><tr className="subtotal-row"><td colSpan={2}>Resultado líquido do período</td><td className="numeric strong">{euro.format(netResult)}</td></tr></tbody>
        <tfoot>
          <tr><td colSpan={2}>Total do capital próprio</td><td className="numeric strong">{euro.format(totalEquity)}</td></tr>
          <tr><td colSpan={2}>Total do passivo e capital próprio</td><td className="numeric strong">{euro.format(totalLiabilities + totalEquity)}</td></tr>
        </tfoot>
      </table></div>
    </section>
  </div>
}
