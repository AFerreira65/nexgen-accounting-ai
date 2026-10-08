import { CalendarClock, Check, CircleAlert, Database, Undo2 } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatMovementDate } from '../app/demo-data'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type ConsoleRow = {
  companyId: string
  companyName: string
  taxRegistrationNumber: string
  canEdit: boolean
  obligation: { code: string; name: string; periodKey: string; periodLabel: string; dueDate: string; legalDueDate: string; state: string; deliveredOn: string | null; note: string | null }
}
type ConsoleData = { from: string; to: string; companyCount: number; rows: ConsoleRow[] }

const iso = (date: Date) => date.toISOString().slice(0, 10)
const keyOf = (row: ConsoleRow) => `${row.companyId}|${row.obligation.code}|${row.obligation.periodKey}`

/**
 * A consola de obrigações em lote (F4E-02): as obrigações de todas as empresas do gabinete a que o
 * utilizador tem acesso, num só ecrã, para ver o que está em atraso e dar várias entregas de uma vez.
 * Cada empresa só aparece se o grupo de permissões do utilizador a deixa consultar (e só se altera com
 * a permissão de registar entregas); quem garante é o servidor.
 */
export function ObligationsConsolePage() {
  const { source, user } = useWorkContext()
  const onDatabase = source === 'database'
  const [from, setFrom] = useState(() => { const date = new Date(); date.setDate(1); return iso(date) })
  const [to, setTo] = useState(() => { const date = new Date(); date.setMonth(date.getMonth() + 2); return iso(date) })
  const [mineOnly, setMineOnly] = useState(false)
  const [state, setState] = useState<'Todas' | 'Pendente' | 'Entregue' | 'Dispensada' | 'Atraso'>('Pendente')
  const [kind, setKind] = useState('')
  const [data, setData] = useState<ConsoleData | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [deliveredOn, setDeliveredOn] = useState(() => iso(new Date()))
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  useEffect(() => {
    if (!onDatabase || !from || !to) return
    const controller = new AbortController()
    setError('')
    const params = new URLSearchParams({ from, to })
    if (mineOnly) params.set('responsible', user.id)
    void fetch(`/api/v1/development/users/${user.id}/obligations-console?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as ConsoleData : Promise.reject(new Error(await problemMessage(response))))
      .then((value) => { if (!controller.signal.aborted) { setData(value); setSelected(new Set()) } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setData(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar a consola.') } })
    return () => controller.abort()
  }, [from, to, mineOnly, onDatabase, reload, user.id])

  const today = iso(new Date())
  const kinds = useMemo(() => [...new Map((data?.rows ?? []).map((row) => [row.obligation.code, row.obligation.name])).entries()].sort((a, b) => a[1].localeCompare(b[1], 'pt-PT')), [data])
  const rows = useMemo(() => (data?.rows ?? []).filter((row) => {
    if (kind && row.obligation.code !== kind) return false
    if (state === 'Todas') return true
    if (state === 'Atraso') return row.obligation.state === 'Pendente' && row.obligation.dueDate < today
    return row.obligation.state === state
  }), [data, kind, state, today])
  const editable = rows.filter((row) => row.canEdit)
  const overdue = (data?.rows ?? []).filter((row) => row.obligation.state === 'Pendente' && row.obligation.dueDate < today).length

  const toggle = useCallback((key: string) => setSelected((current) => { const next = new Set(current); if (next.has(key)) next.delete(key); else next.add(key); return next }), [])
  const allSelected = editable.length > 0 && editable.every((row) => selected.has(keyOf(row)))

  async function apply(newState: 'Entregue' | 'Pendente' | 'Dispensada') {
    const items = rows.filter((row) => selected.has(keyOf(row))).map((row) => ({ companyId: row.companyId, code: row.obligation.code, periodKey: row.obligation.periodKey }))
    if (items.length === 0) return
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/obligations-console/update`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ items, state: newState, deliveredOn: newState === 'Entregue' ? deliveredOn : null }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      const result = await response.json() as { updated: number }
      setMessage(`${result.updated} ${result.updated === 1 ? 'obrigação atualizada' : 'obrigações atualizadas'}.`)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar.')
    } finally {
      setBusy(false)
    }
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Consola de obrigações</h1><p>As obrigações de todas as empresas do gabinete num só ecrã.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><CalendarClock /></span><h2>Requer ligação à base de dados</h2><p>A consola lê o calendário de cada empresa a que tem acesso.</p></section>
    </div>
  }

  const badge = (row: ConsoleRow) => row.obligation.state === 'Entregue' ? 'success' : row.obligation.state === 'Dispensada' ? 'neutral' : row.obligation.dueDate < today ? 'warning' : 'neutral'

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Consola de obrigações</h1><p>As obrigações de todas as empresas a que tem acesso, ordenadas por prazo. Marque várias e registe a entrega de uma vez; as datas e o detalhe de cada período vêm do calendário de cada empresa.</p></div></header>

    <div className="filterbar no-print">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={to} onChange={(event) => setTo(event.target.value)} /></label>
      <label className="inline-check">Estado <select aria-label="Estado" value={state} onChange={(event) => setState(event.target.value as typeof state)}>
        <option value="Pendente">Pendentes</option><option value="Atraso">Em atraso</option><option value="Entregue">Entregues</option><option value="Dispensada">Dispensadas</option><option value="Todas">Todas</option></select></label>
      <label className="inline-check">Obrigação <select aria-label="Obrigação" value={kind} onChange={(event) => setKind(event.target.value)}><option value="">Todas</option>{kinds.map(([code, name]) => <option key={code} value={code}>{name}</option>)}</select></label>
      <label className="inline-check"><input type="checkbox" checked={mineOnly} onChange={(event) => setMineOnly(event.target.checked)} /> Só as empresas de que sou responsável</label>
    </div>

    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}

    {data && <section className="metadata-strip">
      <span><small>Empresas</small><strong>{data.companyCount}</strong></span>
      <span><small>Obrigações no período</small><strong>{data.rows.length}</strong></span>
      <span><small>Em atraso</small><strong>{overdue > 0 ? <span className="badge warning">{overdue}</span> : 0}</strong></span>
    </section>}

    {data && rows.length === 0 && <section className="panel empty-state"><h2>Nada a mostrar</h2><p>Não há obrigações com estes filtros neste período.</p></section>}

    {rows.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>{rows.length} {rows.length === 1 ? 'obrigação' : 'obrigações'}</h2><p>{selected.size} {selected.size === 1 ? 'escolhida' : 'escolhidas'}.</p></div>
        {editable.length > 0 && <div className="toolbar-actions no-print">
          <label className="inline-check">Entregue em <input type="date" aria-label="Data de entrega" value={deliveredOn} onChange={(event) => setDeliveredOn(event.target.value)} /></label>
          <button className="button button-primary" type="button" disabled={busy || selected.size === 0} onClick={() => void apply('Entregue')}><Check size={16} /> Marcar como entregues</button>
          <button className="button button-secondary" type="button" disabled={busy || selected.size === 0} onClick={() => void apply('Dispensada')}>Dispensar</button>
          <button className="button button-secondary" type="button" disabled={busy || selected.size === 0} onClick={() => void apply('Pendente')}><Undo2 size={16} /> Voltar a pendentes</button>
        </div>}
      </div>
      <div className="table-wrap"><table>
        <thead><tr>
          <th>{editable.length > 0 && <input type="checkbox" aria-label="Escolher todas" checked={allSelected} onChange={(event) => setSelected(event.target.checked ? new Set(editable.map(keyOf)) : new Set())} />}</th>
          <th>Prazo</th><th>Empresa</th><th>Obrigação</th><th>Período</th><th>Estado</th></tr></thead>
        <tbody>{rows.map((row) => <tr key={keyOf(row)}>
          <td>{row.canEdit && <input type="checkbox" aria-label={`Escolher ${row.obligation.name} de ${row.companyName}`} checked={selected.has(keyOf(row))} onChange={() => toggle(keyOf(row))} />}</td>
          <td>{formatMovementDate(row.obligation.dueDate)}{row.obligation.dueDate !== row.obligation.legalDueDate && <small>legal {formatMovementDate(row.obligation.legalDueDate)}</small>}</td>
          <td>{row.companyName}<small>{row.taxRegistrationNumber}</small></td>
          <td>{row.obligation.name}</td>
          <td>{row.obligation.periodLabel}</td>
          <td><span className={`badge ${badge(row)}`}>{row.obligation.state === 'Pendente' && row.obligation.dueDate < today ? 'Em atraso' : row.obligation.state}</span>{row.obligation.deliveredOn && <small>em {formatMovementDate(row.obligation.deliveredOn)}</small>}</td>
        </tr>)}</tbody>
      </table></div>
      <p className="mr-hint">Para ajustar o prazo de um período ou juntar uma nota, abra o <Link to="/obrigacoes">calendário</Link> da empresa.</p>
    </section>}
  </div>
}
