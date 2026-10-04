import { Database, Download, Info, Receipt, Save } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type VatReturn, type VatReturnFilingSummary, type VatReturnFilingView, type VatSummary, type VatSummaryRow } from '../app/demo-data'
import { PeriodShortcuts, periodReference, quarterRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { ReportChrome } from '../app/ReportChrome'
import { vatNatureLabel, vatRateLabel, vatRegionLabel } from '../app/vatClassification'
import { useWorkContext } from '../app/WorkContext'

const ROLE_LABELS: Record<VatReturn['lines'][number]['role'], string> = {
  liquidado: 'IVA liquidado',
  dedutivel: 'IVA dedutível',
  regularizacoes: 'Regularizações',
  reporte: 'IVA a recuperar / reporte',
  outras: 'Outras contas de IVA',
}

const KIND_LABELS: Record<VatSummaryRow['kind'], string> = {
  base: 'Base tributável',
  imposto: 'Imposto',
  outras: 'Outras',
}

const round2 = (value: number) => Math.round(value * 100) / 100

// O apuramento é sempre de um período de declaração (mês ou trimestre) — arranca
// no trimestre da data de referência, não no exercício inteiro.
function defaultQuarter(fiscalYear: number | null | undefined) {
  if (!fiscalYear) return { from: '', to: '' }
  return quarterRange(periodReference(fiscalYear))
}

export function VatSettlementPage() {
  const { activeCompany, source, savedVatReturn, saveVatReturn, vatReturnHistory, vatSummary } = useWorkContext()
  const companyId = activeCompany.companyId
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [fromDate, setFromDate] = useState(() => defaultQuarter(fiscalYear).from)
  const [toDate, setToDate] = useState(() => defaultQuarter(fiscalYear).to)
  const [view, setView] = useState<VatReturnFilingView | null>(null)
  const [prior, setPrior] = useState('0')
  const [history, setHistory] = useState<VatReturnFilingSummary[]>([])
  const [summary, setSummary] = useState<VatSummary | null>(null)
  const [summaryError, setSummaryError] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const range = defaultQuarter(fiscalYear)
    setFromDate(range.from)
    setToDate(range.to)
  }, [fiscalYear, companyId])

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setView(null); return }
    const controller = new AbortController()
    setError('')
    setStatus('')
    void savedVatReturn(companyId, { from: fromDate, to: toDate })
      .then((value) => { if (!controller.signal.aborted) { setView(value); setPrior(String(value.priorPeriodCredit)) } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o apuramento.') })
    return () => controller.abort()
  }, [companyId, onDatabase, fromDate, toDate, savedVatReturn, reload])

  useEffect(() => {
    if (!onDatabase) { setHistory([]); return }
    void vatReturnHistory(companyId).then(setHistory).catch(() => setHistory([]))
  }, [companyId, onDatabase, vatReturnHistory, reload])

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setSummary(null); return }
    const controller = new AbortController()
    setSummaryError('')
    void vatSummary(companyId, { from: fromDate, to: toDate })
      .then((value) => { if (!controller.signal.aborted) setSummary(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setSummaryError(problem instanceof Error ? problem.message : 'Não foi possível calcular o resumo de IVA.') })
    return () => controller.abort()
  }, [companyId, onDatabase, fromDate, toDate, vatSummary, reload])

  const result = view?.return ?? null
  const priorValue = Number(prior.replace(',', '.')) || 0
  const afterCarry = result ? round2(result.periodResult - priorValue) : 0
  const netPayable = afterCarry > 0 ? afterCarry : 0
  const carryForward = afterCarry < 0 ? -afterCarry : 0

  const map = useMemo(() => result ? [
    { op: '+', label: 'IVA liquidado (2433)', value: result.outputVat },
    { op: '−', label: 'IVA dedutível (2432)', value: result.inputVat },
    { op: '+', label: 'Regularizações a favor do Estado (2434)', value: result.regularisationsForState },
    { op: '−', label: 'Regularizações a favor do sujeito passivo (2434)', value: result.regularisationsForTaxpayer },
    { op: '=', label: 'Resultado do período', value: result.periodResult, strong: true },
    { op: '−', label: 'Excesso a reportar do período anterior (2437)', value: priorValue },
  ] : [], [result, priorValue])

  const finalRow = result
    ? netPayable > 0
      ? { label: 'IVA a entregar ao Estado', value: netPayable }
      : carryForward > 0
        ? { label: 'Excesso a reportar para o período seguinte', value: carryForward }
        : { label: 'Nada a entregar nem a reportar', value: 0 }
    : null

  const save = async () => {
    if (!result) return
    setBusy(true)
    setError('')
    try {
      const updated = await saveVatReturn(companyId, { from: fromDate, to: toDate }, priorValue)
      setView(updated)
      setPrior(String(updated.priorPeriodCredit))
      setStatus('Apuramento guardado.')
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar o apuramento.')
    } finally {
      setBusy(false)
    }
  }

  const exportCsv = () => {
    if (!result) return
    const body: (string | number)[][] = [['', 'Rubrica', 'Valor']]
    for (const row of map) body.push([row.op, row.label, row.value])
    if (finalRow) body.push(['=', finalRow.label, finalRow.value])
    body.push([])
    body.push(['Conta', 'Descrição', 'Papel', 'Débito', 'Crédito', 'Saldo'])
    for (const line of result.lines) body.push([line.accountCode, line.accountName, ROLE_LABELS[line.role], line.debit, line.credit, line.net])
    if (summary && summary.rows.length > 0) {
      body.push([])
      body.push(['Natureza', 'Taxa', 'Região', 'Base/Imposto', 'Campo', 'Contas', 'Débito', 'Crédito', 'Saldo'])
      for (const row of summary.rows) body.push([vatNatureLabel(row.nature), vatRateLabel(row.rate), vatRegionLabel(row.region), KIND_LABELS[row.kind], row.field?.number ?? '', row.accountCount, row.debit, row.credit, row.net])
      body.push([summary.fieldMappingDisclaimer])
    }
    if (summary && summary.unclassified.length > 0) {
      body.push([])
      body.push(['Conta por classificar', 'Descrição', 'Débito', 'Crédito', 'Saldo'])
      for (const row of summary.unclassified) body.push([row.accountCode, row.accountName, row.debit, row.credit, row.net])
    }
    downloadCsv(csvFilename('apuramento-iva', activeCompany.legalName), toCsv(body))
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Contabilidade</p><h1>Apuramento de IVA</h1><p>O apuramento periódico de IVA a partir das contas 24.3.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Receipt /></span><h2>Requer ligação à base de dados</h2><p>O apuramento de IVA lê os movimentos das contas 24.3 da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Apuramento de IVA</h1><p>O movimento das contas 24.3 no período, com a aritmética do fecho de IVA: liquidado, dedutível, regularizações, excesso reportado e a posição final. É o apuramento contabilístico — não substitui a declaração periódica da AT.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>
    <ReportChrome reportKey="apuramento-iva" title="Apuramento de IVA" from={fromDate} to={toDate} />

    <div className="filterbar no-print">
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label className="mr-hint">Excesso do período anterior
        <input type="number" step="0.01" value={prior} onChange={(event) => setPrior(event.target.value)} style={{ width: '9rem' }} />
      </label>
      <button type="button" className="button button-primary" onClick={() => void save()} disabled={!result || busy}><Save size={15} /> {busy ? 'A guardar…' : 'Guardar apuramento'}</button>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={!result}><Download size={15} /> Exportar CSV</button>
    </div>

    {error && <p className="save-status error">{error}</p>}
    {status && !error && <p className="save-status success">{status}</p>}

    {view && result && <section className="panel">
      <div className="panel-heading"><div><h2>Apuramento do período</h2><p>{fromDate} a {toDate}. {view.saved && view.savedAtUtc ? `Guardado em ${formatMovementDate(view.savedAtUtc.slice(0, 10))}.` : 'Ainda não guardado.'}</p></div></div>
      <div className="table-wrap"><table>
        <tbody>{map.map((row) => <tr key={row.label} className={row.strong ? 'subtotal-row' : undefined}>
          <td className="numeric" aria-hidden="true">{row.op === '=' ? '' : row.op}</td>
          <td>{row.label}</td>
          <td className={`numeric ${row.strong ? 'strong' : ''}`}>{euro.format(row.value)}</td>
        </tr>)}</tbody>
        {finalRow && <tfoot><tr className="analyses-grand"><td /><td>{finalRow.label}</td><td className="numeric strong">{euro.format(finalRow.value)}</td></tr></tfoot>}
      </table></div>
      {view.priorFromPreviousFiling
        ? <p className="mr-hint"><Info size={13} /> O excesso a reportar do período anterior ({euro.format(view.suggestedPriorCredit)}) foi proposto a partir do apuramento guardado do período anterior.</p>
        : result.priorCreditAssumed && priorValue > 0
          ? <p className="mr-hint"><Info size={13} /> O excesso a reportar do período anterior foi assumido do saldo devedor das contas 2437; confirme-o contra a última declaração.</p>
          : null}
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

    {summaryError && <p className="save-status error">{summaryError}</p>}

    {summary && <section className="panel">
      <div className="panel-heading"><div><h2>Resumo por taxa e natureza</h2><p>O movimento do período das linhas com código de IVA e das contas classificadas ({fromDate} a {toDate}), agrupado por natureza, taxa e região — a desagregação que se transcreve para a declaração periódica.</p></div></div>
      {summary.rows.length === 0
        ? <p className="empty-state">Sem linhas com código de IVA nem contas classificadas com movimento no período. Indique o <Link to="/codigos-iva">código de IVA</Link> nos lançamentos ou configure a <Link to="/config-iva">classificação de IVA por conta</Link>.</p>
        : <>
        <div className="table-wrap"><table>
            <thead><tr><th>Natureza</th><th>Taxa</th><th>Região</th><th>Base/Imposto</th><th>Campo</th><th className="numeric">Contas</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
            <tbody>{summary.rows.map((row) => <tr key={`${row.kind}-${row.nature}-${row.rate}-${row.region}`}>
              <td>{vatNatureLabel(row.nature)}</td>
              <td>{vatRateLabel(row.rate)}</td>
              <td>{vatRegionLabel(row.region)}</td>
              <td>{KIND_LABELS[row.kind]}</td>
              <td>{row.field ? <span title={row.field.note ?? undefined}>{row.field.number}{row.field.note ? ' *' : ''}</span> : '—'}</td>
              <td className="numeric">{row.accountCount}</td>
              <td className="numeric">{euro.format(row.debit)}</td>
              <td className="numeric">{euro.format(row.credit)}</td>
              <td className="numeric strong">{euro.format(row.net)}</td>
            </tr>)}</tbody>
          </table></div>
        <p className="mr-hint"><Info size={13} /> {summary.fieldMappingDisclaimer}</p>
        </>}
    </section>}

    {summary && summary.unclassified.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Contas por classificar</h2><p><Info size={13} /> Estas contas têm linhas no período sem código de IVA e ainda não têm classificação de IVA — essas linhas ficam de fora do resumo acima. Configure-as em <Link to="/config-iva">classificação de IVA por conta</Link> antes de transcrever o resumo.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{summary.unclassified.map((row) => <tr key={row.accountCode}>
          <td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td>
          <td>{row.accountName}</td>
          <td className="numeric">{euro.format(row.debit)}</td>
          <td className="numeric">{euro.format(row.credit)}</td>
          <td className="numeric strong">{euro.format(row.net)}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    <section className="panel no-print">
      <div className="panel-heading"><div><h2>Apuramentos guardados</h2><p>Os últimos apuramentos desta empresa.</p></div></div>
      {history.length === 0
        ? <p className="empty-state">Ainda não há apuramentos guardados.</p>
        : <div className="table-wrap"><table>
            <thead><tr><th>Período</th><th className="numeric">A entregar</th><th className="numeric">Excesso a reportar</th><th>Guardado</th><th /></tr></thead>
            <tbody>{history.map((item) => <tr key={item.id}>
              <td>{formatMovementDate(item.periodFrom)} … {formatMovementDate(item.periodTo)}</td>
              <td className="numeric">{euro.format(item.netPayable)}</td>
              <td className="numeric">{euro.format(item.creditToCarryForward)}</td>
              <td>{item.savedAtUtc ? formatMovementDate(item.savedAtUtc.slice(0, 10)) : '—'}</td>
              <td><button type="button" className="button button-secondary button-small" onClick={() => { setFromDate(item.periodFrom); setToDate(item.periodTo) }}>Abrir</button></td>
            </tr>)}</tbody>
          </table></div>}
    </section>
  </div>
}
