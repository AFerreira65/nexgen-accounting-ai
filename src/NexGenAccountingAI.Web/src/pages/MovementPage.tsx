import { ArrowLeft, BookOpen, Check, Copy, FileText, Landmark, Link2, PencilLine } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { entrySourceLabel } from '../app/entrySources'
import { useMovementGuards } from '../app/movementGuards'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'
import { MovementDocuments } from './MovementDocuments'
import { MovementEditor } from './MovementEditor'

export function MovementPage() {
  const { movementId } = useParams()
  usePublishRecord('movimentos', movementId, movementId?.replaceAll('-', ' '))
  const { accountingSource, activeCompany, movements, reopenMovement, reviewMovement, user, voidMovement } = useWorkContext()
  const blockedReason = useMovementGuards()
  const [copied, setCopied] = useState(false)
  const [busy, setBusy] = useState(false)
  const [editing, setEditing] = useState<'correct' | 'duplicate' | null>(null)
  const [revision, setRevision] = useState(0)

  // Numa empresa ligada à base de dados, o detalhe vem de um pedido por
  // referência em vez do array completo; em demonstração — ou se o pedido
  // falhar — usa o que o contexto já tem.
  const onDatabase = accountingSource === 'database'
  const fromContext = movements.find((item) => item.id === movementId)
  const [fetched, setFetched] = useState<Movement | null | 'loading'>(onDatabase ? 'loading' : null)

  useEffect(() => {
    if (!onDatabase || !movementId) { setFetched(null); return }
    const controller = new AbortController()
    setFetched('loading')
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(movementId)}`, { signal: controller.signal })
      .then(async (response) => {
        if (response.status === 404) return null
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as Movement
      })
      .then((body) => setFetched(body))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFetched(null)
      })
    return () => controller.abort()
  }, [onDatabase, movementId, activeCompany.companyId, user.id, revision])

  const movement = onDatabase
    ? (fetched === 'loading' ? fromContext : fetched ?? fromContext)
    : fromContext

  if (onDatabase && fetched === 'loading' && !fromContext) {
    return <div className="empty-state"><h1>A carregar o movimento…</h1></div>
  }
  if (!movement) return <div className="empty-state"><h1>Movimento não encontrado</h1><Link to="/movimentos">Voltar aos movimentos</Link></div>

  const copyLink = () => {
    void navigator.clipboard?.writeText(window.location.href).then(() => {
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    })
  }

  const act = async (action: () => Promise<void>) => {
    setBusy(true)
    try { await action(); setRevision((value) => value + 1) } catch { /* a página volta a carregar o estado */ } finally { setBusy(false) }
  }

  const blocked = blockedReason(movement)
  const isSystemEntry = movement.documentType === 'AP' || movement.documentType === 'AB'
  const statusBadge = movement.status === 'Conferido' ? 'success' : movement.status === 'Anulado' ? 'neutral' : 'warning'
  const mainAccount = movement.lines[0]
  return <div className="page-stack">
    <Link className="back-link" to="/movimentos"><ArrowLeft size={16} /> Movimentos contabilísticos</Link>
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Movimento contabilístico</p><div className="heading-line"><h1>{movement.id.replaceAll('-', ' ')}</h1><span className={`badge ${statusBadge}`}>{movement.status}</span>{movement.documentType ? <span className="badge neutral">{movement.documentType}</span> : null}</div><p>{movement.description}</p></div>
      <div className="heading-actions">
        {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy} onClick={() => void act(() => reviewMovement(movement.id))}>Conferir</button>}
        {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy || blocked !== null} title={blocked ?? undefined} onClick={() => setEditing('correct')}><PencilLine size={16} /> Corrigir</button>}
        {movement.status === 'Conferido' && !isSystemEntry && <button className="button button-secondary" type="button" disabled={busy || blocked !== null} title={blocked ?? undefined} onClick={() => void act(() => reopenMovement(movement.id))}>Reabrir</button>}
        {movement.status !== 'Anulado' && <button className="button button-secondary" type="button" disabled={busy || blocked !== null} title={blocked ?? undefined} onClick={() => void act(() => voidMovement(movement.id))}>Anular</button>}
        {!isSystemEntry && <button className="button button-secondary" type="button" onClick={() => setEditing('duplicate')}><Copy size={16} /> Duplicar</button>}
        <button className="button button-secondary" type="button" onClick={copyLink}>{copied ? <><Check size={16} /> Ligação copiada</> : <><Link2 size={16} /> Copiar ligação</>}</button>
      </div>
    </header>
    {blocked !== null && movement.status !== 'Anulado' && <p className="save-status"><span className="badge neutral">{blocked}</span> Não é possível reabrir nem anular este lançamento.</p>}
    {editing !== null && <MovementEditor movement={movement} duplicate={editing === 'duplicate'} onClose={() => { setEditing(null); setRevision((value) => value + 1) }} />}

    <section className="context-grid" aria-label="Atalhos contextuais">
      <Link className="context-card" to={`/diarios/${movement.journalId}`}><span className="context-icon navy"><Landmark /></span><span><small>Diário</small><strong>{movement.journalName}</strong><em>Abrir diário e movimentos relacionados</em></span><ArrowLeft className="rotate-180" size={17} /></Link>
      <Link className="context-card" to={`/contas/${mainAccount.account}`}><span className="context-icon teal"><BookOpen /></span><span><small>Conta principal</small><strong>{mainAccount.account} · {mainAccount.accountName}</strong><em>Ver razão e saldo da conta</em></span><ArrowLeft className="rotate-180" size={17} /></Link>
      {onDatabase
        ? <a className="context-card" href="#documentos"><span className="context-icon amber"><FileText /></span><span><small>Documento de origem</small><strong>{movement.document}{movement.documentDate ? ` · ${formatMovementDate(movement.documentDate)}` : ''}</strong><em>Ver os documentos anexados</em></span><ArrowLeft className="rotate-180" size={17} /></a>
        : <Link className="context-card" to="/documentos"><span className="context-icon amber"><FileText /></span><span><small>Documento de origem</small><strong>{movement.document}{movement.documentDate ? ` · ${formatMovementDate(movement.documentDate)}` : ''}</strong><em>Tipos de documento</em></span><ArrowLeft className="rotate-180" size={17} /></Link>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Linhas do movimento</h2><p>Débitos e créditos equilibrados</p></div><span className="reconciled">Débito = Crédito</span></div>
      <div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead><tbody>
        {movement.lines.map((line) => <tr key={`${line.account}-${line.description}`}><td><Link className="primary-cell" to={`/contas/${line.account}`}>{line.account}</Link><small>{line.accountName}</small></td><td>{line.description}{line.vatCode && <small>IVA {line.vatCode}</small>}{line.costCenter && <small>C. custo {line.costCenter}</small>}{line.partyTaxNumber && <small>{line.partyName} · {line.partyCountryCode && line.partyCountryCode !== 'PT' ? `${line.partyCountryCode} ` : ''}{line.partyTaxNumber}</small>}</td><td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td><td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td></tr>)}
      </tbody><tfoot><tr><td colSpan={2}>Totais</td><td className="numeric">{euro.format(movement.amount)}</td><td className="numeric">{euro.format(movement.amount)}</td></tr></tfoot></table></div>
    </section>

    {onDatabase && <MovementDocuments reference={movement.id} />}

    {movement.notes ? <section className="panel notes-panel"><div className="panel-heading"><div><h2>Observações</h2><p>Nota interna, não sai em documentos oficiais</p></div></div><p className="notes-body">{movement.notes}</p></section> : null}

    <section className="metadata-strip"><span><small>Data</small><strong>{formatMovementDate(movement.date)}</strong></span><span><small>Período</small><strong>{movement.date.slice(0, 7).replace('-', ' / ')}</strong></span><span><small>Origem</small><strong>{entrySourceLabel(movement.source)}</strong></span><span><small>Documento</small><strong>{movement.document.split(' · ')[0]}</strong></span></section>
  </div>
}
