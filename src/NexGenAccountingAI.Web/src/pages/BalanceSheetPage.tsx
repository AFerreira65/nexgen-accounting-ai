import { Database, Download, GitCompare, Scale, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { groupBySubclass, type SubclassGroup, type SubclassLine } from '../app/accountClasses'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { PeriodShortcuts } from '../app/periods'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'
import { signalLoadFailure } from '../app/loadFailure'

type Row = { accountCode: string; accountName: string; balance: number }
type TrialBalance = { rows: (Row & { openingBalance: number; debit: number; credit: number })[] }

function shiftYear(date: string, delta: number) {
  return date ? `${Number(date.slice(0, 4)) + delta}${date.slice(4)}` : ''
}

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): Row[] {
  const map = new Map<string, Row & { debit: number; credit: number }>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (from && movement.date < from) continue
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

function statement(rows: Row[]) {
  const assets = groupBySubclass(rows
    .filter((row) => ['1', '3', '4'].includes(row.accountCode.charAt(0)) || (row.accountCode.charAt(0) === '2' && row.balance > 0))
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: row.balance })))
  const liabilities = groupBySubclass(rows
    .filter((row) => row.accountCode.charAt(0) === '2' && row.balance < 0)
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: -row.balance })))
  const equityGroups = groupBySubclass(rows
    .filter((row) => row.accountCode.charAt(0) === '5')
    .map((row) => ({ code: row.accountCode, name: row.accountName, amount: -row.balance })))
  const netResult = -rows
    .filter((row) => ['6', '7'].includes(row.accountCode.charAt(0)))
    .reduce((sum, row) => sum + row.balance, 0)
  return {
    assets, liabilities, equityGroups, netResult,
    totalAssets: assets.total,
    totalLiabilities: liabilities.total,
    totalEquity: equityGroups.total + netResult,
  }
}

export function BalanceSheetPage() {
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
      // Sem o apuramento: com ele as classes 6 e 7 ficam a zero e o resultado vai para a 81.
      params.set('excludeClosing', 'true')
      void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
        .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
        .then((result) => apply(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, balance: row.balance }))))
        .catch(() => { if (!controller.signal.aborted) { signalLoadFailure(); apply(fromMovements([], nameOf, from, to)) } })
    }
    load(fromDate, toDate, setRows)
    if (compare && fromDate && toDate) {
      load(shiftYear(fromDate, -1), shiftYear(toDate, -1), setPriorRows)
    } else {
      setPriorRows([])
    }
    return () => controller.abort()
  }, [activeCompany.companyId, compare, fromDate, movements, nameOf, source, toDate, user.id])

  const current = useMemo(() => statement(rows), [rows])
  const prior = useMemo(() => statement(priorRows), [priorRows])
  const showPrior = compare && Boolean(fromDate) && Boolean(toDate)
  const balanced = Math.round((current.totalAssets - current.totalLiabilities - current.totalEquity) * 100) === 0

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const match = (group: SubclassGroup) => !term
    || `${group.code} ${group.name}`.toLocaleLowerCase('pt-PT').includes(term)
    || group.lines.some((line) => `${line.code} ${line.name}`.toLocaleLowerCase('pt-PT').includes(term))
  const priorAmountOf = (groups: SubclassGroup[]) => new Map(groups.map((group) => [group.code, group.amount]))

  // Numa empresa grande, a classe 2 traz milhares de contas de terceiros;
  // acima do tamanho de página mostram-se só os subtotais por subclasse e
  // remete-se para o balancete (paginado e pesquisável) para ver as contas.
  const totalLines = [current.assets.groups, current.liabilities.groups, current.equityGroups.groups]
    .reduce((sum, groups) => sum + groups.reduce((n, group) => n + group.lines.length, 0), 0)
  const detailInGroups = totalLines <= (activeCompany.settings.pageSize || 50)

  const exportCsv = () => {
    const head = showPrior ? ['Rubrica', 'Descrição', 'Montante', 'Homólogo', 'Variação'] : ['Rubrica', 'Descrição', 'Montante']
    const body: (string | number)[][] = [head]
    const line3 = (a: string, b: string, c: number | string, priorValue?: number) => showPrior
      ? [a, b, c, priorValue ?? 0, typeof c === 'number' ? c - (priorValue ?? 0) : '']
      : [a, b, c]
    const section = (title: string, groups: SubclassGroup[], priorGroups: SubclassGroup[]) => {
      const priorMap = priorAmountOf(priorGroups)
      body.push(line3(title, '', ''))
      for (const group of groups) {
        for (const l of group.lines) body.push(line3(l.code, l.name, l.amount))
        body.push(line3(group.code, group.name, group.amount, priorMap.get(group.code) ?? 0))
      }
    }
    section('Ativo', current.assets.groups, prior.assets.groups)
    body.push(line3('Total do ativo', '', current.totalAssets, prior.totalAssets))
    section('Passivo', current.liabilities.groups, prior.liabilities.groups)
    body.push(line3('Total do passivo', '', current.totalLiabilities, prior.totalLiabilities))
    section('Capital próprio', current.equityGroups.groups, prior.equityGroups.groups)
    body.push(line3('Resultado líquido do período', '', current.netResult, prior.netResult))
    body.push(line3('Total do capital próprio', '', current.totalEquity, prior.totalEquity))
    body.push(line3('Total do passivo e capital próprio', '', current.totalLiabilities + current.totalEquity, prior.totalLiabilities + prior.totalEquity))
    downloadCsv(csvFilename('balanco', activeCompany.legalName), toCsv(body))
  }

  const emptyPrior = showPrior ? <><td /><td /></> : null

  const renderGroups = (groups: SubclassGroup[], priorGroups: SubclassGroup[]) => {
    const priorMap = priorAmountOf(priorGroups)
    return groups.filter(match).map((group) => {
      const p = priorMap.get(group.code) ?? 0
      return <tbody key={group.code}>
        <tr className="group-row"><td colSpan={2}>{group.code} · {group.name}{!detailInGroups ? ` · ${group.lines.length} contas` : ''}</td><td />{emptyPrior}</tr>
        {detailInGroups && group.lines.map((line: SubclassLine) => <tr key={line.code}>
          <td><Link className="primary-cell" to={`/contas/${line.code}`}>{line.code}</Link></td>
          <td>{line.name}</td>
          <td className="numeric">{euro.format(line.amount)}</td>
          {emptyPrior}
        </tr>)}
        <tr className="subtotal-row"><td colSpan={2}>Subtotal {group.code}</td><td className="numeric strong">{euro.format(group.amount)}</td>{showPrior && <><td className="numeric">{euro.format(p)}</td><td className="numeric strong">{euro.format(group.amount - p)}</td></>}</tr>
      </tbody>
    })
  }

  const totalsRow = (label: string, value: number, priorValue: number) => <tr>
    <td colSpan={2}>{label}</td><td className="numeric strong">{euro.format(value)}</td>
    {showPrior && <><td className="numeric">{euro.format(priorValue)}</td><td className="numeric strong">{euro.format(value - priorValue)}</td></>}
  </tr>

  const columns = <thead><tr><th>Rubrica</th><th>Descrição</th><th className="numeric">Montante</th>{showPrior && <><th className="numeric">Homólogo</th><th className="numeric">Variação</th></>}</tr></thead>

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Balanço</h1><p>Síntese patrimonial: ativo (classes 1, 3, 4 e saldos devedores da 2), passivo (saldos credores da 2) e capital próprio (classe 5) com o resultado do período.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <ReportChrome reportKey="balanco" title="Balanço" from={fromDate} to={toDate} />

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Total do ativo</small><strong>{euro.format(current.totalAssets)}</strong><span>{showPrior ? `Homólogo ${euro.format(prior.totalAssets)}` : 'Classes 1, 3, 4 e 2 devedora'}</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><Scale /></span><div><small>Total do passivo</small><strong>{euro.format(current.totalLiabilities)}</strong><span>{showPrior ? `Homólogo ${euro.format(prior.totalLiabilities)}` : 'Classe 2 credora'}</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><Scale /></span><div><small>Capital próprio</small><strong>{euro.format(current.totalEquity)}</strong><span>{showPrior ? `Homólogo ${euro.format(prior.totalEquity)}` : 'Classe 5 e resultado'}</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${balanced ? 'blue' : 'rose'}`}><Scale /></span><div><small>Equilíbrio</small><strong>{balanced ? 'Ativo = Passivo + CP' : 'Desequilíbrio'}</strong><span>{euro.format(current.totalAssets - current.totalLiabilities - current.totalEquity)}</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar rubricas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar rubrica ou conta…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label className="toggle-inline"><input type="checkbox" checked={compare} onChange={(event) => setCompare(event.target.checked)} /><GitCompare size={15} /> Comparar com período homólogo</label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={rows.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    {!detailInGroups && <p className="save-status"><Scale size={15} /> A mostrar só os subtotais por subclasse — {totalLines} contas ao todo. Para ver as contas, abra o <Link className="inline-link" to="/balancete">balancete</Link> (paginado e pesquisável). A exportação CSV inclui as contas.</p>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Ativo</h2><p>Por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        {columns}
        {renderGroups(current.assets.groups, prior.assets.groups)}
        <tfoot>{totalsRow('Total do ativo', current.totalAssets, prior.totalAssets)}</tfoot>
      </table></div>
      {current.assets.groups.length === 0 && <p className="empty-state">Sem ativo no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Passivo</h2><p>Por subclasse.</p></div></div>
      <div className="table-wrap"><table>
        {columns}
        {renderGroups(current.liabilities.groups, prior.liabilities.groups)}
        <tfoot>{totalsRow('Total do passivo', current.totalLiabilities, prior.totalLiabilities)}</tfoot>
      </table></div>
      {current.liabilities.groups.length === 0 && <p className="empty-state">Sem passivo no período.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Capital próprio</h2><p>Classe 5 e resultado líquido do período.</p></div></div>
      <div className="table-wrap"><table>
        {columns}
        {renderGroups(current.equityGroups.groups, prior.equityGroups.groups)}
        <tbody><tr className="subtotal-row"><td colSpan={2}>Resultado líquido do período</td><td className="numeric strong">{euro.format(current.netResult)}</td>{showPrior && <><td className="numeric">{euro.format(prior.netResult)}</td><td className="numeric strong">{euro.format(current.netResult - prior.netResult)}</td></>}</tr></tbody>
        <tfoot>
          {totalsRow('Total do capital próprio', current.totalEquity, prior.totalEquity)}
          {totalsRow('Total do passivo e capital próprio', current.totalLiabilities + current.totalEquity, prior.totalLiabilities + prior.totalEquity)}
        </tfoot>
      </table></div>
    </section>
  </div>
}
