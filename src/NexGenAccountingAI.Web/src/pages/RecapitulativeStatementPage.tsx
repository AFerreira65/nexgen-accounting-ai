import { ArrowLeftRight, CircleAlert, Database, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type RecapitulativeStatement } from '../app/demo-data'
import { monthRange, periodReference, quarterRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const shortDate = (value: string) => value.split('-').reverse().join('/')
const whole = new Intl.NumberFormat('pt-PT', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 })
const typeLabels: Record<number, string> = {
  1: '1 — Transmissões intracomunitárias de bens',
  4: '4 — Operações triangulares',
  5: '5 — Prestações de serviços',
}

export function RecapitulativeStatementPage() {
  const { activeCompany, source, recapitulativeStatement } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [range, setRange] = useState(() => quarterRange(periodReference(fiscalYear)))
  const [statement, setStatement] = useState<RecapitulativeStatement | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setRange(quarterRange(periodReference(fiscalYear))) }, [fiscalYear, companyId])

  useEffect(() => {
    if (!onDatabase || !range.from || !range.to) { setStatement(null); return }
    const controller = new AbortController()
    setError('')
    void recapitulativeStatement(companyId, range)
      .then((value) => { if (!controller.signal.aborted) setStatement(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setStatement(null); setError(problem instanceof Error ? problem.message : 'Não foi possível preparar a declaração recapitulativa.') } })
    return () => controller.abort()
  }, [recapitulativeStatement, companyId, onDatabase, range])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Declaração recapitulativa</h1><p>As transmissões intracomunitárias de bens e as prestações de serviços a outros Estados-Membros, por adquirente.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><ArrowLeftRight /></span><h2>Requer ligação à base de dados</h2><p>A declaração é preparada a partir dos lançamentos e dos terceiros da empresa.</p></section>
    </div>
  }

  const reference = periodReference(fiscalYear)
  const quarters = [0, 3, 6, 9].map((month) => quarterRange(new Date(Date.UTC(fiscalYear, month, 1))))
  const lines = statement?.lines ?? []
  const issues = statement?.issues ?? []
  const exportCsv = () => downloadCsv(
    csvFilename(`declaracao-recapitulativa-${statement?.period ?? 'periodo'}`, activeCompany.legalName),
    toCsv([
      ['Prefixo', 'NIF do adquirente', 'Nome', 'Valor', 'Tipo de operação', 'Valor exato'],
      ...lines.map((line) => [line.prefix, line.taxNumber, line.partyName, line.value, line.operationType, line.exactValue]),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Declaração recapitulativa</h1><p>Os quadros 04 e 05: as operações com natureza de IVA intracomunitária, agregadas por adquirente e tipo, com o valor arredondado ao euro.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={range.from} onChange={(event) => setRange({ ...range, from: event.target.value })} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={range.to} onChange={(event) => setRange({ ...range, to: event.target.value })} /></label>
      <div className="period-shortcuts" role="group" aria-label="Períodos rápidos">
        <button type="button" className="period-chip" onClick={() => setRange(monthRange(reference))}>Mês</button>
        {quarters.map((quarter, index) => <button key={quarter.from} type="button" className={`period-chip${quarter.from === range.from && quarter.to === range.to ? ' active' : ''}`} onClick={() => setRange(quarter)}>{index + 1}.º T</button>)}
      </div>
      <ExportButtons run={exportCsv} disabled={lines.length === 0} />
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!statement}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {statement && <>
      {statement.period === null && <p className="save-status error"><CircleAlert size={15} /> As datas não correspondem a um mês nem a um trimestre.</p>}

      <section className="metadata-strip">
        <span><small>Campo 10 — bens (tipo 1)</small><strong>{whole.format(statement.field10)}</strong></span>
        <span><small>Campo 17 — triangulares (tipo 4)</small><strong>{whole.format(statement.field17)}</strong></span>
        <span><small>Campo 18 — serviços (tipo 5)</small><strong>{whole.format(statement.field18)}</strong></span>
        <span><small>Campo 19 — a coincidir com o campo 7 da declaração periódica</small><strong>{whole.format(statement.field19)}</strong><small>{shortDate(statement.from)} a {shortDate(statement.to)}</small></span>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Quadro 04 — Operações intracomunitárias</h2><p>Uma linha por adquirente e tipo de operação. Confirme os números no VIES antes de entregar.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Prefixo</th><th>NIF do adquirente</th><th>Nome</th><th className="numeric">Valor</th><th>Tipo de operação</th></tr></thead>
          <tbody>{lines.map((line) => <tr key={`${line.prefix}-${line.taxNumber}-${line.operationType}`}>
            <td className="primary-cell">{line.prefix}</td>
            <td>{line.taxNumber}</td>
            <td>{line.partyName}</td>
            <td className="numeric strong" title={`Antes do arredondamento: ${euro.format(line.exactValue)}`}>{whole.format(line.value)}</td>
            <td>{typeLabels[line.operationType]}</td>
          </tr>)}</tbody>
        </table></div>
        {lines.length === 0 && <p className="empty-state">Sem operações intracomunitárias no período. Só entram as linhas com um código de IVA (ou uma conta classificada) de transmissões intracomunitárias de bens, operações triangulares ou prestações de serviços intracomunitárias — veja os <Link to="/codigos-iva">códigos de IVA</Link>.</p>}
      </section>

      {issues.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>Movimentos que ficaram de fora</h2><p>Corrija o terceiro ou o código de IVA do lançamento para entrarem na declaração.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Data</th><th>Lançamento</th><th>Conta</th><th className="numeric">Valor</th><th>Motivo</th></tr></thead>
          <tbody>{issues.map((issue, index) => <tr key={`${issue.entryReference}-${issue.accountCode}-${index}`}>
            <td>{shortDate(issue.entryDate)}</td>
            <td>{issue.entryReference}</td>
            <td><Link to={`/contas/${issue.accountCode}`}>{issue.accountCode}</Link></td>
            <td className="numeric">{euro.format(issue.amount)}</td>
            <td>{issue.reason}</td>
          </tr>)}</tbody>
        </table></div>
      </section>}

      <p className="statement-note">{statement.disclaimer}</p>
      <p className="statement-note">Fonte: {statement.source}</p>
    </>}
  </div>
}
