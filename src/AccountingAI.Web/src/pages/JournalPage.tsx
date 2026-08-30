import { ArrowLeft, Landmark } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { euro, movements } from '../app/demo-data'

export function JournalPage() {
  const { journalId } = useParams()
  const related = movements.filter((movement) => movement.journalId === journalId)
  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading"><div><p className="eyebrow">Diário contabilístico</p><div className="heading-line"><span className="heading-symbol"><Landmark /></span><h1>{related[0]?.journalName ?? journalId}</h1></div><p>Consulta contextual dos movimentos do diário.</p></div><button className="button button-primary" type="button">Novo movimento</button></header>
    <section className="panel"><div className="panel-heading"><div><h2>Movimentos do diário</h2><p>Exercício 2026 · Período 08</p></div><span className="attention-count">{related.length}</span></div><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Documento</th><th>Estado</th><th className="numeric">Valor</th></tr></thead><tbody>{related.map((movement) => <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{movement.date}</small></td><td>{movement.description}</td><td>{movement.document}</td><td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td><td className="numeric strong">{euro.format(movement.amount)}</td></tr>)}</tbody></table></div></section>
  </div>
}
