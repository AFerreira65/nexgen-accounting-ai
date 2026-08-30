import { AlertTriangle, ArrowRight, CheckCircle2, Clock3, FileCheck2, Scale, Sparkles, TrendingUp } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, movements } from '../app/demo-data'

export function DashboardPage() {
  return (
    <div className="page-stack">
      <header className="page-heading dashboard-heading">
        <div><p className="eyebrow">Sábado, 29 de agosto</p><h1>Bom dia, António</h1><p>O essencial da contabilidade, com acesso direto ao detalhe.</p></div>
        <Link className="button button-primary" to="/movimentos/COMP-2026-00842"><Sparkles size={17} /> Novo movimento</Link>
      </header>

      <section className="metric-grid" aria-label="Indicadores do período">
        <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Movimentos no mês</small><strong>842</strong><span className="positive"><TrendingUp size={14} /> 12,4% vs. julho</span></div></article>
        <article className="metric-card"><span className="metric-icon blue"><FileCheck2 /></span><div><small>Documentos tratados</small><strong>94%</strong><span>487 de 518 documentos</span></div></article>
        <article className="metric-card"><span className="metric-icon amber"><Clock3 /></span><div><small>Por conferir</small><strong>12</strong><span>3 com prioridade alta</span></div></article>
        <article className="metric-card"><span className="metric-icon rose"><AlertTriangle /></span><div><small>Alertas fiscais</small><strong>3</strong><span>Próximo prazo: 7 dias</span></div></article>
      </section>

      <div className="dashboard-grid">
        <section className="panel">
          <div className="panel-heading"><div><h2>Movimentos recentes</h2><p>Últimas operações da empresa selecionada</p></div><Link className="text-link" to="/movimentos">Ver todos <ArrowRight size={15} /></Link></div>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th></tr></thead>
              <tbody>{movements.map((movement) => <tr key={movement.id}>
                <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{movement.date}</small></td>
                <td>{movement.description}<small>{movement.document}</small></td>
                <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td>
                <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td>
                <td className="numeric strong">{euro.format(movement.amount)}</td>
              </tr>)}</tbody>
            </table>
          </div>
        </section>

        <aside className="panel attention-panel">
          <div className="panel-heading"><div><h2>A sua atenção</h2><p>Prioridades para hoje</p></div><span className="attention-count">3</span></div>
          <Link className="attention-item" to="/movimentos/BANC-2026-00196"><span className="attention-icon amber"><AlertTriangle /></span><span><strong>Conferir movimento bancário</strong><small>BANC 2026 00196 · {euro.format(1476)}</small></span><ArrowRight size={16} /></Link>
          <Link className="attention-item" to="/documentos"><span className="attention-icon teal"><FileCheck2 /></span><span><strong>8 documentos por classificar</strong><small>Recebidos nas últimas 24 horas</small></span><ArrowRight size={16} /></Link>
          <Link className="attention-item" to="/tarefas"><span className="attention-icon blue"><Clock3 /></span><span><strong>Preparar declaração de IVA</strong><small>Período 2026/07 · prazo em 7 dias</small></span><ArrowRight size={16} /></Link>
          <div className="attention-item muted-item"><span className="attention-icon green"><CheckCircle2 /></span><span><strong>Reconciliação de julho concluída</strong><small>Sem diferenças pendentes</small></span></div>
        </aside>
      </div>
    </div>
  )
}
