import { Filter, Plus, Search } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, movements } from '../app/demo-data'

export function MovementsPage() {
  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Movimentos contabilísticos</h1><p>Consulte e percorra lançamentos, contas e diários sem perder o contexto.</p></div><Link className="button button-primary" to="/movimentos/COMP-2026-00842"><Plus size={17} /> Novo movimento</Link></header>
    <div className="filterbar"><label><Search size={17} /><input aria-label="Pesquisar movimentos" placeholder="Pesquisar movimento ou descrição…" /></label><button className="button button-secondary" type="button"><Filter size={16} /> Filtros</button></div>
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{movements.map((movement) => <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{movement.date}</small></td><td>{movement.description}<small>{movement.document}</small></td><td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td><td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td><td className="numeric strong">{euro.format(movement.amount)}</td><td><Link className="row-action" to={`/movimentos/${movement.id}`} aria-label={`Abrir ${movement.id}`}>Abrir</Link></td></tr>)}</tbody></table></div></section>
  </div>
}
