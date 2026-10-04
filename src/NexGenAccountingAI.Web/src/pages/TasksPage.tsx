import { ArrowLeftRight, ArrowRight, CircleCheck, Clock3, Receipt, Tags, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { yearRange } from '../app/periods'
import { useMovementsSummary, useWorkContext } from '../app/WorkContext'

const VISIBLE_PENDING = 6
const DISMISSED_VAT_KEY_PREFIX = 'accounting-ai.dismissed-vat.'

// «Dispensar até à próxima entrega» só vale para o valor exato dispensado
// (em cêntimos, para não perder por arredondamento): assim que a posição de
// IVA mudar — nova entrega ou novo movimento — o lembrete volta a aparecer.
function readDismissedVatCents(companyId: string): number | null {
  try {
    const raw = window.localStorage.getItem(`${DISMISSED_VAT_KEY_PREFIX}${companyId}`)
    return raw === null ? null : Number(raw)
  } catch {
    return null
  }
}

export function TasksPage() {
  const { activeCompany, crossCheck, source, taxonomyReview } = useWorkContext()
  // Posição de IVA e movimentos por conferir vêm do resumo da fila de
  // trabalho (`useMovementsSummary`) — agregados no servidor numa empresa
  // ligada à base de dados, sem carregar o histórico inteiro.
  const summary = useMovementsSummary()
  const pending = summary.pendingPreview
  const pendingCount = summary.pendingReview
  const unclassified = taxonomyReview.accounts.filter((account) => account.status === 'Unclassified')
  const suggested = taxonomyReview.accounts.filter((account) => account.status === 'SuggestionAvailable')
  const taxonomyPending = unclassified.length + suggested.length
  const vatPosition = summary.vatPosition
  const vatCents = Math.round(vatPosition * 100)
  const [dismissedVatCents, setDismissedVatCents] = useState<number | null>(() => readDismissedVatCents(activeCompany.companyId))
  useEffect(() => setDismissedVatCents(readDismissedVatCents(activeCompany.companyId)), [activeCompany.companyId])
  const vatDue = vatPosition > 0 && vatCents !== dismissedVatCents
  const dismissVat = () => {
    try { window.localStorage.setItem(`${DISMISSED_VAT_KEY_PREFIX}${activeCompany.companyId}`, String(vatCents)) } catch { /* storage é opcional */ }
    setDismissedVatCents(vatCents)
  }
  // Faturas do e-Fatura por lançar no exercício da empresa — só numa empresa
  // ligada à base de dados; um cruzamento indisponível não deve partir as Tarefas.
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const [unbooked, setUnbooked] = useState(0)
  useEffect(() => {
    if (source !== 'database' || !fiscalYear) { setUnbooked(0); return }
    const controller = new AbortController()
    const { from, to } = yearRange(fiscalYear)
    void crossCheck(activeCompany.companyId, { from, to })
      .then((report) => setUnbooked(report.purchases.unbookedCount))
      .catch(() => setUnbooked(0))
    return () => controller.abort()
  }, [activeCompany.companyId, crossCheck, fiscalYear, source])

  const nothingPending = pendingCount === 0 && taxonomyPending === 0 && !vatDue && unbooked === 0

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">NexGen Accounting AI</p><h1>Tarefas</h1><p>O que precisa da sua atenção na empresa ativa.</p></div></header>

    {nothingPending && <section className="panel empty-state"><span className="empty-icon"><CircleCheck /></span><h2>Tudo em dia</h2><p>Não há movimentos por conferir, contas de movimento por classificar nem IVA a entregar.</p></section>}

    {vatDue && <section className="panel attention-panel">
      <div className="panel-heading"><div><h2>IVA a entregar</h2><p>Posição atual das contas de IVA (24.3): liquidado menos dedutível.</p></div><span className="attention-count">{euro.format(vatPosition)}</span></div>
      <Link className="attention-item" to="/iva">
        <span className="attention-icon rose"><Receipt /></span>
        <span><strong>Posição de IVA a entregar ao Estado</strong><small>Contas 2432 (dedutível) e 2433 (liquidado)</small></span>
        <ArrowRight size={16} />
      </Link>
      <button type="button" className="text-link" onClick={dismissVat}><X size={15} /> Dispensar até à próxima entrega</button>
    </section>}

    {pendingCount > 0 && <section className="panel attention-panel">
      <div className="panel-heading"><div><h2>Movimentos por conferir</h2><p>Lançamentos que ainda não foram conferidos, dos mais antigos aos mais recentes.</p></div><span className="attention-count">{pendingCount}</span></div>
      {pending.slice(0, VISIBLE_PENDING).map((movement) => <Link className="attention-item" key={movement.id} to={`/movimentos/${movement.id}`}>
        <span className="attention-icon amber"><Clock3 /></span>
        <span><strong>{movement.description}</strong><small>{movement.id.replaceAll('-', ' ')} · {formatMovementDate(movement.date)} · {euro.format(movement.amount)}</small></span>
        <ArrowRight size={16} />
      </Link>)}
      {pendingCount > VISIBLE_PENDING && <Link className="text-link" to="/movimentos">Ver todos os {pendingCount} <ArrowRight size={15} /></Link>}
    </section>}

    {unbooked > 0 && <section className="panel attention-panel">
      <div className="panel-heading"><div><h2>Faturas do e-Fatura sem correspondência</h2><p>Faturas de compra que o Portal e-Fatura conhece e não se encontraram lançadas na contabilidade, no exercício.</p></div><span className="attention-count">{unbooked}</span></div>
      <Link className="attention-item" to="/cruzamento">
        <span className="attention-icon amber"><ArrowLeftRight /></span>
        <span><strong>Rever o cruzamento de compras</strong><small>e-Fatura × contabilidade, por mês e por documento</small></span>
        <ArrowRight size={16} />
      </Link>
    </section>}

    {taxonomyPending > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Contas de movimento por classificar</h2><p>Sem código de taxonomia atribuído; afeta o balancete por taxonomia e a cobertura para a IES.</p></div><span className="attention-count">{taxonomyPending}</span></div>
      <p className="save-status"><Tags size={15} /> {suggested.length > 0 && `${suggested.length} ${suggested.length === 1 ? 'tem' : 'têm'} sugestão do referencial disponível; `}{unclassified.length} sem sugestão.</p>
      <Link className="text-link" to="/taxonomias">Ir para Taxonomias <ArrowRight size={15} /></Link>
    </section>}
  </div>
}
