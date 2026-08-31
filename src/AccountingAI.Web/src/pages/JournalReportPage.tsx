import { BookText, Database, Filter } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type ReportEntry = {
  id: string; date: string; journalId: string; documentType: string | null; document: string
  description: string; status: string; amount: number
  lines: { account: string; accountName: string; description: string; debit: number; credit: number }[]
}
type Report = { totalDebit: number; totalCredit: number; entries: ReportEntry[] }

function fromMovements(movements: Movement[], journal: string, documentType: string, year: string): Report {
  const entries = movements
    .filter((movement) => movement.status !== 'Anulado')
    .filter((movement) => !journal || movement.journalId === journal)
    .filter((movement) => !documentType || movement.documentType === documentType)
    .filter((movement) => !year || movement.date.slice(0, 4) === year)
    .map((movement) => ({
      id: movement.id, date: movement.date, journalId: movement.journalId, documentType: movement.documentType ?? null,
      document: movement.document, description: movement.description, status: movement.status, amount: movement.amount,
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
  const [data, setData] = useState<Report>(() => fromMovements(movements, '', '', ''))

  useEffect(() => {
    if (source !== 'database') {
      setData(fromMovements(movements, journal, documentType, year))
      return
    }
    const params = new URLSearchParams()
    if (journal) params.set('journal', journal)
    if (documentType) params.set('documentType', documentType)
    if (year) params.set('year', year)
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/journal-report?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as Report : Promise.reject(new Error()))
      .then((result) => setData(result))
      .catch(() => { if (!controller.signal.aborted) setData(fromMovements(movements, journal, documentType, year)) })
    return () => controller.abort()
  }, [activeCompany.companyId, documentType, journal, movements, source, user.id, year])

  const balanced = useMemo(() => Math.round((data.totalDebit - data.totalCredit) * 100) === 0, [data.totalDebit, data.totalCredit])

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Livro diário</h1><p>Lançamentos por ordem cronológica, com as linhas e os totais. Sem os anulados.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <div className="filterbar">
      <label><Filter size={16} /><span className="sr-only">Diário</span><select value={journal} onChange={(event) => setJournal(event.target.value)}><option value="">Todos os diários</option>{accountingSetup.journals.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      <label><BookText size={16} /><span className="sr-only">Tipo de documento</span><select value={documentType} onChange={(event) => setDocumentType(event.target.value)}><option value="">Todos os tipos</option>{accountingSetup.documentTypes.map((item) => <option key={item.code} value={item.code}>{item.code} · {item.name}</option>)}</select></label>
      <label><span className="sr-only">Exercício</span><input type="number" min="2000" max="2200" value={year} onChange={(event) => setYear(event.target.value)} placeholder="Exercício" /></label>
      <span className="catalog-count">{data.entries.length} lançamentos</span>
    </div>

    <section className="metric-grid compact-grid">
      <article className="metric-card"><div><small>Total de débito</small><strong>{euro.format(data.totalDebit)}</strong></div></article>
      <article className="metric-card"><div><small>Total de crédito</small><strong>{euro.format(data.totalCredit)}</strong><span>{balanced ? 'Equilibrado' : 'Débito e crédito diferentes'}</span></div></article>
    </section>

    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Lançamento</th><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead><tbody>
      {data.entries.map((entry) => entry.lines.map((line, index) => <tr key={`${entry.id}-${index}`}>
        <td>{index === 0
          ? <><Link className="primary-cell" to={`/movimentos/${entry.id}`}>{entry.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(entry.date)} · {entry.journalId}{entry.documentType ? ` · ${entry.documentType}` : ''}</small></>
          : <span className="muted-value">·</span>}</td>
        <td><Link className="inline-link" to={`/contas/${line.account}`}>{line.account}</Link><small>{line.accountName}</small></td>
        <td>{line.description}{index === 0 && entry.description ? <small>{entry.description}</small> : null}</td>
        <td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td>
        <td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td>
      </tr>))}
    </tbody><tfoot><tr><td colSpan={3}>Totais</td><td className="numeric strong">{euro.format(data.totalDebit)}</td><td className="numeric strong">{euro.format(data.totalCredit)}</td></tr></tfoot></table></div>{data.entries.length === 0 && <p className="empty-state">Sem lançamentos para os filtros escolhidos.</p>}</section>
  </div>
}
