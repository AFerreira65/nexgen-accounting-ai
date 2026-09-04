import { ArrowRight, CircleCheck, Clock3, Receipt, Tags } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

const VISIBLE_PENDING = 6

// Posição de IVA atual, calculada sobre os movimentos já carregados pelo
// WorkContext (sem pedido adicional) — mesma fórmula do dashboard e do
// resumo de IVA: 2433 liquidado menos 2432 dedutível.
function currentVatPosition(movements: ReturnType<typeof useWorkContext>['movements']): number {
  let dedutivel = 0
  let liquidado = 0
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    for (const line of movement.lines) {
      if (line.account.startsWith('2432')) dedutivel += line.debit - line.credit
      if (line.account.startsWith('2433')) liquidado += line.credit - line.debit
    }
  }
  return liquidado - dedutivel
}

export function TasksPage() {
  const { movements, taxonomyReview } = useWorkContext()
  const pending = [...movements]
    .filter((movement) => movement.status === 'Por conferir')
    .sort((a, b) => a.date.localeCompare(b.date) || a.id.localeCompare(b.id))
  const unclassified = taxonomyReview.accounts.filter((account) => account.status === 'Unclassified')
  const suggested = taxonomyReview.accounts.filter((account) => account.status === 'SuggestionAvailable')
  const taxonomyPending = unclassified.length + suggested.length
  const vatPosition = currentVatPosition(movements)
  const vatDue = vatPosition > 0
  const nothingPending = pending.length === 0 && taxonomyPending === 0 && !vatDue

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Accounting AI</p><h1>Tarefas</h1><p>O que precisa da sua atenção na empresa ativa.</p></div></header>

    {nothingPending && <section className="panel empty-state"><span className="empty-icon"><CircleCheck /></span><h2>Tudo em dia</h2><p>Não há movimentos por conferir, contas de movimento por classificar nem IVA a entregar.</p></section>}

    {vatDue && <section className="panel attention-panel">
      <div className="panel-heading"><div><h2>IVA a entregar</h2><p>Posição atual das contas de IVA (24.3): liquidado menos dedutível.</p></div><span className="attention-count">{euro.format(vatPosition)}</span></div>
      <Link className="attention-item" to="/iva">
        <span className="attention-icon rose"><Receipt /></span>
        <span><strong>Posição de IVA a entregar ao Estado</strong><small>Contas 2432 (dedutível) e 2433 (liquidado)</small></span>
        <ArrowRight size={16} />
      </Link>
    </section>}

    {pending.length > 0 && <section className="panel attention-panel">
      <div className="panel-heading"><div><h2>Movimentos por conferir</h2><p>Lançamentos que ainda não foram conferidos, dos mais antigos aos mais recentes.</p></div><span className="attention-count">{pending.length}</span></div>
      {pending.slice(0, VISIBLE_PENDING).map((movement) => <Link className="attention-item" key={movement.id} to={`/movimentos/${movement.id}`}>
        <span className="attention-icon amber"><Clock3 /></span>
        <span><strong>{movement.description}</strong><small>{movement.id.replaceAll('-', ' ')} · {formatMovementDate(movement.date)} · {euro.format(movement.amount)}</small></span>
        <ArrowRight size={16} />
      </Link>)}
      {pending.length > VISIBLE_PENDING && <Link className="text-link" to="/movimentos">Ver todos os {pending.length} <ArrowRight size={15} /></Link>}
    </section>}

    {taxonomyPending > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Contas de movimento por classificar</h2><p>Sem código de taxonomia atribuído; afeta o balancete por taxonomia e a cobertura para a IES.</p></div><span className="attention-count">{taxonomyPending}</span></div>
      <p className="save-status"><Tags size={15} /> {suggested.length > 0 && `${suggested.length} ${suggested.length === 1 ? 'tem' : 'têm'} sugestão do referencial disponível; `}{unclassified.length} sem sugestão.</p>
      <Link className="text-link" to="/taxonomias">Ir para Taxonomias <ArrowRight size={15} /></Link>
    </section>}
  </div>
}
