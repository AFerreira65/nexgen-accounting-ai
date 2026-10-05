import { Database, Hourglass, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Chart } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type PartyAging } from '../app/demo-data'
import { periodReference } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

type Kind = 'clientes' | 'fornecedores'

const shortDate = (value: string | null) => (value ? value.split('-').reverse().join('/') : '—')
const kindLabels: Record<Kind, { tab: string; open: string; advance: string }> = {
  clientes: { tab: 'Clientes', open: 'A receber', advance: 'Adiantamentos de clientes' },
  fornecedores: { tab: 'Fornecedores', open: 'A pagar', advance: 'Adiantamentos a fornecedores' },
}

export function PartyAgingPage() {
  const { activeCompany, source, partyAging } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [kind, setKind] = useState<Kind>('clientes')
  const [date, setDate] = useState(() => periodReference(fiscalYear).toISOString().slice(0, 10))
  const [aging, setAging] = useState<PartyAging | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setDate(periodReference(fiscalYear).toISOString().slice(0, 10)) }, [fiscalYear, companyId])

  useEffect(() => {
    if (!onDatabase || !date) { setAging(null); return }
    const controller = new AbortController()
    setError('')
    void partyAging(companyId, { date, kind })
      .then((value) => { if (!controller.signal.aborted) setAging(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setAging(null); setError(problem instanceof Error ? problem.message : 'Não foi possível calcular a idade dos saldos.') } })
    return () => controller.abort()
  }, [partyAging, companyId, onDatabase, date, kind])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Idade de saldos</h1><p>O que está por receber de clientes e por pagar a fornecedores, pela antiguidade dos documentos.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Hourglass /></span><h2>Requer ligação à base de dados</h2><p>A idade dos saldos é calculada sobre os lançamentos com terceiro da empresa.</p></section>
    </div>
  }

  const labels = kindLabels[kind]
  const rows = aging?.rows ?? []
  const buckets = aging?.bucketLabels ?? []
  const exportCsv = () => downloadCsv(
    csvFilename(`idade-saldos-${kind}`, activeCompany.legalName),
    toCsv([
      ['NIF', 'Nome', labels.open, ...buckets, labels.advance, 'Documento mais antigo'],
      ...rows.map((row) => [row.taxRegistrationNumber, row.name, row.open, ...row.buckets, row.advance, row.oldestDate ?? '']),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Idade de saldos</h1><p>O saldo em aberto de cada terceiro a uma data, repartido pela antiguidade dos documentos. Os pagamentos abatem aos documentos mais antigos.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <div className="period-shortcuts" role="tablist" aria-label="Tipo de terceiro">
        {(Object.keys(kindLabels) as Kind[]).map((item) => <button key={item} type="button" role="tab" aria-selected={kind === item} className={`period-chip${kind === item ? ' active' : ''}`} onClick={() => setKind(item)}>{kindLabels[item].tab}</button>)}
      </div>
      <label className="inline-check">À data de <input type="date" aria-label="Data" value={date} onChange={(event) => setDate(event.target.value)} /></label>
      <ExportButtons run={exportCsv} disabled={rows.length === 0} />
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!aging}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {aging && rows.length === 0 && <section className="panel empty-state"><h2>Sem saldos em aberto</h2><p>Não há lançamentos com terceiro nas contas {kind === 'clientes' ? '21' : '22'} até {shortDate(aging.date)}.</p></section>}

    {aging && rows.length > 0 && <>
      <section className="metadata-strip">
        <span><small>{labels.open} em {shortDate(aging.date)}</small><strong>{euro.format(aging.totalOpen)}</strong></span>
        <span><small>Com mais de 90 dias</small><strong>{euro.format(aging.totalBuckets[3] + aging.totalBuckets[4])}</strong></span>
        <span><small>{labels.advance}</small><strong>{euro.format(aging.totalAdvance)}</strong></span>
        <span><small>Terceiros</small><strong>{rows.length}</strong></span>
      </section>

      <section className="panel chart-panel">
        <div className="panel-heading"><div><h2>{labels.open} por antiguidade</h2></div></div>
        <Chart title={`${labels.open} por antiguidade em ${shortDate(aging.date)}`} labels={buckets} format={euro.format} height={220} series={[
          { name: labels.open, kind: 'bar', tone: kind === 'clientes' ? 'primary' : 'warning', values: aging.totalBuckets },
        ]} />
      </section>

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>Terceiro</th><th className="numeric">{labels.open}</th>{buckets.map((bucket) => <th key={bucket} className="numeric">{bucket}</th>)}<th className="numeric">{labels.advance}</th><th>Mais antigo</th></tr></thead>
          <tbody>
            {rows.map((row) => <tr key={row.partyId}>
              <td><Link to={`/terceiros/${row.partyId}`}>{row.name}</Link><br /><small>{row.countryCode === 'PT' ? '' : `${row.countryCode} `}{row.taxRegistrationNumber}</small></td>
              <td className="numeric strong">{euro.format(row.open)}</td>
              {row.buckets.map((amount, index) => <td key={buckets[index]} className="numeric">{amount === 0 ? '—' : euro.format(amount)}</td>)}
              <td className="numeric">{row.advance === 0 ? '—' : euro.format(row.advance)}</td>
              <td>{shortDate(row.oldestDate)}</td>
            </tr>)}
            <tr className="subtotal-row">
              <td>Total</td>
              <td className="numeric">{euro.format(aging.totalOpen)}</td>
              {aging.totalBuckets.map((amount, index) => <td key={buckets[index]} className="numeric">{euro.format(amount)}</td>)}
              <td className="numeric">{euro.format(aging.totalAdvance)}</td>
              <td />
            </tr>
          </tbody>
        </table></div>
      </section>

      <p className="statement-note">{aging.disclaimer}</p>
    </>}
  </div>
}
