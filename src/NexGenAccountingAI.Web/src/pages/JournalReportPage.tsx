import { BookText, ChevronLeft, ChevronRight, Database, Download, Filter } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { ENTRY_SOURCE_LABELS, ENTRY_SOURCES, entrySourceLabel } from '../app/entrySources'
import { paginate } from '../app/pagination'
import { PeriodShortcuts } from '../app/periods'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

type ReportEntry = {
  id: string; date: string; journalId: string; documentType: string | null; document: string
  description: string; status: string; source?: string; amount: number
  lines: { account: string; accountName: string; description: string; debit: number; credit: number }[]
}
type Report = { totalDebit: number; totalCredit: number; entries: ReportEntry[] }

function fromMovements(movements: Movement[], journal: string, documentType: string, year: string, from: string, to: string, account: string, source: string): Report {
  const normalizedAccount = account.trim().toUpperCase()
  const entries = movements
    .filter((movement) => movement.status !== 'Anulado')
    .filter((movement) => !journal || movement.journalId === journal)
    .filter((movement) => !documentType || movement.documentType === documentType)
    .filter((movement) => !year || movement.date.slice(0, 4) === year)
    .filter((movement) => !from || movement.date >= from)
    .filter((movement) => !to || movement.date <= to)
    .filter((movement) => !normalizedAccount || movement.lines.some((line) => line.account.toUpperCase() === normalizedAccount))
    .filter((movement) => !source || (movement.source ?? 'Manual') === source)
    .map((movement) => ({
      id: movement.id, date: movement.date, journalId: movement.journalId, documentType: movement.documentType ?? null,
      document: movement.document, description: movement.description, status: movement.status, source: movement.source ?? 'Manual', amount: movement.amount,
      lines: movement.lines,
    }))
    .sort((a, b) => (a.date + a.id).localeCompare(b.date + b.id))
  const totalDebit = entries.reduce((sum, entry) => sum + entry.lines.reduce((s, line) => s + line.debit, 0), 0)
  const totalCredit = entries.reduce((sum, entry) => sum + entry.lines.reduce((s, line) => s + line.credit, 0), 0)
  return { entries, totalDebit, totalCredit }
}

export function JournalReportPage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [journal, setJournal] = useState('')
  const [documentType, setDocumentType] = useState('')
  const [year, setYear] = useState(String(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear()))
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [account, setAccount] = useState('')
  const [sourceFilter, setSourceFilter] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Report>(() => fromMovements(movements, '', '', '', '', '', '', ''))

  // O exercício arranca no da empresa; volta a acertar ao trocar de empresa
  // ou quando o exercício predefinido muda (ex.: acabou de se importar um
  // SAF-T de outro ano) — sem isto, o filtro ficava preso ao valor do
  // primeiro desenho, muitas vezes o ano do sistema.
  useEffect(() => setYear(String(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear())),
    [activeCompany.companyId, activeCompany.settings.defaultFiscalYear])

  useEffect(() => {
    if (source !== 'database') {
      setData(fromMovements(movements, journal, documentType, year, fromDate, toDate, account, sourceFilter))
      return
    }
    const params = new URLSearchParams()
    if (journal) params.set('journal', journal)
    if (documentType) params.set('documentType', documentType)
    if (year) params.set('year', year)
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    if (account.trim()) params.set('account', account.trim())
    if (sourceFilter) params.set('source', sourceFilter)
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/journal-report?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as Report : Promise.reject(new Error()))
      .then((result) => setData(result))
      .catch(() => { if (!controller.signal.aborted) setData(fromMovements(movements, journal, documentType, year, fromDate, toDate, account, sourceFilter)) })
    return () => controller.abort()
  }, [account, activeCompany.companyId, documentType, fromDate, journal, movements, source, sourceFilter, toDate, user.id, year])

  // As sugestões da conta: só as que começam pelo que está escrito, no máximo 100.
  // Um plano importado pode ter dezenas de milhares de contas (uma por cliente), e
  // pô-las todas na lista bloqueava o ecrã mais de 10 segundos.
  const accountSuggestions = useMemo(() => {
    const typed = account.trim().toUpperCase()
    return accountingSetup.accounts.filter((item) => item.allowsPosting && item.code.toUpperCase().startsWith(typed)).slice(0, 100)
  }, [account, accountingSetup.accounts])
  const balanced = useMemo(() => Math.round((data.totalDebit - data.totalCredit) * 100) === 0, [data.totalDebit, data.totalCredit])

  // Uma empresa grande (SAF-T com anos de histórico) tem lançamentos a mais
  // para um só desenho da tabela; pagina-se por lançamento — as linhas de um
  // lançamento nunca se partem entre páginas — pelo tamanho de página da
  // empresa, voltando à primeira página quando um filtro muda. Os totais no
  // rodapé e a contagem continuam a ser do conjunto filtrado inteiro.
  const { items: pagedEntries, currentPage, totalPages } = paginate(data.entries, activeCompany.settings.pageSize, page)
  useEffect(() => setPage(1), [journal, documentType, year, fromDate, toDate, account, sourceFilter])

  const exportCsv = () => {
    const header = ['Referência', 'Data', 'Diário', 'Tipo de documento', 'Documento', 'Origem', 'Descrição', 'Estado', 'Conta', 'Nome da conta', 'Descrição da linha', 'Débito', 'Crédito']
    const lines = data.entries.flatMap((entry) => entry.lines.map((line) => [
      entry.id, entry.date, entry.journalId, entry.documentType ?? '', entry.document, entrySourceLabel(entry.source), entry.description, entry.status,
      line.account, line.accountName, line.description, line.debit, line.credit,
    ]))
    const totals = ['', '', '', '', '', '', '', '', '', '', 'Totais', data.totalDebit, data.totalCredit]
    downloadCsv(csvFilename('livro-diario', activeCompany.legalName), toCsv([header, ...lines, totals]))
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Livro diário</h1><p>Lançamentos por ordem cronológica, com as linhas e os totais. Sem os anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <ReportChrome reportKey="livro-diario" title="Livro diário" from={fromDate} to={toDate} />

    <div className="filterbar">
      <label><Filter size={16} /><span className="sr-only">Diário</span><select value={journal} onChange={(event) => setJournal(event.target.value)}><option value="">Todos os diários</option>{accountingSetup.journals.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      <label><BookText size={16} /><span className="sr-only">Tipo de documento</span><select value={documentType} onChange={(event) => setDocumentType(event.target.value)}><option value="">Todos os tipos</option>{accountingSetup.documentTypes.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      <label><span className="sr-only">Exercício</span><input type="number" min="2000" max="2200" value={year} onChange={(event) => setYear(event.target.value)} placeholder="Exercício" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label><span className="sr-only">Conta</span><input aria-label="Conta" list="journal-report-accounts" value={account} onChange={(event) => setAccount(event.target.value)} placeholder="Conta" /></label>
      <datalist id="journal-report-accounts">{accountSuggestions.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</datalist>
      <label><span className="sr-only">Origem</span><select value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value)}><option value="">Todas as origens</option>{ENTRY_SOURCES.map((code) => <option key={code} value={code}>{ENTRY_SOURCE_LABELS[code]}</option>)}</select></label>
      <span className="catalog-count">{data.entries.length} lançamentos</span>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={data.entries.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="metric-grid compact-grid">
      <article className="metric-card"><div><small>Total de débito</small><strong>{euro.format(data.totalDebit)}</strong></div></article>
      <article className="metric-card"><div><small>Total de crédito</small><strong>{euro.format(data.totalCredit)}</strong><span>{balanced ? 'Equilibrado' : 'Débito e crédito diferentes'}</span></div></article>
    </section>

    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Lançamento</th><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead><tbody>
      {pagedEntries.map((entry) => entry.lines.map((line, index) => <tr key={`${entry.id}-${index}`}>
        <td>{index === 0
          ? <><Link className="primary-cell" to={`/movimentos/${entry.id}`}>{entry.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(entry.date)} · {entry.journalId}{entry.documentType ? ` · ${entry.documentType}` : ''}</small></>
          : <span className="muted-value">·</span>}</td>
        <td><Link className="inline-link" to={`/contas/${line.account}`}>{line.account}</Link><small>{line.accountName}</small></td>
        <td>{line.description}{index === 0 && entry.description ? <small>{entry.description}</small> : null}</td>
        <td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td>
        <td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td>
      </tr>))}
    </tbody><tfoot><tr><td colSpan={3}>Totais</td><td className="numeric strong">{euro.format(data.totalDebit)}</td><td className="numeric strong">{euro.format(data.totalCredit)}</td></tr></tfoot></table></div>
    {totalPages > 1 && <div className="pager">
      <button className="button button-secondary" type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}><ChevronLeft size={15} /> Anterior</button>
      <span>Página {currentPage} de {totalPages}</span>
      <button className="button button-secondary" type="button" disabled={currentPage === totalPages} onClick={() => setPage(currentPage + 1)}>Seguinte <ChevronRight size={15} /></button>
    </div>}
    {data.entries.length === 0 && <p className="empty-state">Sem lançamentos para os filtros escolhidos.</p>}</section>
  </div>
}
