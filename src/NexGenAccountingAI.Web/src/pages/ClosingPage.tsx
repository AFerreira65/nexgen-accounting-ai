import { CircleAlert, CircleCheck, ClipboardList, Database, Info } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { signalLoadFailure } from '../app/loadFailure'
import { useWorkContext } from '../app/WorkContext'

type Item = {
  key: string
  group: string
  label: string
  kind: 'auto' | 'manual'
  state: 'ok' | 'attention' | 'info' | 'done' | 'pending'
  count: number | null
  detail: string
  link: string | null
  note: string | null
  checkedByName: string | null
  checkedAtUtc: string | null
}
type Checklist = { year: number; month: number; periodKey: string; items: Item[]; attention: number; pendingManual: number; ready: boolean }

const MONTHS = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']
const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

/**
 * O papel de trabalho de fecho de um mês (F4F-02): o que ainda falta tratar antes de fechar, com as
 * verificações automáticas à vista e as conferências manuais por marcar. Não bloqueia nada.
 */
export function ClosingPage() {
  const { activeCompany, companies, source, user } = useWorkContext()
  const now = new Date()
  const [year, setYear] = useState(activeCompany.settings.defaultFiscalYear ?? now.getUTCFullYear())
  const [month, setMonth] = useState(now.getMonth() === 0 ? 12 : now.getMonth())
  const [list, setList] = useState<Checklist | null>(null)
  const [error, setError] = useState('')
  const [notes, setNotes] = useState<Record<string, string>>({})
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/closing`
  const canWrite = companies.find((company) => company.companyId === activeCompany.companyId)?.role !== 'Viewer'

  const load = useCallback(() => {
    if (source !== 'database') return () => undefined
    const controller = new AbortController()
    void fetch(`${root}?${new URLSearchParams({ year: String(year), month: String(month) })}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as Checklist : Promise.reject(new Error()))
      .then((value) => { if (!controller.signal.aborted) { setList(value); setNotes(Object.fromEntries(value.items.filter((item) => item.kind === 'manual').map((item) => [item.key, item.note ?? '']))) } })
      .catch(() => { if (!controller.signal.aborted) { signalLoadFailure(); setError('Não foi possível ler o fecho.') } })
    return () => controller.abort()
  }, [root, source, year, month])
  useEffect(() => load(), [load])

  async function toggle(item: Item) {
    setError('')
    const response = await fetch(`${root}/${item.key}?${new URLSearchParams({ year: String(year), month: String(month) })}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ checked: item.state !== 'done', note: notes[item.key] || null }),
    })
    if (!response.ok) { setError('Não foi possível guardar a verificação.'); return }
    load()
  }

  /** A nota de uma conferência já feita grava-se ao sair do campo. */
  async function saveNote(item: Item) {
    if (item.state !== 'done' || (notes[item.key] ?? '') === (item.note ?? '')) return
    const response = await fetch(`${root}/${item.key}?${new URLSearchParams({ year: String(year), month: String(month) })}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ checked: true, note: notes[item.key] || null }),
    })
    if (!response.ok) { setError('Não foi possível guardar a nota.'); return }
    load()
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Fecho do mês</h1><p>O fecho precisa da base de dados; em demonstração não está disponível.</p></div></header></div>
  }

  const groups = [...new Set((list?.items ?? []).map((item) => item.group))]
  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gestão</p><h1>Fecho do mês</h1><p>O que falta tratar antes de fechar o mês: as verificações automáticas saem da contabilidade e as conferências marcam-se à mão, com uma nota. Não bloqueia nada: diz só se o mês está pronto.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}

    <section className="panel">
      <div className="filterbar">
        <label className="inline-check">Mês
          <select aria-label="Mês" value={month} onChange={(event) => setMonth(Number(event.target.value))}>
            {MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
          </select>
        </label>
        <label className="inline-check">Ano <input type="number" aria-label="Ano" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
        {list && (list.ready
          ? <span className="badge success"><CircleCheck size={14} /> Pronto para fechar</span>
          : <span className="badge warning">{list.attention > 0 ? `${list.attention} por tratar` : ''}{list.attention > 0 && list.pendingManual > 0 ? ' · ' : ''}{list.pendingManual > 0 ? `${list.pendingManual} conferência(s) por fazer` : ''}</span>)}
      </div>
    </section>

    {list && groups.map((group) => <section key={group} className="panel" aria-label={group}>
      <div className="panel-heading"><h2><ClipboardList size={17} /> {group}</h2></div>
      <ul className="closing-list" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {list.items.filter((item) => item.group === group).map((item) => <li key={item.key} style={{ display: 'flex', gap: 12, alignItems: 'flex-start', padding: '8px 0', borderBottom: '1px solid var(--line, #e0e8ea)' }}>
          <span aria-hidden style={{ width: 20, paddingTop: 2 }}>
            {item.kind === 'manual'
              ? <input type="checkbox" aria-label={item.label} checked={item.state === 'done'} disabled={!canWrite} onChange={() => void toggle(item)} />
              : item.state === 'ok' ? <CircleCheck size={16} /> : item.state === 'attention' ? <CircleAlert size={16} /> : <Info size={16} />}
          </span>
          <div style={{ flex: 1 }}>
            <strong>{item.label}</strong>
            {item.kind === 'auto' && <span className={`badge ${item.state === 'ok' ? 'success' : item.state === 'attention' ? 'warning' : ''}`} style={{ marginLeft: 8 }}>{item.state === 'ok' ? 'Em ordem' : item.state === 'attention' ? 'Por tratar' : 'Não se aplica'}</span>}
            <div><small>{item.detail}</small>{item.kind === 'auto' && item.link && item.state === 'attention' && <> · <Link to={item.link}>Tratar</Link></>}</div>
            {item.kind === 'manual' && <div style={{ marginTop: 4 }}>
              <input aria-label={`Nota: ${item.label}`} placeholder="Nota (opcional)" maxLength={500} style={{ width: '60%' }} disabled={!canWrite} value={notes[item.key] ?? ''} onChange={(event) => setNotes({ ...notes, [item.key]: event.target.value })} onBlur={() => void saveNote(item)} />
              {item.state === 'done' && item.checkedByName && <small style={{ marginLeft: 8 }}>Feito por {item.checkedByName}{item.checkedAtUtc ? ` em ${dateTime.format(new Date(item.checkedAtUtc))}` : ''}</small>}
            </div>}
          </div>
        </li>)}
      </ul>
    </section>)}
  </div>
}
