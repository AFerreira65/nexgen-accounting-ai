import { ArrowLeft, BookOpen, ChevronLeft, ChevronRight, Database, Download, Landmark, StickyNote } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { ENTRY_SOURCE_LABELS, ENTRY_SOURCES, entrySourceLabel } from '../app/entrySources'
import { paginate } from '../app/pagination'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'

type LedgerEntry = {
  reference: string; entryDate: string; journalCode: string; accountCode: string; description: string; notes?: string | null
  debit: number; credit: number; runningBalance: number; source: string
}
type Ledger = {
  accountCode: string; accountName: string; isAggregate: boolean; openingBalance: number; balance: number
  entries: LedgerEntry[]
}

function fromMovements(movements: Movement[], accountCode: string, accountName: string, from: string, to: string, isAggregate: boolean, sourceFilter: string): Ledger {
  const belongs = (code: string) => isAggregate ? code.startsWith(accountCode) : code === accountCode
  const relevant = movements
    .filter((movement) => movement.status !== 'Anulado')
    .flatMap((movement) => movement.lines
      .filter((line) => belongs(line.account))
      .map((line) => ({
        reference: movement.id, entryDate: movement.date, journalCode: movement.journalId, accountCode: line.account,
        description: line.description, notes: movement.notes, debit: line.debit, credit: line.credit, source: movement.source ?? 'Manual',
      })))
    .sort((a, b) => (a.entryDate + a.reference).localeCompare(b.entryDate + b.reference))

  const openingBalance = from
    ? relevant.filter((item) => item.entryDate < from).reduce((sum, item) => sum + item.debit - item.credit, 0)
    : 0
  const period = relevant.filter((item) => (!from || item.entryDate >= from) && (!to || item.entryDate <= to))

  // O saldo acumulado soma sempre todas as origens; o filtro só restringe as
  // linhas mostradas, calculado depois do saldo, para este continuar a bater
  // certo mesmo a ver só uma parte dos movimentos.
  let running = openingBalance
  const entries = period.map((item) => {
    running += item.debit - item.credit
    return { ...item, runningBalance: running }
  })
  const shown = sourceFilter ? entries.filter((entry) => entry.source === sourceFilter) : entries
  return { accountCode, accountName, isAggregate, openingBalance, balance: running, entries: shown }
}

export function AccountPage() {
  const { accountCode = '' } = useParams()
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  // A razão arranca no exercício da empresa (como a lista de movimentos e o
  // livro diário), dando a leitura habitual «abertura → movimentos → fecho»;
  // «Limpar datas» abre a conta inteira. Volta a acertar ao mudar de conta,
  // de empresa ou de exercício.
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(
    activeCompany.settings.defaultFiscalYear, `${accountCode}|${activeCompany.companyId}`,
  )
  const [sourceFilter, setSourceFilter] = useState('')
  const [page, setPage] = useState(1)
  const chartAccount = useMemo(
    () => accountingSetup.accounts.find((account) => account.code === accountCode),
    [accountCode, accountingSetup.accounts],
  )
  const nameOf = chartAccount?.name
    ?? movements.flatMap((movement) => movement.lines).find((line) => line.account === accountCode)?.accountName
    ?? 'Conta contabilística'
  // Uma conta do plano sem permissão de movimento é agregadora: soma as subcontas.
  const aggregate = chartAccount ? !chartAccount.allowsPosting : false
  const [data, setData] = useState<Ledger>(() => fromMovements(movements, accountCode, nameOf, '', '', aggregate, ''))

  useEffect(() => {
    if (source !== 'database') {
      setData(fromMovements(movements, accountCode, nameOf, fromDate, toDate, aggregate, sourceFilter))
      return
    }
    const params = new URLSearchParams()
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    if (sourceFilter) params.set('source', sourceFilter)
    const controller = new AbortController()
    void fetch(
      `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/accounts/${encodeURIComponent(accountCode)}/ledger?${params}`,
      { signal: controller.signal },
    )
      .then(async (response) => (response.ok ? (await response.json()) as Ledger : Promise.reject(new Error())))
      .then((result) => setData(result))
      .catch(() => { if (!controller.signal.aborted) setData(fromMovements(movements, accountCode, nameOf, fromDate, toDate, aggregate, sourceFilter)) })
    return () => controller.abort()
  }, [accountCode, activeCompany.companyId, aggregate, fromDate, movements, nameOf, source, sourceFilter, toDate, user.id])

  const inPeriod = Boolean(fromDate || toDate)

  // Uma conta de integração numa empresa grande soma milhares de linhas de
  // subcontas; pagina-se pelo tamanho de página da empresa, voltando à
  // primeira página quando a conta ou um filtro muda. O saldo acumulado de
  // cada linha já vem calculado, por isso a página não o afeta; a linha do
  // saldo de abertura só aparece na primeira página.
  const { items: pagedEntries, currentPage, totalPages } = paginate(data.entries, activeCompany.settings.pageSize, page)
  useEffect(() => setPage(1), [accountCode, fromDate, toDate, sourceFilter])

  const exportCsv = () => {
    const header = data.isAggregate
      ? ['Movimento', 'Data', 'Diário', 'Conta', 'Descrição', 'Origem', 'Débito', 'Crédito', 'Saldo']
      : ['Movimento', 'Data', 'Diário', 'Descrição', 'Origem', 'Débito', 'Crédito', 'Saldo']
    const pad = data.isAggregate ? [''] : []
    const opening = inPeriod ? [['Saldo de abertura', '', '', ...pad, '', '', '', '', data.openingBalance]] : []
    const body = data.entries.map((entry) => data.isAggregate
      ? [entry.reference, entry.entryDate, entry.journalCode, entry.accountCode, entry.description, entrySourceLabel(entry.source), entry.debit, entry.credit, entry.runningBalance]
      : [entry.reference, entry.entryDate, entry.journalCode, entry.description, entrySourceLabel(entry.source), entry.debit, entry.credit, entry.runningBalance])
    downloadCsv(csvFilename(`razao-${data.accountCode || accountCode}`, activeCompany.legalName), toCsv([header, ...opening, ...body]))
  }

  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading">
      <div>
        <p className="eyebrow">Razão da conta</p>
        <div className="heading-line"><span className="heading-symbol"><BookOpen /></span><h1>{data.accountCode || accountCode} · {data.accountName || nameOf}</h1></div>
        <p>{data.isAggregate ? 'Conta de integração: soma dos movimentos das subcontas, por ordem cronológica.' : 'Movimentos da conta por ordem cronológica, com saldo acumulado.'}</p>
      </div>
      <span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span>
    </header>

    <section className="metric-grid compact-grid">
      {inPeriod && <article className="metric-card"><div><small>Saldo de abertura</small><strong>{euro.format(data.openingBalance)}</strong><span>Antes de {fromDate || '—'}</span></div></article>}
      <article className="metric-card"><div><small>Saldo {inPeriod ? 'final' : 'atual'}</small><strong>{euro.format(data.balance)}</strong><span>{sourceFilter ? 'Todas as origens' : inPeriod ? 'No fim do período' : 'Todas as datas'}</span></div></article>
      <article className="metric-card"><div><small>Movimentos</small><strong>{data.entries.length}</strong><span>{sourceFilter ? `Só ${ENTRY_SOURCE_LABELS[sourceFilter].toLocaleLowerCase('pt-PT')}` : 'Com ligação direta ao lançamento'}</span></div></article>
    </section>

    <div className="filterbar">
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      {(fromDate || toDate) && <button type="button" className="button button-secondary" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>}
      <label><span className="sr-only">Origem</span><select value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value)}><option value="">Todas as origens</option>{ENTRY_SOURCES.map((code) => <option key={code} value={code}>{ENTRY_SOURCE_LABELS[code]}</option>)}</select></label>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={data.entries.length === 0 && !inPeriod}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Movimentos desta conta</h2><p>Clique num movimento ou diário para consultar o contexto.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Movimento</th><th>Diário</th>{data.isAggregate && <th>Conta</th>}<th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>
          {inPeriod && currentPage === 1 && <tr><td colSpan={data.isAggregate ? 6 : 5}>Saldo de abertura</td><td className="numeric strong">{euro.format(data.openingBalance)}</td></tr>}
          {pagedEntries.map((entry, index) => <tr key={`${entry.reference}-${index}`}>
            <td><Link className="primary-cell" to={`/movimentos/${entry.reference}`}>{entry.reference.replaceAll('-', ' ')}</Link><small>{formatMovementDate(entry.entryDate)}</small></td>
            <td><Link className="inline-link" to={`/diarios/${entry.journalCode}`}><Landmark size={14} /> {entry.journalCode}</Link></td>
            {data.isAggregate && <td><Link className="inline-link" to={`/contas/${entry.accountCode}`}>{entry.accountCode}</Link></td>}
            <td>{entry.description}{entry.notes ? <span className="note-flag" title={entry.notes}><StickyNote size={13} /> nota</span> : null}</td>
            <td className="numeric">{entry.debit ? euro.format(entry.debit) : '—'}</td>
            <td className="numeric">{entry.credit ? euro.format(entry.credit) : '—'}</td>
            <td className="numeric strong">{euro.format(entry.runningBalance)}</td>
          </tr>)}
        </tbody>
      </table></div>
      {totalPages > 1 && <div className="pager">
        <button className="button button-secondary" type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}><ChevronLeft size={15} /> Anterior</button>
        <span>Página {currentPage} de {totalPages}</span>
        <button className="button button-secondary" type="button" disabled={currentPage === totalPages} onClick={() => setPage(currentPage + 1)}>Seguinte <ChevronRight size={15} /></button>
      </div>}
      {data.entries.length === 0 && <p className="empty-state">Sem movimentos para esta conta.</p>}
    </section>
  </div>
}
