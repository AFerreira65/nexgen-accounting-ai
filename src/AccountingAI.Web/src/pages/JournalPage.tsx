import { ArrowLeft, Landmark } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { Pager, usePagedList } from '../app/Pager'
import { useWorkContext } from '../app/WorkContext'

export function JournalPage() {
  const { journalId } = useParams()
  const { activeCompany, movements } = useWorkContext()
  const related = movements.filter((movement) => movement.journalId === journalId)
  // Numa empresa grande, um diário como o de vendas tem milhares de
  // lançamentos; pagina-se pelo tamanho de página da empresa.
  const { paged, currentPage, totalPages, setPage } = usePagedList(related, journalId ?? '', activeCompany.settings.pageSize || 50)
  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading"><div><p className="eyebrow">Diário contabilístico</p><div className="heading-line"><span className="heading-symbol"><Landmark /></span><h1>{related[0]?.journalName ?? journalId}</h1></div><p>Consulta contextual dos movimentos do diário.</p></div></header>
    <section className="panel"><div className="panel-heading"><div><h2>Movimentos do diário</h2><p>Dados contabilísticos da empresa ativa</p></div><span className="attention-count">{related.length}</span></div><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Documento</th><th>Estado</th><th className="numeric">Valor</th></tr></thead><tbody>{paged.map((movement) => <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td><td>{movement.description}</td><td>{movement.document}</td><td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td><td className="numeric strong">{euro.format(movement.amount)}</td></tr>)}</tbody></table></div><Pager currentPage={currentPage} totalPages={totalPages} onPage={setPage} />{related.length === 0 && <p className="empty-state">Sem movimentos neste diário.</p>}</section>
  </div>
}
