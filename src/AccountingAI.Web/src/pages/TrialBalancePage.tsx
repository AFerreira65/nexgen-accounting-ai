import { Database, Scale, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type Row = { accountCode: string; accountName: string; debit: number; credit: number; balance: number }
type TrialBalance = { totalDebit: number; totalCredit: number; rows: Row[] }

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): TrialBalance {
  const map = new Map<string, Row>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (from && movement.date < from) continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: nameOf(line.account), debit: 0, credit: 0, balance: 0 }
      row.debit += line.debit
      row.credit += line.credit
      row.balance = row.debit - row.credit
      map.set(line.account, row)
    }
  }
  const rows = [...map.values()].sort((a, b) => a.accountCode.localeCompare(b.accountCode, 'pt-PT'))
  return { rows, totalDebit: rows.reduce((sum, row) => sum + row.debit, 0), totalCredit: rows.reduce((sum, row) => sum + row.credit, 0) }
}

export function TrialBalancePage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [data, setData] = useState<TrialBalance>(() => fromMovements(movements, nameOf, '', ''))

  useEffect(() => {
    if (source !== 'database') {
      setData(fromMovements(movements, nameOf, fromDate, toDate))
      return
    }
    const params = new URLSearchParams()
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
      .then((result) => setData(result))
      .catch(() => { if (!controller.signal.aborted) setData(fromMovements(movements, nameOf, fromDate, toDate)) })
    return () => controller.abort()
  }, [activeCompany.companyId, fromDate, movements, nameOf, source, toDate, user.id])

  const rows = useMemo(() => {
    const text = search.trim().toLocaleLowerCase('pt-PT')
    return text ? data.rows.filter((row) => `${row.accountCode} ${row.accountName}`.toLocaleLowerCase('pt-PT').includes(text)) : data.rows
  }, [data.rows, search])
  const balanced = Math.round((data.totalDebit - data.totalCredit) * 100) === 0

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
    </div>
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead><tbody>
      {rows.map((row) => <tr key={row.accountCode}><td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td><td>{row.accountName}</td><td className="numeric">{euro.format(row.debit)}</td><td className="numeric">{euro.format(row.credit)}</td><td className="numeric strong">{euro.format(row.balance)}</td></tr>)}
    </tbody><tfoot><tr><td colSpan={2}>Totais</td><td className="numeric strong">{euro.format(data.totalDebit)}</td><td className="numeric strong">{euro.format(data.totalCredit)}</td><td /></tr></tfoot></table></div>{data.rows.length === 0 && <p className="empty-state">Sem movimentos para apresentar.</p>}</section>
  </div>
}
