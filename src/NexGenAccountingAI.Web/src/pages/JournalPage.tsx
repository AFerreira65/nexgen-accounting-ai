import { ArrowLeft, Landmark } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { Pager, usePagedList } from '../app/Pager'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

export function JournalPage() {
  const { journalId } = useParams()
  usePublishRecord('diarios', journalId, journalId ? `Diário ${journalId}` : undefined)
  const { accountingSetup, accountingSource, activeCompany, movements, user } = useWorkContext()
  const pageSize = activeCompany.settings.pageSize || 50

  // Numa empresa grande, um diário como o de vendas tem milhares de
  // lançamentos: com ligação à base de dados pede-se só a página atual;
  // em demonstração — ou se o pedido falhar — pagina-se o array do contexto.
  const onDatabase = accountingSource === 'database'
  const [server, setServer] = useState<{ items: Movement[]; totalItems: number; totalPages: number; currentPage: number } | null>(null)
  const [page, setPage] = useState(1)

  useEffect(() => setPage(1), [journalId])

  useEffect(() => {
    if (!onDatabase || !journalId) { setServer(null); return }
    const controller = new AbortController()
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize), journal: journalId })
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements?${params}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as { items: Movement[]; page: number; totalItems: number; totalPages: number }
      })
      .then((body) => setServer({ items: body.items, totalItems: body.totalItems, totalPages: body.totalPages, currentPage: body.page }))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setServer(null)
      })
    return () => controller.abort()
  }, [onDatabase, journalId, page, pageSize, activeCompany.companyId, user.id])

  const related = movements.filter((movement) => movement.journalId === journalId)
  const clientPaged = usePagedList(related, journalId ?? '', pageSize)

  const useServer = onDatabase && server !== null
  const rows = useServer ? server.items : clientPaged.paged
  const count = useServer ? server.totalItems : related.length
  const currentPage = useServer ? server.currentPage : clientPaged.currentPage
  const totalPages = useServer ? server.totalPages : clientPaged.totalPages
  const onPage = useServer ? setPage : clientPaged.setPage
  const journalName = rows[0]?.journalName
    ?? accountingSetup.journals.find((journal) => journal.code === journalId)?.name
    ?? journalId

  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading"><div><p className="eyebrow">Diário contabilístico</p><div className="heading-line"><span className="heading-symbol"><Landmark /></span><h1>{journalName}</h1></div><p>Consulta contextual dos movimentos do diário.</p></div></header>
    <section className="panel"><div className="panel-heading"><div><h2>Movimentos do diário</h2><p>Dados contabilísticos da empresa ativa</p></div><span className="attention-count">{count}</span></div><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Documento</th><th>Estado</th><th className="numeric">Valor</th></tr></thead><tbody>{rows.map((movement) => <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td><td>{movement.description}</td><td>{movement.document}</td><td><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></td><td className="numeric strong">{euro.format(movement.amount)}</td></tr>)}</tbody></table></div><Pager currentPage={currentPage} totalPages={totalPages} onPage={onPage} />{count === 0 && <p className="empty-state">Sem movimentos neste diário.</p>}</section>
  </div>
}
