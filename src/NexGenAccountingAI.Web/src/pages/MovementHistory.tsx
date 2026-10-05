import { History } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type HistoryLine = { sequence: number; accountCode: string; accountName: string; description: string; debit: number; credit: number; partyName: string | null; vatCode: string | null; costCenter: string | null }
type HistoryVersion = { entryDate: string; journalCode: string; documentTypeCode: string | null; documentReference: string; documentDate: string | null; description: string; notes: string | null; status: string; totalAmount: number; createdAtUtc: string | null; createdByName: string | null; lines: HistoryLine[] }
type HistoryRow = { id: string; action: string; changedAtUtc: string; changedByName: string | null; before: HistoryVersion }

// O que aconteceu, em português. «Removed» num lançamento que continua a existir é uma correção.
const ACTION_LABELS: Record<string, string> = {
  Removed: 'Corrigido',
  Reviewed: 'Conferido',
  Reopened: 'Reaberto',
  Voided: 'Anulado',
  Changed: 'Alterado',
}
const STATUS_LABELS: Record<string, string> = { PendingReview: 'Por conferir', Reviewed: 'Conferido', Voided: 'Anulado' }
const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

/** O histórico de alterações de um lançamento: quem alterou, quando, e como estava antes. */
export function MovementHistory({ reference, revision }: { reference: string; revision: number }) {
  const { activeCompany, user } = useWorkContext()
  const [rows, setRows] = useState<HistoryRow[] | null>(null)
  const [open, setOpen] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements/${encodeURIComponent(reference)}/history`, { signal: controller.signal })
      .then(async (response) => (response.ok ? await response.json() as HistoryRow[] : []))
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch(() => { if (!controller.signal.aborted) setRows([]) })
    return () => controller.abort()
  }, [activeCompany.companyId, reference, revision, user.id])

  if (!rows || rows.length === 0) return null

  return <section className="panel" id="historico">
    <div className="panel-heading"><div><h2><History size={17} /> Histórico de alterações</h2><p>Cada alteração a este lançamento, da mais recente para a mais antiga, com o estado em que estava antes.</p></div></div>
    <div className="table-wrap"><table>
      <thead><tr><th>Quando</th><th>Quem</th><th>O que aconteceu</th><th>Como estava antes</th><th><span className="sr-only">Ações</span></th></tr></thead>
      <tbody>{rows.map((row) => <Fragment key={row.id}>
        <tr>
          <td>{dateTime.format(new Date(row.changedAtUtc))}</td>
          <td>{row.changedByName ?? 'Processo automático'}</td>
          <td><span className="badge">{ACTION_LABELS[row.action] ?? row.action}</span></td>
          <td>{formatMovementDate(row.before.entryDate)} · {row.before.documentReference || 'sem documento'} · {euro.format(row.before.totalAmount)} · {STATUS_LABELS[row.before.status] ?? row.before.status}</td>
          <td><button className="button button-secondary" type="button" aria-expanded={open === row.id} onClick={() => setOpen(open === row.id ? null : row.id)}>{open === row.id ? 'Ocultar' : 'Ver versão anterior'}</button></td>
        </tr>
        {open === row.id && <tr><td colSpan={5}>
          <p className="statement-note">Diário {row.before.journalCode}{row.before.documentTypeCode ? ` · tipo ${row.before.documentTypeCode}` : ''} · {row.before.description}{row.before.documentDate ? ` · documento de ${formatMovementDate(row.before.documentDate)}` : ''}{row.before.notes ? ` · observações: ${row.before.notes}` : ''}{row.before.createdByName ? ` · criado por ${row.before.createdByName}` : ''}{row.before.createdAtUtc ? ` em ${dateTime.format(new Date(row.before.createdAtUtc))}` : ''}</p>
          <table className="dense-table"><thead><tr><th>Conta</th><th>Terceiro</th><th>IVA</th><th>C. custo</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead>
            <tbody>{row.before.lines.map((line) => <tr key={line.sequence}>
              <td>{line.accountCode} {line.accountName}</td><td>{line.partyName ?? '—'}</td><td>{line.vatCode ?? '—'}</td><td>{line.costCenter ?? '—'}</td><td>{line.description}</td>
              <td className="numeric">{line.debit ? euro.format(line.debit) : ''}</td><td className="numeric">{line.credit ? euro.format(line.credit) : ''}</td>
            </tr>)}</tbody>
          </table>
        </td></tr>}
      </Fragment>)}</tbody>
    </table></div>
  </section>
}
