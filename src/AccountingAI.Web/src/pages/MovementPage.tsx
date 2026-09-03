import { ArrowLeft, BookOpen, FileText, Landmark, Link2, MoreHorizontal } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

export function MovementPage() {
  const { movementId } = useParams()
  const { movements } = useWorkContext()
  const movement = movements.find((item) => item.id === movementId)
  if (!movement) return <div className="empty-state"><h1>Movimento não encontrado</h1><Link to="/movimentos">Voltar aos movimentos</Link></div>

  const mainAccount = movement.lines[0]
  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Movimento contabilístico</p><div className="heading-line"><h1>{movement.id.replaceAll('-', ' ')}</h1><span className={`badge ${movement.status === 'Conferido' ? 'success' : 'warning'}`}>{movement.status}</span></div><p>{movement.description}</p></div>
      <div className="heading-actions"><button className="button button-secondary" type="button"><Link2 size={16} /> Copiar ligação</button><button className="icon-button bordered" type="button" aria-label="Mais opções"><MoreHorizontal /></button></div>
    </header>

    <section className="context-grid" aria-label="Atalhos contextuais">
      <Link className="context-card" to={`/diarios/${movement.journalId}`}><span className="context-icon navy"><Landmark /></span><span><small>Diário</small><strong>{movement.journalName}</strong><em>Abrir diário e movimentos relacionados</em></span><ArrowLeft className="rotate-180" size={17} /></Link>
      <Link className="context-card" to={`/contas/${mainAccount.account}`}><span className="context-icon teal"><BookOpen /></span><span><small>Conta principal</small><strong>{mainAccount.account} · {mainAccount.accountName}</strong><em>Ver razão e saldo da conta</em></span><ArrowLeft className="rotate-180" size={17} /></Link>
      <Link className="context-card" to="/documentos"><span className="context-icon amber"><FileText /></span><span><small>Documento de origem</small><strong>{movement.document}</strong><em>Abrir documento digitalizado</em></span><ArrowLeft className="rotate-180" size={17} /></Link>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Linhas do movimento</h2><p>Débitos e créditos equilibrados</p></div><span className="reconciled">Débito = Crédito</span></div>
      <div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead><tbody>
        {movement.lines.map((line) => <tr key={`${line.account}-${line.description}`}><td><Link className="primary-cell" to={`/contas/${line.account}`}>{line.account}</Link><small>{line.accountName}</small></td><td>{line.description}</td><td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td><td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td></tr>)}
      </tbody><tfoot><tr><td colSpan={2}>Totais</td><td className="numeric">{euro.format(movement.amount)}</td><td className="numeric">{euro.format(movement.amount)}</td></tr></tfoot></table></div>
    </section>

    {movement.notes ? <section className="panel notes-panel"><div className="panel-heading"><div><h2>Observações</h2><p>Nota interna, não sai em documentos oficiais</p></div></div><p className="notes-body">{movement.notes}</p></section> : null}

    <section className="metadata-strip"><span><small>Data</small><strong>{formatMovementDate(movement.date)}</strong></span><span><small>Período</small><strong>{movement.date.slice(0, 7).replace('-', ' / ')}</strong></span><span><small>Origem</small><strong>Dados de teste locais</strong></span><span><small>Referência</small><strong>{movement.document.split(' · ')[0]}</strong></span></section>
  </div>
}
