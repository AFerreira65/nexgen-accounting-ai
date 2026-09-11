import { Calculator, Database, Download, Info, Plus, Save, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type IrcAdjustment, type IrcEstimate, type IrcEstimateFilingSummary } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'

export function IrcEstimatePage() {
  const { activeCompany, source, ircEstimate, savedIrcEstimate, saveIrcEstimate, ircEstimateHistory } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, activeCompany.companyId)
  const [taxRatePercent, setTaxRatePercent] = useState('21')
  const [adjustments, setAdjustments] = useState<IrcAdjustment[]>([])
  const [result, setResult] = useState<IrcEstimate | null>(null)
  const [savedAtUtc, setSavedAtUtc] = useState<string | null>(null)
  const [history, setHistory] = useState<IrcEstimateFilingSummary[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [reload, setReload] = useState(0)

  const taxRate = (Number(taxRatePercent.replace(',', '.')) || 0) / 100

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { return }
    const controller = new AbortController()
    setError('')
    void savedIrcEstimate(companyId, { from: fromDate, to: toDate })
      .then((view) => {
        if (controller.signal.aborted) return
        if (view.saved) {
          setTaxRatePercent(String((view.taxRate ?? 0.21) * 100))
          setAdjustments(view.adjustments)
          setResult(view.estimate)
          setSavedAtUtc(view.savedAtUtc)
        } else {
          setSavedAtUtc(null)
        }
      })
      .catch(() => { if (!controller.signal.aborted) setSavedAtUtc(null) })
    return () => controller.abort()
  }, [companyId, onDatabase, fromDate, toDate, savedIrcEstimate, reload])

  useEffect(() => {
    if (!onDatabase) { setHistory([]); return }
    void ircEstimateHistory(companyId).then(setHistory).catch(() => setHistory([]))
  }, [companyId, onDatabase, ircEstimateHistory, reload])

  const addAdjustment = () => setAdjustments((current) => [...current, { description: '', amount: 0 }])
  const removeAdjustment = (index: number) => setAdjustments((current) => current.filter((_, i) => i !== index))
  const patchAdjustment = (index: number, patch: Partial<IrcAdjustment>) =>
    setAdjustments((current) => current.map((item, i) => i === index ? { ...item, ...patch } : item))

  const validPeriodAndRate = () => {
    if (!fromDate || !toDate) { setError('Indique a data inicial e a data final do período.'); return false }
    if (taxRate <= 0 || taxRate > 1) { setError('Indique uma taxa entre 0% e 100%.'); return false }
    return true
  }

  const calculate = async () => {
    if (!validPeriodAndRate()) return
    setBusy(true); setError(''); setStatus('')
    try {
      const value = await ircEstimate(companyId, { from: fromDate, to: toDate }, { taxRate, adjustments: adjustments.filter((item) => item.description.trim() || item.amount !== 0) })
      setResult(value)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível calcular a estimativa.')
      setResult(null)
    } finally {
      setBusy(false)
    }
  }

  const save = async () => {
    if (!validPeriodAndRate()) return
    setBusy(true); setError(''); setStatus('')
    try {
      const view = await saveIrcEstimate(companyId, { from: fromDate, to: toDate }, { taxRate, adjustments: adjustments.filter((item) => item.description.trim() || item.amount !== 0) })
      setResult(view.estimate)
      setSavedAtUtc(view.savedAtUtc)
      setStatus('Estimativa guardada.')
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a estimativa.')
    } finally {
      setBusy(false)
    }
  }

  const exportCsv = () => {
    if (!result) return
    const body: (string | number)[][] = [['Rubrica', 'Valor']]
    body.push(['Resultado líquido do período', result.netIncome])
    for (const adjustment of result.adjustments) body.push([adjustment.description || 'Correção', adjustment.amount])
    body.push(['Total das correções', result.adjustmentsTotal])
    body.push(['Resultado fiscal', result.taxableResult])
    body.push([`Taxa (${(result.taxRate * 100).toFixed(2)}%)`, ''])
    body.push(['Coleta estimada', result.estimatedTax])
    downloadCsv(csvFilename('estimativa-irc', activeCompany.legalName), toCsv(body))
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Contabilidade</p><h1>Estimativa de IRC</h1><p>A estimativa do IRC de um período a partir do resultado contabilístico.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Calculator /></span><h2>Requer ligação ao SQL Server</h2><p>A estimativa de IRC lê o resultado do período da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Estimativa de IRC</h1><p>Resultado líquido do período (classes 6/7) mais as correções fiscais que introduzir, vezes a taxa que indicar. Não é o Modelo 22 nem substitui o cálculo oficial — introduza as correções e confirme a taxa aplicável.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>
    <ReportChrome reportKey="estimativa-irc" title="Estimativa de IRC" from={fromDate} to={toDate} />

    <div className="filterbar no-print">
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <label className="mr-hint">Taxa de IRC (%)
        <input type="number" step="0.01" min="0" max="100" value={taxRatePercent} onChange={(event) => setTaxRatePercent(event.target.value)} style={{ width: '7rem' }} />
      </label>
      <button type="button" className="button button-secondary" onClick={() => void calculate()} disabled={busy}><Calculator size={15} /> {busy ? 'A calcular…' : 'Calcular'}</button>
      <button type="button" className="button button-primary" onClick={() => void save()} disabled={busy}><Save size={15} /> Guardar estimativa</button>
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={!result}><Download size={15} /> Exportar CSV</button>
    </div>

    <p className="mr-hint no-print"><Info size={13} /> A taxa sugerida (21%) é apenas um ponto de partida — confirme a taxa aplicável ao regime e à região da empresa antes de usar o resultado.</p>

    {error && <p className="save-status error">{error}</p>}
    {status && !error && <p className="save-status success">{status}</p>}

    <fieldset className="builder-section no-print">
      <legend>Correções fiscais</legend>
      {adjustments.map((adjustment, index) => <div key={index} className="builder-row">
        <input value={adjustment.description} onChange={(event) => patchAdjustment(index, { description: event.target.value })} placeholder="Descrição (ex.: tributação autónoma)" style={{ flex: 2 }} />
        <input type="number" step="0.01" value={adjustment.amount} onChange={(event) => patchAdjustment(index, { amount: Number(event.target.value) || 0 })} placeholder="Valor" />
        <button className="icon-button" type="button" aria-label="Remover correção" onClick={() => removeAdjustment(index)}><Trash2 size={15} /></button>
      </div>)}
      <button className="button button-secondary" type="button" onClick={addAdjustment}><Plus size={14} /> Correção</button>
    </fieldset>

    {result && <section className="panel">
      <div className="panel-heading"><div><h2>Estimativa do período</h2><p>{result.from} a {result.to}. {savedAtUtc ? `Guardada em ${formatMovementDate(savedAtUtc.slice(0, 10))}.` : 'Ainda não guardada.'}</p></div></div>
      <div className="table-wrap"><table>
        <tbody>
          <tr><td>Resultado líquido do período</td><td className="numeric">{euro.format(result.netIncome)}</td></tr>
          {result.adjustments.map((adjustment, index) => <tr key={index}>
            <td>{adjustment.description || 'Correção'}</td>
            <td className="numeric">{euro.format(adjustment.amount)}</td>
          </tr>)}
          <tr className="subtotal-row"><td>Resultado fiscal</td><td className="numeric strong">{euro.format(result.taxableResult)}</td></tr>
          <tr><td>Taxa aplicada</td><td className="numeric">{(result.taxRate * 100).toFixed(2)}%</td></tr>
        </tbody>
        <tfoot><tr className="analyses-grand"><td>Coleta estimada</td><td className="numeric strong">{euro.format(result.estimatedTax)}</td></tr></tfoot>
      </table></div>
      {result.taxableResult <= 0 && <p className="mr-hint"><Info size={13} /> Resultado fiscal não positivo — sem coleta nesta estimativa. O reporte de prejuízos fiscais fica fora deste apoio.</p>}
    </section>}

    {!result && !error && <section className="panel"><p className="empty-state">Indique o período, a taxa e as correções, e clique em «Calcular» ou «Guardar estimativa».</p></section>}

    <section className="panel no-print">
      <div className="panel-heading"><div><h2>Estimativas guardadas</h2><p>As últimas estimativas desta empresa.</p></div></div>
      {history.length === 0
        ? <p className="empty-state">Ainda não há estimativas guardadas.</p>
        : <div className="table-wrap"><table>
            <thead><tr><th>Período</th><th className="numeric">Taxa</th><th className="numeric">Coleta estimada</th><th>Guardada</th><th /></tr></thead>
            <tbody>{history.map((item) => <tr key={item.id}>
              <td>{formatMovementDate(item.periodFrom)} … {formatMovementDate(item.periodTo)}</td>
              <td className="numeric">{(item.taxRate * 100).toFixed(2)}%</td>
              <td className="numeric">{euro.format(item.estimatedTax)}</td>
              <td>{item.savedAtUtc ? formatMovementDate(item.savedAtUtc.slice(0, 10)) : '—'}</td>
              <td><button type="button" className="button button-secondary button-small" onClick={() => { setFromDate(item.periodFrom); setToDate(item.periodTo) }}>Abrir</button></td>
            </tr>)}</tbody>
          </table></div>}
    </section>
  </div>
}
