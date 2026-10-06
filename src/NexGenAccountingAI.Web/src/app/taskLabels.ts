/** Os tipos e os rótulos das tarefas do gabinete (F4E-01): espelham a API; os códigos nunca se mostram. */

export type TaskPriority = 'low' | 'normal' | 'high'
export type TaskStatus = 'open' | 'inprogress' | 'done' | 'cancelled'

export type FirmTask = {
  id: string
  companyId: string
  companyName: string
  title: string
  description: string | null
  dueDate: string | null
  priority: TaskPriority
  status: TaskStatus
  assigneeUserId: string | null
  assigneeName: string | null
  createdByName: string
  createdAtUtc: string
  completedAtUtc: string | null
  completedByName: string | null
  origin: string | null
  overdue: boolean
  mine: boolean
}

export type TaskAssignee = { userId: string; displayName: string; isResponsible: boolean }

export const PRIORITY_LABELS: Record<TaskPriority, string> = { low: 'Baixa', normal: 'Normal', high: 'Alta' }

export const STATUS_LABELS: Record<TaskStatus, string> = {
  open: 'Por fazer',
  inprogress: 'Em curso',
  done: 'Concluída',
  cancelled: 'Cancelada',
}

/** O filtro de estado do ecrã: o que se pede à API. */
export const STATUS_FILTERS: { value: 'open' | 'closed' | 'all'; label: string }[] = [
  { value: 'open', label: 'Por fazer e em curso' },
  { value: 'closed', label: 'Concluídas e canceladas' },
  { value: 'all', label: 'Todas' },
]

export const ASSIGNEE_FILTERS: { value: 'all' | 'me' | 'none'; label: string }[] = [
  { value: 'all', label: 'De todos' },
  { value: 'me', label: 'Minhas' },
  { value: 'none', label: 'Sem responsável' },
]

/** Uma tarefa que nasceu de uma obrigação (código e período), para mostrar a etiqueta. */
export function originLabel(origin: string | null): string | null {
  if (!origin) return null
  return origin.startsWith('obrigacao:') ? 'Obrigação' : 'Automática'
}
