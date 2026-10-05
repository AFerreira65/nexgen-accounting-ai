import { ChevronDown, ChevronRight, CircleAlert, CircleCheck, Database, Printer, Receipt } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type VatDeclaration, type VatFormField } from '../app/demo-data'
import { monthRange, periodReference, quarterRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'
import { VatSettlementEntryPanel } from './VatSettlementEntryPanel'
import { VatDeclarationFilingPanel } from './VatDeclarationFilingPanel'

const shortDate = (value: string) => value.split('-').reverse().join('/')
const seatLabels: Record<string, string> = { Continente: 'Continente', Acores: 'Açores', Madeira: 'Madeira' }
const columns = ['base', 'sujeito-passivo', 'estado', 'apuramento'] as const

function periodLabel(period: string | null) {
  if (!period) return 'Período irregular'
  return period.endsWith('T') ? `${Number(period.slice(0, 2)) / 3}.º trimestre (${period})` : `Mês ${period}`
}

export function VatDeclarationPage() {
  const { activeCompany, source, vatDeclaration } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [range, setRange] = useState(() => quarterRange(periodReference(fiscalYear)))
  const [priorCredit, setPriorCredit] = useState('')
  const [declaration, setDeclaration] = useState<VatDeclaration | null>(null)
  const [open, setOpen] = useState<Set<number>>(new Set())
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setRange(quarterRange(periodReference(fiscalYear))); setPriorCredit('') }, [fiscalYear, companyId])

  useEffect(() => {
    if (!onDatabase || !range.from || !range.to) { setDeclaration(null); return }
    const controller = new AbortController()
    setError('')
    const credit = priorCredit.trim() === '' ? null : Number(priorCredit.replace(',', '.'))
    if (credit !== null && (!Number.isFinite(credit) || credit < 0)) { setError('O excesso a reportar tem de ser um valor positivo.'); return }
    void vatDeclaration(companyId, { from: range.from, to: range.to, priorCredit: credit })
      .then((value) => { if (!controller.signal.aborted) setDeclaration(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setDeclaration(null); setError(problem instanceof Error ? problem.message : 'Não foi possível preparar a declaração.') } })
    return () => controller.abort()
  }, [vatDeclaration, companyId, onDatabase, range, priorCredit])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Declaração periódica de IVA</h1><p>O quadro 06 preenchido a partir da contabilidade, com conferência por conta.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Receipt /></span><h2>Requer ligação à base de dados</h2><p>A declaração é preparada a partir dos lançamentos da empresa.</p></section>
    </div>
  }

  const reference = periodReference(fiscalYear)
  const quarters = [0, 3, 6, 9].map((month) => quarterRange(new Date(Date.UTC(fiscalYear, month, 1))))
  const fields = declaration?.fields ?? []
  const value = (number: number) => fields.find((field) => field.number === number)?.amount ?? 0
  const toggle = (number: number) => setOpen((current) => {
    const next = new Set(current)
    if (next.has(number)) next.delete(number)
    else next.add(number)
    return next
  })
  const cell = (field: VatFormField, column: string) => (field.column === column ? (field.supported ? euro.format(field.amount) : 'à mão') : '')
  const exportCsv = () => downloadCsv(
    csvFilename(`declaracao-iva-${declaration?.period ?? 'periodo'}`, activeCompany.legalName),
    toCsv([
      ['Campo', 'Descrição', 'Coluna', 'Valor', 'Apurado pela aplicação', 'Conta', 'Nome da conta', 'Valor da conta'],
      ...fields.flatMap((field) => field.sources.length === 0
        ? [[field.number, field.label, field.column, field.amount, field.supported ? 'sim' : 'não', '', '', '']]
        : field.sources.map((item) => [field.number, field.label, field.column, field.amount, 'sim', item.accountCode, item.accountName, item.amount])),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Declaração periódica de IVA</h1><p>O quadro 06 preenchido a partir da contabilidade: cada linha vale pelo seu código de IVA ou pela classificação da conta. Abra um campo para ver as contas que o formam.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={range.from} onChange={(event) => setRange({ ...range, from: event.target.value })} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={range.to} onChange={(event) => setRange({ ...range, to: event.target.value })} /></label>
      <div className="period-shortcuts" role="group" aria-label="Períodos rápidos">
        <button type="button" className="period-chip" onClick={() => setRange(monthRange(reference))}>Mês</button>
        {quarters.map((quarter, index) => <button key={quarter.from} type="button" className={`period-chip${quarter.from === range.from && quarter.to === range.to ? ' active' : ''}`} onClick={() => setRange(quarter)}>{index + 1}.º T</button>)}
      </div>
      <label className="inline-check">Excesso a reportar do período anterior <input type="text" inputMode="decimal" aria-label="Excesso a reportar do período anterior" placeholder="do razão (2437)" value={priorCredit} onChange={(event) => setPriorCredit(event.target.value)} /></label>
      <ExportButtons run={exportCsv} disabled={!declaration} />
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!declaration}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {declaration && <>
      <section className="metadata-strip">
        <span><small>Período</small><strong>{periodLabel(declaration.period)}</strong><small>{shortDate(declaration.from)} a {shortDate(declaration.to)} · sede: {seatLabels[declaration.seat] ?? declaration.seat}</small></span>
        <span><small>A favor do sujeito passivo (91)</small><strong>{euro.format(value(91))}</strong></span>
        <span><small>A favor do Estado (92)</small><strong>{euro.format(value(92))}</strong></span>
        <span><small>{value(93) > 0 ? 'Imposto a entregar (93)' : 'Imposto a recuperar (94)'}</small><strong>{euro.format(value(93) > 0 ? value(93) : value(94))}</strong></span>
      </section>

      {declaration.difference === 0
        ? <p className="save-status success"><CircleCheck size={15} /> A declaração bate com o <Link to="/apuramento-iva">apuramento contabilístico</Link> das contas 243.</p>
        : null}
      {declaration.warnings.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>A conferir antes de entregar</h2></div></div>
        <ul className="warning-list">{declaration.warnings.map((warning) => <li key={warning}><CircleAlert size={15} /> {warning}</li>)}</ul>
      </section>}

      <section className="panel">
        <div className="panel-heading"><div><h2>Quadro 06 — Apuramento do imposto</h2><p>«À mão» assinala os campos que a aplicação não apura.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Campo</th><th>Descrição</th><th className="numeric">Base tributável</th><th className="numeric">A favor do sujeito passivo</th><th className="numeric">A favor do Estado</th><th className="numeric">Apuramento</th></tr></thead>
          <tbody>{fields.map((field) => {
            const expanded = open.has(field.number)
            return <Fragment key={field.number}>
              <tr className={field.computed && field.number >= 90 ? 'subtotal-row' : !field.supported ? 'muted-row' : undefined}>
                <td className="primary-cell">{field.number}</td>
                <td>{field.sources.length > 0
                  ? <button type="button" className="row-toggle" aria-expanded={expanded} onClick={() => toggle(field.number)}>{expanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}{field.label}</button>
                  : field.label}</td>
                {columns.map((column) => <td key={column} className="numeric">{cell(field, column)}</td>)}
              </tr>
              {expanded && field.sources.map((item) => <tr key={`${field.number}-${item.accountCode}`} className="detail-row">
                <td />
                <td><Link to={`/contas/${item.accountCode}`}>{item.accountCode}</Link> {item.accountName}</td>
                {columns.map((column) => <td key={column} className="numeric">{field.column === column ? euro.format(item.amount) : ''}</td>)}
              </tr>)}
            </Fragment>
          })}</tbody>
        </table></div>
      </section>

      <VatDeclarationFilingPanel companyId={companyId} from={range.from} to={range.to} priorCredit={priorCredit.trim() === '' ? null : Number(priorCredit.replace(',', '.'))} />
      <VatSettlementEntryPanel companyId={companyId} from={range.from} to={range.to} />

      <p className="statement-note">{declaration.disclaimer}</p>
      <p className="statement-note">Fonte dos campos: {declaration.source}</p>
    </>}
  </div>
}
