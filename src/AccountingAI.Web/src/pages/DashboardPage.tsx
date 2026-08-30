import { AlertTriangle, ArrowRight, BookOpen, CheckCircle2, Clock3, Database, Scale } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

export function DashboardPage() {
  const { accountingSource, movements, user } = useWorkContext()
  const pending = movements.filter((movement) => movement.status === 'Por conferir')
  const accounts = new Set(movements.flatMap((movement) => movement.lines.map((line) => line.account)))
  const total = movements.reduce((sum, movement) => sum + movement.amount, 0)
  const firstName = user.displayName.replace(' (Demonstração)', '').split(' ')[0]

  return (
    <div className="page-stack">
      <header className="page-heading dashboard-heading">
        <div><p className="eyebrow">Núcleo contabilístico local</p><h1>Bom dia, {firstName}</h1><p>Dados de teste persistidos, com acesso direto ao movimento, diário e conta.</p></div>
        <Link className="button button-primary" to="/movimentos"><Database size={17} /> Consultar dados</Link>
      </header>

      <section className="metric-grid" aria-label="Indicadores do período">
        <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Movimentos carregados</small><strong>{movements.length}</strong><span>{accountingSource === 'database' ? 'Lidos do SQL Server' : 'Fallback visual local'}</span></div></article>
        <article className="metric-card"><span className="metric-icon blue"><Database /></span><div><small>Total movimentado</small><strong>{euro.format(total)}</strong><span>Débito e crédito equilibrados</span></div></article>
        <article className="metric-card"><span className="metric-icon amber"><Clock3 /></span><div><small>Por conferir</small><strong>{pending.length}</strong><span>Dados sintéticos para validação</span></div></article>
        <article className="metric-card"><span className="metric-icon rose"><BookOpen /></span><div><small>Contas utilizadas</small><strong>{accounts.size}</strong><span>Na empresa selecionada</span></div></article>
      </section>

      <div className="dashboard-grid">
        <section className="panel">
          <div className="panel-heading"><div><h2>Movimentos recentes</h2><p>Últimas operações da empresa selecionada</p></div><Link className="text-link" to="/movimentos">Ver todos <ArrowRight size={15} /></Link></div>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th></tr></thead>
              <tbody>{movements.map((movement) => <tr key={movement.id}>
                <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td>
                <td>{movement.description}<small>{movement.document}</small></td>
                <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td>
                <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td>
                <td className="numeric strong">{euro.format(movement.amount)}</td>
              </tr>)}</tbody>
            </table>
          </div>
        </section>

        <aside className="panel attention-panel">
          <div className="panel-heading"><div><h2>A sua atenção</h2><p>Movimentos de teste por conferir</p></div><span className="attention-count">{pending.length}</span></div>
          {pending.map((movement) => <Link className="attention-item" key={movement.id} to={`/movimentos/${movement.id}`}><span className="attention-icon amber"><AlertTriangle /></span><span><strong>{movement.description}</strong><small>{movement.id.replaceAll('-', ' ')} · {euro.format(movement.amount)}</small></span><ArrowRight size={16} /></Link>)}
          {pending.length === 0 && <div className="attention-item muted-item"><span className="attention-icon green"><CheckCircle2 /></span><span><strong>Todos os movimentos conferidos</strong><small>Sem dados de teste pendentes</small></span></div>}
        </aside>
      </div>
    </div>
  )
}
