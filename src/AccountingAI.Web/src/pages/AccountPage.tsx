import { ArrowLeft, BookOpen, Landmark } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

export function AccountPage() {
  const { accountCode } = useParams()
  const { movements } = useWorkContext()
  const related = movements.filter((movement) => movement.status !== 'Anulado' && movement.lines.some((line) => line.account === accountCode))
  const line = related.flatMap((movement) => movement.lines).find((item) => item.account === accountCode)
  let running = 0
  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading"><div><p className="eyebrow">Razão da conta</p><div className="heading-line"><span className="heading-symbol"><BookOpen /></span><h1>{accountCode} · {line?.accountName ?? 'Conta contabilística'}</h1></div><p>Consulta rápida de saldo e movimentos relacionados.</p></div><button className="button button-primary" type="button">Extrato da conta</button></header>
    <section className="metric-grid compact-grid"><article className="metric-card"><div><small>Saldo atual</small><strong>{euro.format(related.reduce((total, movement) => { const accountLine = movement.lines.find((item) => item.account === accountCode); return total + (accountLine?.debit ?? 0) - (accountLine?.credit ?? 0) }, 0))}</strong><span>Exercício de 2026</span></div></article><article className="metric-card"><div><small>Movimentos</small><strong>{related.length}</strong><span>Com ligação direta ao lançamento</span></div></article></section>
    <section className="panel"><div className="panel-heading"><div><h2>Movimentos desta conta</h2><p>Clique num movimento ou diário para consultar o contexto.</p></div></div><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Diário</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead><tbody>{related.map((movement) => { const accountLine = movement.lines.find((item) => item.account === accountCode)!; running += (accountLine.debit ?? 0) - (accountLine.credit ?? 0); return <tr key={movement.id}><td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td><td><Link className="inline-link" to={`/diarios/${movement.journalId}`}><Landmark size={14} /> {movement.journalId}</Link></td><td>{movement.description}</td><td className="numeric">{accountLine.debit ? euro.format(accountLine.debit) : '—'}</td><td className="numeric">{accountLine.credit ? euro.format(accountLine.credit) : '—'}</td><td className="numeric strong">{euro.format(running)}</td></tr>})}</tbody></table></div></section>
  </div>
}
