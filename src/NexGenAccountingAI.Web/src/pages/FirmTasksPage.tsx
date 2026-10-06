import { CalendarClock, CircleAlert, CircleCheck, Database, ListTodo, Pencil, Plus, RotateCcw, Search, Trash2, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { signalLoadFailure } from '../app/loadFailure'
import {
  ASSIGNEE_FILTERS, PRIORITY_LABELS, STATUS_FILTERS, STATUS_LABELS, originLabel,
  type FirmTask, type TaskAssignee, type TaskPriority,
} from '../app/taskLabels'
import { companyRoleLabel } from '../app/companyRoles'
import { useWorkContext } from '../app/WorkContext'

type Draft = { id: string | null; companyId: string; title: string; description: string; dueDate: string; priority: TaskPriority; assigneeUserId: string }

const dateFormat = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short' })
const formatDate = (iso: string | null) => (iso ? dateFormat.format(new Date(`${iso}T00:00:00`)) : '—')

async function refusal(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback
}

/**
 * As tarefas do gabinete (F4E-01): o que há a fazer em cada empresa da carteira e por quem.
 * Cada tarefa é de uma empresa; quem só tem acesso de consulta vê-as e não as altera.
 */
export function FirmTasksPage() {
  const { activeCompany, companies, source, user } = useWorkContext()
  const root = `/api/v1/development/users/${user.id}`
  const [tasks, setTasks] = useState<FirmTask[] | null>(null)
  const [companyFilter, setCompanyFilter] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState<'all' | 'me' | 'none'>('all')
  const [statusFilter, setStatusFilter] = useState<'open' | 'closed' | 'all'>('open')
  const [search, setSearch] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [assignees, setAssignees] = useState<TaskAssignee[]>([])
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [reload, setReload] = useState(0)

  const canWrite = (companyId: string) => companies.find((company) => company.companyId === companyId)?.role !== 'Viewer'
  const writable = companies.filter((company) => company.role !== 'Viewer')

  const load = useCallback(() => {
    if (source !== 'database') return () => undefined
    const controller = new AbortController()
    const params = new URLSearchParams({ status: statusFilter })
    if (companyFilter) params.set('company', companyFilter)
    if (assigneeFilter !== 'all') params.set('assignee', assigneeFilter)
    if (search.trim()) params.set('search', search.trim())
    void fetch(`${root}/tasks?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as FirmTask[] : Promise.reject(new Error()))
      .then((list) => { if (!controller.signal.aborted) setTasks(list) })
      .catch(() => { if (!controller.signal.aborted) { signalLoadFailure(); setError('Não foi possível ler as tarefas.') } })
    return () => controller.abort()
  }, [root, source, statusFilter, companyFilter, assigneeFilter, search])
  useEffect(() => load(), [load, reload])

  // Os membros a quem se pode atribuir, da empresa do formulário.
  useEffect(() => {
    if (!draft?.companyId) { setAssignees([]); return }
    const controller = new AbortController()
    void fetch(`${root}/companies/${draft.companyId}/tasks/assignees`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as TaskAssignee[] : [])
      .then((list) => { if (!controller.signal.aborted) setAssignees(list) })
      .catch(() => { /* a lista de responsáveis é um complemento */ })
    return () => controller.abort()
  }, [root, draft?.companyId])

  const overdue = useMemo(() => (tasks ?? []).filter((task) => task.overdue).length, [tasks])

  function newDraft(): Draft {
    const first = writable.find((company) => company.companyId === (companyFilter || activeCompany.companyId)) ?? writable[0]
    return { id: null, companyId: first?.companyId ?? '', title: '', description: '', dueDate: '', priority: 'normal', assigneeUserId: '' }
  }

  async function save() {
    if (!draft) return
    setError(''); setNotice('')
    const body = {
      title: draft.title, description: draft.description || null, dueDate: draft.dueDate || null,
      priority: draft.priority, assigneeUserId: draft.assigneeUserId || null,
    }
    const response = draft.id
      ? await fetch(`${root}/companies/${draft.companyId}/tasks/${draft.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      : await fetch(`${root}/companies/${draft.companyId}/tasks`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível guardar a tarefa.')); return }
    setDraft(null); setNotice('Tarefa guardada.'); setReload((value) => value + 1)
  }

  async function setStatus(task: FirmTask, status: 'open' | 'done') {
    setError(''); setNotice('')
    const response = await fetch(`${root}/companies/${task.companyId}/tasks/${task.id}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: task.title, description: task.description, dueDate: task.dueDate, priority: task.priority, assigneeUserId: task.assigneeUserId, status }),
    })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível mudar o estado.')); return }
    setReload((value) => value + 1)
  }

  async function remove(task: FirmTask) {
    if (!window.confirm(`Apagar a tarefa «${task.title}»?`)) return
    const response = await fetch(`${root}/companies/${task.companyId}/tasks/${task.id}`, { method: 'DELETE' })
    if (!response.ok) { setError('Só o autor, o dono ou um administrador apaga a tarefa.'); return }
    setReload((value) => value + 1)
  }

  async function fromObligations(companyId: string) {
    setError(''); setNotice('')
    const response = await fetch(`${root}/companies/${companyId}/tasks/from-obligations`, { method: 'POST' })
    if (!response.ok) { setError(await refusal(response, 'Não foi possível criar as tarefas.')); return }
    const result = await response.json() as { created: number; alreadyExisted: number }
    setNotice(result.created === 0 ? 'Não há obrigações novas em atraso nem a vencer.' : `${result.created} tarefa(s) criada(s) das obrigações${result.alreadyExisted ? ` (${result.alreadyExisted} já existiam)` : ''}.`)
    setReload((value) => value + 1)
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gabinete</p><h1>Tarefas do gabinete</h1><p>As tarefas precisam da base de dados; em demonstração não estão disponíveis.</p></div></header></div>
  }

  const target = companyFilter || activeCompany.companyId
  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gabinete</p><h1>Tarefas do gabinete</h1><p>O que há a fazer em cada empresa e por quem. Podem nascer à mão ou das <Link to="/obrigacoes">obrigações</Link> em atraso ou a vencer.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {notice && <p className="save-status success" role="status"><CircleCheck size={15} /> {notice}</p>}

    <section className="panel">
      <div className="panel-heading">
        <h2><ListTodo size={17} /> {tasks ? `${tasks.length} tarefa(s)` : 'A ler…'}{overdue > 0 && <span className="badge warning" style={{ marginLeft: 8 }}>{overdue} em atraso</span>}</h2>
        <button type="button" className="button button-primary" disabled={writable.length === 0} onClick={() => setDraft(newDraft())}><Plus size={15} /> Nova tarefa</button>
      </div>
      <div className="filterbar">
        <label className="inline-check">Empresa
          <select aria-label="Empresa" value={companyFilter} onChange={(event) => setCompanyFilter(event.target.value)}>
            <option value="">Todas</option>
            {companies.map((company) => <option key={company.companyId} value={company.companyId}>{company.legalName}</option>)}
          </select>
        </label>
        <label className="inline-check">Responsável
          <select aria-label="Responsável" value={assigneeFilter} onChange={(event) => setAssigneeFilter(event.target.value as 'all' | 'me' | 'none')}>
            {ASSIGNEE_FILTERS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
          </select>
        </label>
        <label className="inline-check">Estado
          <select aria-label="Estado" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as 'open' | 'closed' | 'all')}>
            {STATUS_FILTERS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
          </select>
        </label>
        <label className="inline-check"><Search size={14} /> <input aria-label="Procurar tarefas" placeholder="Procurar" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
        {canWrite(target) && <button type="button" className="button button-secondary" onClick={() => void fromObligations(target)}><CalendarClock size={15} /> Criar das obrigações</button>}
      </div>

      {tasks && tasks.length === 0 && <p className="empty-state">Não há tarefas com estes filtros.</p>}
      {tasks && tasks.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th><span className="sr-only">Concluir</span></th><th>Tarefa</th><th>Empresa</th><th>Prazo</th><th>Prioridade</th><th>Responsável</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{tasks.map((task) => {
          const closed = task.status === 'done' || task.status === 'cancelled'
          const editable = canWrite(task.companyId)
          return <tr key={task.id} className={closed ? 'muted-row' : ''}>
            <td>
              {editable && (closed
                ? <button type="button" className="icon-button" aria-label={`Reabrir ${task.title}`} onClick={() => void setStatus(task, 'open')}><RotateCcw size={15} /></button>
                : <input type="checkbox" aria-label={`Concluir ${task.title}`} checked={false} onChange={() => void setStatus(task, 'done')} />)}
            </td>
            <td>
              <strong style={closed ? { textDecoration: 'line-through' } : undefined}>{task.title}</strong>
              {originLabel(task.origin) && <span className="badge" style={{ marginLeft: 6 }}>{originLabel(task.origin)}</span>}
              {task.status === 'inprogress' && <span className="badge" style={{ marginLeft: 6 }}>{STATUS_LABELS.inprogress}</span>}
              {task.status === 'cancelled' && <span className="badge" style={{ marginLeft: 6 }}>{STATUS_LABELS.cancelled}</span>}
              {task.description && <div><small>{task.description}</small></div>}
              {closed && task.completedByName && <div><small>{STATUS_LABELS[task.status]} por {task.completedByName}</small></div>}
            </td>
            <td>{task.companyName}</td>
            <td>{task.overdue ? <span className="badge warning">{formatDate(task.dueDate)} · em atraso</span> : formatDate(task.dueDate)}</td>
            <td>{PRIORITY_LABELS[task.priority]}</td>
            <td>{task.assigneeName ?? '—'}{task.mine ? ' (eu)' : ''}</td>
            <td className="numeric">
              {editable && <>
                <button type="button" className="icon-button" aria-label={`Editar ${task.title}`} onClick={() => setDraft({ id: task.id, companyId: task.companyId, title: task.title, description: task.description ?? '', dueDate: task.dueDate ?? '', priority: task.priority, assigneeUserId: task.assigneeUserId ?? '' })}><Pencil size={15} /></button>
                <button type="button" className="icon-button" aria-label={`Apagar ${task.title}`} onClick={() => void remove(task)}><Trash2 size={15} /></button>
              </>}
            </td>
          </tr>
        })}</tbody>
      </table></div>}
      {companies.some((company) => company.role === 'Viewer') && <p className="statement-note">Nas empresas em que tem a função «{companyRoleLabel('Viewer')}» vê as tarefas, mas não as altera.</p>}
    </section>

    {draft && <section className="panel" aria-label={draft.id ? 'Editar tarefa' : 'Nova tarefa'}>
      <div className="panel-heading"><h2>{draft.id ? 'Editar tarefa' : 'Nova tarefa'}</h2><button type="button" className="icon-button" aria-label="Fechar" onClick={() => setDraft(null)}><X size={15} /></button></div>
      <div className="filterbar">
        <label className="inline-check">Empresa
          <select aria-label="Empresa da tarefa" value={draft.companyId} disabled={!!draft.id} onChange={(event) => setDraft({ ...draft, companyId: event.target.value, assigneeUserId: '' })}>
            {writable.map((company) => <option key={company.companyId} value={company.companyId}>{company.legalName}</option>)}
          </select>
        </label>
        <label className="inline-check">Título <input aria-label="Título da tarefa" value={draft.title} maxLength={200} style={{ minWidth: 300 }} onChange={(event) => setDraft({ ...draft, title: event.target.value })} /></label>
        <label className="inline-check">Prazo <input type="date" aria-label="Prazo da tarefa" value={draft.dueDate} onChange={(event) => setDraft({ ...draft, dueDate: event.target.value })} /></label>
        <label className="inline-check">Prioridade
          <select aria-label="Prioridade da tarefa" value={draft.priority} onChange={(event) => setDraft({ ...draft, priority: event.target.value as TaskPriority })}>
            {(Object.keys(PRIORITY_LABELS) as TaskPriority[]).map((key) => <option key={key} value={key}>{PRIORITY_LABELS[key]}</option>)}
          </select>
        </label>
        <label className="inline-check">Responsável
          <select aria-label="Responsável da tarefa" value={draft.assigneeUserId} onChange={(event) => setDraft({ ...draft, assigneeUserId: event.target.value })}>
            <option value="">(ninguém)</option>
            {assignees.map((item) => <option key={item.userId} value={item.userId}>{item.displayName}{item.isResponsible ? ' · responsável pela empresa' : ''}</option>)}
          </select>
        </label>
      </div>
      <label className="inline-check" style={{ display: 'block' }}>Descrição <textarea aria-label="Descrição da tarefa" rows={3} maxLength={2000} style={{ width: '100%' }} value={draft.description} onChange={(event) => setDraft({ ...draft, description: event.target.value })} /></label>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <button type="button" className="button button-primary" disabled={!draft.title.trim() || !draft.companyId} onClick={() => void save()}>Guardar</button>
        <button type="button" className="button button-secondary" onClick={() => setDraft(null)}>Cancelar</button>
      </div>
    </section>}
  </div>
}
