import { Database, Download, Info, Receipt } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type VatReturn } from '../app/demo-data'
import { PeriodShortcuts, periodReference, quarterRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

const ROLE_LABELS: Record<VatReturn['lines'][number]['role'], string> = {
  liquidado: 'IVA liquidado',
  dedutivel: 'IVA dedutível',
  regularizacoes: 'Regularizações',
  reporte: 'IVA a recuperar / reporte',
  outras: 'Outras contas de IVA',
}

// O apuramento é sempre de um período de declaração (mês ou trimestre) — arranca
// no trimestre da data de referência, não no exercício inteiro.
function defaultQuarter(fiscalYear: number | null | undefined) {
  if (!fiscalYear) return { from: '', to: '' }
  return quarterRange(periodReference(fiscalYear))
}

export function VatSettlementPage() {
  const { activeCompany, source, vatReturn } = useWorkContext()
  const companyId = activeCompany.companyId
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [fromDate, setFromDate] = useState(() => defaultQuarter(fiscalYear).from)
  const [toDate, setToDate] = useState(() => defaultQuarter(fiscalYear).to)
  const [result, setResult] = useState<VatReturn | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const range = defaultQuarter(fiscalYear)
    setFromDate(range.from)
    setToDate(range.to)
  }, [fiscalYear, companyId])

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setResult(null); return }
    const controller = new AbortController()
    setError('')
    void vatReturn(companyId, { from: fromDate, to: toDate })
      .then((value) => { if (!controller.signal.aborted) setResult(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o apuramento.') })
    return () => controller.abort()
  }, [companyId, onDatabase, fromDate, toDate, vatReturn])

  // Cada linha do mapa mostra o valor da rubrica e o operador com que entra no
  // apuramento (à imagem de uma declaração). O IVA dedutível pode vir negativo
  // num período em que as regularizações o superam — o operador «−» mantém-se.
  const map = useMemo(() => result ? [
    { op: '+', label: 'IVA liquidado (2433)', value: result.outputVat },
    { op: '−', label: 'IVA dedutível (2432)', value: result.inputVat },
    { op: '+', label: 'Regularizações a favor do Estado (2434)', value: result.regularisationsForState },
    { op: '−', label: 'Regularizações a favor do sujeito passivo (2434)', value: result.regularisationsForTaxpayer },
    { op: '=', label: 'Resultado do período', value: result.periodResult, strong: true },
    { op: '−', label: 'Excesso a reportar do período anterior (2437)', value: result.priorPeriodCredit },
  ] : [], [result])

  const finalRow = result
    ? result.netPayable > 0
      ? { label: 'IVA a entregar ao Estado', value: result.netPayable }
      : result.creditToCarryForward > 0
        ? { label: 'Excesso a reportar para o período seguinte', value: result.creditToCarryForward }
        : { label: 'Nada a entregar nem a reportar', value: 0 }
    : null

  const exportCsv = () => {
    if (!result) return
    const body: (string | number)[][] = [['', 'Rubrica', 'Valor']]
    for (const row of map) body.push([row.op, row.label, row.value])
    if (finalRow) body.push(['=', finalRow.label, finalRow.value])
    body.push([])
    body.push(['Conta', 'Descrição', 'Papel', 'Débito', 'Crédito', 'Saldo'])
    for (const line of result.lines) body.push([line.accountCode, line.accountName, ROLE_LABELS[line.role], line.debit, line.credit, line.net])
    downloadCsv(csvFilename('apuramento-iva', activeCompany.legalName), toCsv(body))
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Contabilidade</p><h1>Apuramento de IVA</h1><p>O apuramento periódico de IVA a partir das contas 24.3.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Receipt /></span><h2>Requer ligação ao SQL Server</h2><p>O apuramento de IVA lê os movimentos das contas 24.3 da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Apuramento de IVA</h1><p>O movimento das contas 24.3 no período, com a aritmética do fecho de IVA: liquidado, dedutível, regularizações, excesso reportado e a posição final. É o apuramento contabilístico — não substitui a declaração periódica da AT.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>
    <ReportChrome reportKey="apuramento-iva" title="Apuramento de IVA" from={fromDate} to={toDate} />

    <div className="filterbar no-print">
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={!result}><Download size={15} /> Exportar CSV</button>
    </div>

    {error && <p className="save-status error">{error}</p>}

    {result && <section className="panel">
      <div className="panel-heading"><div><h2>Apuramento do período</h2><p>{fromDate} a {toDate}.</p></div></div>
      <div className="table-wrap"><table>
        <tbody>{map.map((row) => <tr key={row.label} className={row.strong ? 'subtotal-row' : undefined}>
          <td className="numeric" aria-hidden="true">{row.op === '=' ? '' : row.op}</td>
          <td>{row.label}</td>
          <td className={`numeric ${row.strong ? 'strong' : ''}`}>{euro.format(row.value)}</td>
        </tr>)}</tbody>
        {finalRow && <tfoot><tr className="analyses-grand"><td /><td>{finalRow.label}</td><td className="numeric strong">{euro.format(finalRow.value)}</td></tr></tfoot>}
      </table></div>
      {result.priorCreditAssumed && result.priorPeriodCredit > 0 && <p className="mr-hint"><Info size={13} /> O excesso a reportar do período anterior foi assumido do saldo devedor das contas 2437; confirme-o contra a última declaração.</p>}
    </section>}

    {result && result.lines.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Contas de IVA no período</h2><p>Todas as contas 24.3 que se moveram, com o papel de cada uma.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Descrição</th><th>Papel</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{result.lines.map((line) => <tr key={line.accountCode}>
          <td><Link className="primary-cell" to={`/contas/${line.accountCode}`}>{line.accountCode}</Link></td>
          <td>{line.accountName}</td>
          <td>{ROLE_LABELS[line.role]}</td>
          <td className="numeric">{euro.format(line.debit)}</td>
          <td className="numeric">{euro.format(line.credit)}</td>
          <td className="numeric strong">{euro.format(line.net)}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {result && result.lines.length === 0 && <section className="panel"><p className="empty-state"><Info size={15} /> Sem movimento em contas de IVA (24.3) no período.</p></section>}
  </div>
}
