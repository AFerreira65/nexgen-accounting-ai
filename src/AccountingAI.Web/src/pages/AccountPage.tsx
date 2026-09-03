import { ArrowLeft, BookOpen, Database, Download, Landmark, StickyNote } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type LedgerEntry = {
  reference: string; entryDate: string; journalCode: string; description: string; notes?: string | null
  debit: number; credit: number; runningBalance: number
}
type Ledger = {
  accountCode: string; accountName: string; openingBalance: number; balance: number
  entries: LedgerEntry[]
}

function fromMovements(movements: Movement[], accountCode: string, accountName: string, from: string, to: string): Ledger {
  const relevant = movements
    .filter((movement) => movement.status !== 'Anulado')
    .filter((movement) => movement.lines.some((line) => line.account === accountCode))
    .map((movement) => {
      const line = movement.lines.find((item) => item.account === accountCode)!
      return {
        reference: movement.id, entryDate: movement.date, journalCode: movement.journalId,
        description: line.description, notes: movement.notes, debit: line.debit, credit: line.credit,
      }
    })
    .sort((a, b) => (a.entryDate + a.reference).localeCompare(b.entryDate + b.reference))

  const openingBalance = from
    ? relevant.filter((item) => item.entryDate < from).reduce((sum, item) => sum + item.debit - item.credit, 0)
    : 0
  const period = relevant.filter((item) => (!from || item.entryDate >= from) && (!to || item.entryDate <= to))

  let running = openingBalance
  const entries = period.map((item) => {
    running += item.debit - item.credit
    return { ...item, runningBalance: running }
  })
  return { accountCode, accountName, openingBalance, balance: running, entries }
}

export function AccountPage() {
  const { accountCode = '' } = useParams()
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const nameOf = useMemo(
    () => accountingSetup.accounts.find((account) => account.code === accountCode)?.name
      ?? movements.flatMap((movement) => movement.lines).find((line) => line.account === accountCode)?.accountName
      ?? 'Conta contabilística',
    [accountCode, accountingSetup.accounts, movements],
  )
  const [data, setData] = useState<Ledger>(() => fromMovements(movements, accountCode, nameOf, '', ''))

  useEffect(() => {
    if (source !== 'database') {
      setData(fromMovements(movements, accountCode, nameOf, fromDate, toDate))
      return
    }
    const params = new URLSearchParams()
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    const controller = new AbortController()
    void fetch(
      `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/accounts/${encodeURIComponent(accountCode)}/ledger?${params}`,
      { signal: controller.signal },
    )
      .then(async (response) => (response.ok ? (await response.json()) as Ledger : Promise.reject(new Error())))
      .then((result) => setData(result))
      .catch(() => { if (!controller.signal.aborted) setData(fromMovements(movements, accountCode, nameOf, fromDate, toDate)) })
    return () => controller.abort()
  }, [accountCode, activeCompany.companyId, fromDate, movements, nameOf, source, toDate, user.id])

  const inPeriod = Boolean(fromDate || toDate)

  const exportCsv = () => {
    const header = ['Movimento', 'Data', 'Diário', 'Descrição', 'Débito', 'Crédito', 'Saldo']
    const opening = inPeriod ? [['Saldo de abertura', '', '', '', '', '', data.openingBalance]] : []
    const body = data.entries.map((entry) => [
      entry.reference, entry.entryDate, entry.journalCode, entry.description, entry.debit, entry.credit, entry.runningBalance,
    ])
    downloadCsv(csvFilename(`razao-${data.accountCode || accountCode}`, activeCompany.legalName), toCsv([header, ...opening, ...body]))
  }

  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading">
      <div>
        <p className="eyebrow">Razão da conta</p>
        <div className="heading-line"><span className="heading-symbol"><BookOpen /></span><h1>{data.accountCode || accountCode} · {data.accountName || nameOf}</h1></div>
        <p>Movimentos da conta por ordem cronológica, com saldo acumulado.</p>
      </div>
      <span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span>
    </header>

    <section className="metric-grid compact-grid">
      {inPeriod && <article className="metric-card"><div><small>Saldo de abertura</small><strong>{euro.format(data.openingBalance)}</strong><span>Antes de {fromDate || '—'}</span></div></article>}
      <article className="metric-card"><div><small>Saldo {inPeriod ? 'final' : 'atual'}</small><strong>{euro.format(data.balance)}</strong><span>{inPeriod ? 'No fim do período' : `Exercício de ${activeCompany.settings.defaultFiscalYear ?? 'atual'}`}</span></div></article>
      <article className="metric-card"><div><small>Movimentos</small><strong>{data.entries.length}</strong><span>Com ligação direta ao lançamento</span></div></article>
    </section>

    <div className="filterbar">
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={data.entries.length === 0 && !inPeriod}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Movimentos desta conta</h2><p>Clique num movimento ou diário para consultar o contexto.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Movimento</th><th>Diário</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>
          {inPeriod && <tr><td colSpan={5}>Saldo de abertura</td><td className="numeric strong">{euro.format(data.openingBalance)}</td></tr>}
          {data.entries.map((entry) => <tr key={entry.reference}>
            <td><Link className="primary-cell" to={`/movimentos/${entry.reference}`}>{entry.reference.replaceAll('-', ' ')}</Link><small>{formatMovementDate(entry.entryDate)}</small></td>
            <td><Link className="inline-link" to={`/diarios/${entry.journalCode}`}><Landmark size={14} /> {entry.journalCode}</Link></td>
            <td>{entry.description}{entry.notes ? <span className="note-flag" title={entry.notes}><StickyNote size={13} /> nota</span> : null}</td>
            <td className="numeric">{entry.debit ? euro.format(entry.debit) : '—'}</td>
            <td className="numeric">{entry.credit ? euro.format(entry.credit) : '—'}</td>
            <td className="numeric strong">{euro.format(entry.runningBalance)}</td>
          </tr>)}
        </tbody>
      </table></div>
      {data.entries.length === 0 && <p className="empty-state">Sem movimentos para esta conta.</p>}
    </section>
  </div>
}
