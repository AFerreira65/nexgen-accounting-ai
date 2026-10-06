import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { FirmTasksPage } from './FirmTasksPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1', legalName: 'Empresa A', role: 'Accountant', settings: {} },
    companies: [
      { companyId: 'c1', legalName: 'Empresa A', role: 'Accountant', settings: {} },
      { companyId: 'c2', legalName: 'Empresa B', role: 'Viewer', settings: {} },
    ],
    source: 'database',
    user: { id: 'u1', displayName: 'Rui' },
  }),
}))

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const task = (extra: Record<string, unknown>) => ({
  id: 't1', companyId: 'c1', companyName: 'Empresa A', title: 'Conferir o banco', description: null, dueDate: '2026-10-01', priority: 'high', status: 'open',
  assigneeUserId: 'u1', assigneeName: 'Rui', createdByName: 'Maria', createdAtUtc: '2026-10-01T10:00:00Z', completedAtUtc: null, completedByName: null,
  origin: null, overdue: true, mine: true, ...extra,
})
const list = [
  task({}),
  task({ id: 't2', title: 'Entregar IVA (setembro)', origin: 'obrigacao:IVA-DP-M:2026-09', overdue: false, dueDate: '2026-10-20', assigneeUserId: null, assigneeName: null, mine: false, priority: 'normal' }),
  task({ id: 't3', companyId: 'c2', companyName: 'Empresa B', title: 'Da empresa só de consulta', overdue: false, mine: false }),
]

function serve(calls: { url: string; body?: unknown }[]) {
  return vi.fn((url: string, init?: RequestInit) => {
    const body = init?.body ? JSON.parse(String(init.body)) as unknown : undefined
    calls.push({ url: `${init?.method ?? 'GET'} ${url}`, body })
    if (url.includes('/tasks/assignees')) return Promise.resolve(json([{ userId: 'u1', displayName: 'Rui', isResponsible: true }, { userId: 'u9', displayName: 'Ana', isResponsible: false }]))
    if (url.includes('/tasks/from-obligations')) return Promise.resolve(json({ created: 3, alreadyExisted: 1 }))
    if (url.includes('/tasks?')) return Promise.resolve(json(list))
    if (init?.method === 'DELETE') return Promise.resolve(new Response(null, { status: 204 }))
    if (init?.method === 'POST' || init?.method === 'PUT') return Promise.resolve(json(list[0]))
    return Promise.resolve(json({}, 404))
  })
}

const renderPage = () => render(<MemoryRouter><FirmTasksPage /></MemoryRouter>)

describe('tarefas do gabinete', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lista as tarefas, assinala as em atraso e esconde as ações onde só há consulta', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    expect(await screen.findByText('Conferir o banco')).toBeInTheDocument()
    expect(screen.getAllByText(/em atraso/, { selector: '.badge' }), 'o resumo e a linha').toHaveLength(2)
    expect(screen.getByText('Obrigação')).toBeInTheDocument()
    expect(screen.getByLabelText('Concluir Conferir o banco')).toBeInTheDocument()
    expect(screen.queryByLabelText('Concluir Da empresa só de consulta')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Editar Da empresa só de consulta')).not.toBeInTheDocument()
    expect(calls[0].url).toContain('status=open')
  })

  it('aplica os filtros no pedido', async () => {
    const calls: { url: string }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    await screen.findByText('Conferir o banco')
    fireEvent.change(screen.getByLabelText('Responsável'), { target: { value: 'me' } })
    fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'c1' } })
    fireEvent.change(screen.getByLabelText('Estado'), { target: { value: 'all' } })
    await waitFor(() => expect(calls.some((call) => call.url.includes('assignee=me') && call.url.includes('company=c1') && call.url.includes('status=all'))).toBe(true))
  })

  it('conclui uma tarefa mantendo os restantes dados', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    fireEvent.click(await screen.findByLabelText('Concluir Conferir o banco'))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('PUT') && call.url.endsWith('/companies/c1/tasks/t1'))).toBe(true))
    const put = calls.find((call) => call.url.startsWith('PUT'))!.body as { status: string; title: string; priority: string }
    expect(put).toMatchObject({ status: 'done', title: 'Conferir o banco', priority: 'high' })
  })

  it('cria uma tarefa atribuída a um membro da empresa', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls))
    renderPage()
    await screen.findByText('Conferir o banco')
    fireEvent.click(screen.getByRole('button', { name: /Nova tarefa/ }))
    const form = await screen.findByRole('region', { name: 'Nova tarefa' })
    fireEvent.change(within(form).getByLabelText('Título da tarefa'), { target: { value: 'Pedir extratos' } })
    fireEvent.change(within(form).getByLabelText('Prazo da tarefa'), { target: { value: '2026-10-31' } })
    fireEvent.change(within(form).getByLabelText('Prioridade da tarefa'), { target: { value: 'low' } })
    await waitFor(() => expect(within(form).getByRole('option', { name: /Ana/ })).toBeInTheDocument())
    fireEvent.change(within(form).getByLabelText('Responsável da tarefa'), { target: { value: 'u9' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(calls.some((call) => call.url.startsWith('POST') && call.url.endsWith('/companies/c1/tasks'))).toBe(true))
    expect(calls.find((call) => call.url.startsWith('POST') && call.url.endsWith('/companies/c1/tasks'))!.body)
      .toEqual({ title: 'Pedir extratos', description: null, dueDate: '2026-10-31', priority: 'low', assigneeUserId: 'u9' })
  })

  it('cria as tarefas das obrigações e diz quantas', async () => {
    vi.stubGlobal('fetch', serve([]))
    renderPage()
    await screen.findByText('Conferir o banco')
    fireEvent.click(screen.getByRole('button', { name: /Criar das obrigações/ }))
    expect(await screen.findByText(/3 tarefa\(s\) criada\(s\) das obrigações \(1 já existiam\)/)).toBeInTheDocument()
  })
})
