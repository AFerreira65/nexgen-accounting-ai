import { ArrowLeft, Contact, Database, Download, Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type PartyBalances, type PartyStatement } from '../app/demo-data'
import { Pager } from '../app/Pager'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const partyNumber = (countryCode: string, taxNumber: string) => `${countryCode !== 'PT' ? `${countryCode} ` : ''}${taxNumber}`

// Positivo é a receber (devedor); negativo é a pagar (credor).
function BalanceCell({ value }: { value: number }) {
  if (value === 0) return <>—</>
  return <>{euro.format(Math.abs(value))} <small>{value > 0 ? 'a receber' : 'a pagar'}</small></>
}

export function PartyBalancesByPartyPage() {
  const { activeCompany, source, partyBalances } = useWorkContext()
  const companyId = activeCompany.companyId
  const pageSize = activeCompany.settings.pageSize || 50
  const onDatabase = source === 'database'
  const [search, setSearch] = useState('')
  const [cutoff, setCutoff] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PartyBalances | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!onDatabase) { setResult(null); return }
    const controller = new AbortController()
    setError('')
    const timer = window.setTimeout(() => {
      void partyBalances(companyId, { to: cutoff, search: search.trim(), page, pageSize })
        .then((value) => { if (!controller.signal.aborted) setResult(value) })
        .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os saldos.') })
    }, 200)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [companyId, onDatabase, partyBalances, cutoff, search, page, pageSize])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Saldos de terceiros</h1><p>O saldo de cada cliente e fornecedor, a partir das linhas de lançamento que o indicam.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Contact /></span><h2>Requer ligação ao SQL Server</h2><p>Os saldos por terceiro são calculados na base de dados da empresa.</p></section>
    </div>
  }

  const rows = result?.items ?? []
  const totalPages = Math.max(1, Math.ceil((result?.total ?? 0) / pageSize))
  const exportCsv = () => downloadCsv(
    csvFilename('saldos-terceiros', activeCompany.legalName),
    toCsv([['Contribuinte', 'Nome', 'Débito', 'Crédito', 'Saldo'], ...rows.map((row) => [partyNumber(row.countryCode, row.taxRegistrationNumber), row.name, row.debit, row.credit, row.balance])]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Saldos de terceiros</h1><p>O saldo de cada cliente e fornecedor, somando as linhas de lançamento que o indicam — independentemente da conta. As linhas sem terceiro não entram; o saldo por subconta continua em <Link to="/contas-correntes">Contas correntes</Link>.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar terceiros</span><input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} placeholder="Pesquisar nome ou contribuinte…" /></label>
      <label className="inline-check">Até <input type="date" aria-label="Saldos até" value={cutoff} onChange={(event) => { setCutoff(event.target.value); setPage(1) }} /></label>
      <span className="catalog-count">{result ? `${result.total} ${result.total === 1 ? 'terceiro' : 'terceiros'} · saldo ${euro.format(result.totalBalance)}` : 'A carregar…'}</span>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={rows.length === 0}><Download size={16} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Contribuinte</th><th>Nome</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.partyId}>
          <td><Link className="primary-cell" to={`/terceiros/${row.partyId}`}>{partyNumber(row.countryCode, row.taxRegistrationNumber)}</Link></td>
          <td>{row.name}</td>
          <td className="numeric">{euro.format(row.debit)}</td>
          <td className="numeric">{euro.format(row.credit)}</td>
          <td className="numeric strong"><BalanceCell value={row.balance} /></td>
        </tr>)}</tbody>
      </table></div>
      {result && rows.length === 0 && <p className="empty-state">Ainda não há linhas de lançamento com terceiro. Indique o terceiro ao lançar, ou importe os terceiros antes de importar os movimentos de um SAF-T.</p>}
      <Pager currentPage={Math.min(page, totalPages)} totalPages={totalPages} onPage={setPage} />
    </section>
  </div>
}

export function PartyStatementPage() {
  const { partyId = '' } = useParams()
  const { activeCompany, source, partyStatement } = useWorkContext()
  const companyId = activeCompany.companyId
  const pageSize = activeCompany.settings.pageSize || 50
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const { fromDate: from, toDate: to, setFromDate: setFrom, setToDate: setTo } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, `${companyId}-${partyId}`)
  const [page, setPage] = useState(1)
  const [statement, setStatement] = useState<PartyStatement | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(statement ? 'terceiros' : undefined, statement?.partyId, statement?.name)

  useEffect(() => {
    if (!onDatabase || !partyId) { setStatement(null); return }
    const controller = new AbortController()
    setError('')
    void partyStatement(companyId, partyId, { from, to, page, pageSize })
      .then((value) => { if (!controller.signal.aborted) setStatement(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setStatement(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o extrato.') } })
    return () => controller.abort()
  }, [companyId, onDatabase, partyId, partyStatement, from, to, page, pageSize])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Terceiros</p><h1>Extrato de terceiro</h1></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Contact /></span><h2>Requer ligação ao SQL Server</h2><p>O extrato é lido da base de dados da empresa.</p></section>
    </div>
  }

  const totalPages = Math.max(1, Math.ceil((statement?.total ?? 0) / pageSize))
  const pick = (nextFrom: string, nextTo: string) => { setFrom(nextFrom); setTo(nextTo); setPage(1) }
  const exportCsv = () => statement && downloadCsv(
    csvFilename(`extrato-${statement.taxRegistrationNumber}`, activeCompany.legalName),
    toCsv([['Data', 'Movimento', 'Documento', 'Conta', 'Descrição', 'Débito', 'Crédito', 'Saldo'],
      ...statement.lines.map((line) => [line.date, line.reference, line.documentReference, line.accountCode, line.description, line.debit, line.credit, line.balance])]),
  )

  return <div className="page-stack">
    <Link className="back-link" to="/saldos-terceiros"><ArrowLeft size={16} /> Saldos de terceiros</Link>
    <header className="page-heading detail-heading"><div><p className="eyebrow">Extrato de terceiro</p><h1>{statement?.name ?? 'Terceiro'}</h1>{statement && <p>{partyNumber(statement.countryCode, statement.taxRegistrationNumber)}</p>}</div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={from} onChange={(event) => { setFrom(event.target.value); setPage(1) }} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={to} onChange={(event) => { setTo(event.target.value); setPage(1) }} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear} onPick={pick} />
      <button className="button button-secondary" type="button" onClick={() => pick('', '')}>Limpar datas</button>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={!statement || statement.lines.length === 0}><Download size={16} /> Exportar CSV</button>
    </div>

    {statement && <section className="metadata-strip">
      <span><small>Saldo anterior</small><strong><BalanceCell value={statement.openingBalance} /></strong></span>
      <span><small>Débito no período</small><strong>{euro.format(statement.periodDebit)}</strong></span>
      <span><small>Crédito no período</small><strong>{euro.format(statement.periodCredit)}</strong></span>
      <span><small>Saldo final</small><strong><BalanceCell value={statement.closingBalance} /></strong></span>
    </section>}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Data</th><th>Movimento</th><th>Documento</th><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{statement?.lines.map((line, index) => <tr key={`${line.reference}-${index}`}>
          <td>{formatMovementDate(line.date)}</td>
          <td><Link className="primary-cell" to={`/movimentos/${encodeURIComponent(line.reference)}`}>{line.reference}</Link></td>
          <td>{line.documentReference}</td>
          <td><Link to={`/contas/${line.accountCode}`}>{line.accountCode}</Link></td>
          <td>{line.description}</td>
          <td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td>
          <td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td>
          <td className="numeric strong"><BalanceCell value={line.balance} /></td>
        </tr>)}</tbody>
      </table></div>
      {statement && statement.lines.length === 0 && <p className="empty-state">Sem linhas com este terceiro no período.</p>}
      <Pager currentPage={Math.min(page, totalPages)} totalPages={totalPages} onPage={setPage} />
    </section>
  </div>
}
