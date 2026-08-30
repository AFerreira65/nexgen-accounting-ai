import { Database, Filter, Search } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

export function MovementsPage() {
  const { accountingLoading, accountingSource, movements } = useWorkContext()
  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Movimentos contabilísticos</h1><p>Consulte e percorra lançamentos, contas e diários sem perder o contexto.</p></div><span className={`connection-badge ${accountingSource}`}><Database size={15} />{accountingSource === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <div className="filterbar"><label><Search size={17} /><input aria-label="Pesquisar movimentos" placeholder="Pesquisar movimento ou descrição…" /></label><button className="button button-secondary" type="button"><Filter size={16} /> Filtros</button></div>
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{movements.map((movement) => <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td><td>{movement.description}<small>{movement.document}</small></td><td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td><td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td><td className="numeric strong">{euro.format(movement.amount)}</td><td><Link className="row-action" to={`/movimentos/${movement.id}`} aria-label={`Abrir ${movement.id}`}>Abrir</Link></td></tr>)}</tbody></table></div>{accountingLoading && <p className="empty-state">A carregar movimentos da base de dados…</p>}{!accountingLoading && movements.length === 0 && <p className="empty-state">Esta empresa ainda não tem movimentos de teste.</p>}</section>
  </div>
}
