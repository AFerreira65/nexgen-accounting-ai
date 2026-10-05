import { CalendarClock, Check, CircleAlert, Database, Printer, Undo2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatMovementDate, type ObligationCalendar, type ObligationRow } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const iso = (date: Date) => date.toISOString().slice(0, 10)

export function ObligationsPage() {
  const { activeCompany, source, obligations, updateObligation } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const [from, setFrom] = useState(() => { const date = new Date(); date.setDate(1); return iso(date) })
  const [to, setTo] = useState(() => { const date = new Date(); date.setMonth(date.getMonth() + 3); return iso(date) })
  const [calendar, setCalendar] = useState<ObligationCalendar | null>(null)
  const [editing, setEditing] = useState<{ row: ObligationRow; deliveredOn: string; note: string; dueDate: string } | null>(null)
  const [reload, setReload] = useState(0)
  const [includeAll, setIncludeAll] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !from || !to) { setCalendar(null); return }
    const controller = new AbortController()
    setError('')
    void obligations(companyId, { from, to }, includeAll)
      .then((value) => { if (!controller.signal.aborted) setCalendar(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setCalendar(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o calendário.') } })
    return () => controller.abort()
  }, [companyId, from, includeAll, obligations, onDatabase, reload, to])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Calendário de obrigações</h1><p>As declarações e os pagamentos da empresa, com o prazo legal e o estado de cada período.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><CalendarClock /></span><h2>Requer ligação à base de dados</h2><p>O calendário decide pelos lançamentos da empresa que obrigações se aplicam.</p></section>
    </div>
  }

  async function save(row: ObligationRow, state: string, deliveredOn: string | null, note: string | null, dueDate: string | null) {
    setBusy(true)
    setError('')
    try {
      await updateObligation(companyId, row.code, row.periodKey, { state, deliveredOn, note, dueDateOverride: dueDate && dueDate !== row.legalDueDate ? dueDate : null })
      setEditing(null)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar.')
    } finally {
      setBusy(false)
    }
  }

  const today = iso(new Date())
  const rows = calendar?.rows ?? []
  const overdue = rows.filter((row) => row.state === 'Pendente' && row.dueDate < today)
  const badge = (row: ObligationRow) => row.state === 'Entregue' ? 'success' : row.state === 'Dispensada' ? 'neutral' : row.dueDate < today ? 'warning' : 'neutral'

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Calendário de obrigações</h1><p>As declarações e os pagamentos da empresa no período, com o prazo legal — e a fonte de cada um — e o que foi entregue. A aplicação decide pelos lançamentos que obrigações se aplicam; as datas são as legais e alteram-se em cada período quando há prorrogação.</p></div>
      <div className="heading-actions no-print"><button className="button button-secondary" type="button" onClick={() => window.print()} disabled={rows.length === 0}><Printer size={16} /> Imprimir / PDF</button></div>
    </header>

    <div className="filterbar no-print">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={to} onChange={(event) => setTo(event.target.value)} /></label>
      <label className="inline-check"><input type="checkbox" checked={includeAll} onChange={(event) => setIncludeAll(event.target.checked)} /> Mostrar também as que os lançamentos não indicam</label>
    </div>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {calendar && !calendar.vatPeriodicityConfigured && <p className="save-status"><CircleAlert size={15} /> A periodicidade do IVA não está indicada: o calendário assume o regime mensal. Indique-a em <Link to="/comunicacao-at">Comunicação com a AT</Link>.</p>}

    {calendar && <section className="metadata-strip">
      <span><small>Obrigações no período</small><strong>{rows.length}</strong></span>
      <span><small>Entregues</small><strong>{rows.filter((row) => row.state === 'Entregue').length}</strong></span>
      <span><small>Em atraso</small><strong>{overdue.length > 0 ? <span className="badge warning">{overdue.length}</span> : 0}</strong></span>
      <span><small>IVA</small><strong>{calendar.vatPeriodicity}</strong></span>
    </section>}

    {calendar && rows.length === 0 && <section className="panel empty-state"><h2>Sem obrigações no período</h2><p>Alargue as datas.</p></section>}

    {rows.length > 0 && <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Prazo</th><th>Obrigação</th><th>Período</th><th>Estado</th><th className="no-print" /></tr></thead>
        <tbody>{rows.map((row) => <tr key={`${row.code}-${row.periodKey}`}>
          <td>{formatMovementDate(row.dueDate)}{row.dueDateChanged && <small>legal {formatMovementDate(row.legalDueDate)}</small>}</td>
          <td>{row.name}<small title={row.source}>{row.reason} · {row.source}</small></td>
          <td>{row.periodLabel}</td>
          <td><span className={`badge ${badge(row)}`}>{row.state === 'Pendente' && row.dueDate < today ? 'Em atraso' : row.state}</span>{row.deliveredOn && <small>em {formatMovementDate(row.deliveredOn)}</small>}{row.note && <small>{row.note}</small>}</td>
          <td className="no-print">{editing?.row === row
            ? <form className="inline-check" onSubmit={(event) => { event.preventDefault(); void save(row, 'Entregue', editing.deliveredOn, editing.note.trim() || null, editing.dueDate) }}>
              <input type="date" aria-label="Data de entrega" value={editing.deliveredOn} onChange={(event) => setEditing({ ...editing, deliveredOn: event.target.value })} />
              <input aria-label="Nota" value={editing.note} onChange={(event) => setEditing({ ...editing, note: event.target.value })} placeholder="comprovativo, referência" />
              <label className="inline-check">Prazo <input type="date" aria-label="Prazo alterado" value={editing.dueDate} onChange={(event) => setEditing({ ...editing, dueDate: event.target.value })} /></label>
              <button className="button button-primary" type="submit" disabled={busy || !editing.deliveredOn}><Check size={15} /> Entregue</button>
              <button className="button button-secondary" type="button" disabled={busy} onClick={() => void save(row, row.state, row.deliveredOn, editing.note.trim() || null, editing.dueDate)}>Só guardar o prazo e a nota</button>
              <button className="text-link" type="button" onClick={() => setEditing(null)}>Cancelar</button>
            </form>
            : <span className="inline-check">
              {row.state === 'Pendente' && <button className="button button-secondary" type="button" disabled={busy} onClick={() => setEditing({ row, deliveredOn: today, note: row.note ?? '', dueDate: row.dueDate })}>Registar</button>}
              {row.state === 'Pendente' && <button className="text-link" type="button" disabled={busy} onClick={() => void save(row, 'Dispensada', null, row.note, row.dueDateChanged ? row.dueDate : null)}>Dispensar</button>}
              {row.state !== 'Pendente' && <button className="icon-button" type="button" aria-label={`Voltar a pendente: ${row.name}`} title="Voltar a pendente" disabled={busy} onClick={() => void save(row, 'Pendente', null, row.note, row.dueDateChanged ? row.dueDate : null)}><Undo2 size={15} /></button>}
            </span>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {calendar && <p className="statement-note">Cada prazo vem do texto dos códigos publicado no Portal das Finanças (a fonte está em cada linha). A aplicação mostra o IVA pela periodicidade indicada; a recapitulativa quando há operações intracomunitárias; a declaração mensal de remunerações e a entrega das retenções quando há gastos com pessoal (63) ou retenções (242) no mês; a comunicação de faturas quando há vendas no mês; o Modelo 22, a IES, o dossier fiscal e os pagamentos por conta sempre. O Imposto do Selo regista-se à mão, com «Mostrar também as que os lançamentos não indicam». {calendar.notIncluded}</p>}
  </div>
}
