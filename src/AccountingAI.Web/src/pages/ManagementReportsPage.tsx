import { Database, Download, Printer, Sparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { formatMovementDate, type AiSettings, type ManagementReportCatalogEntry, type ManagementReportView } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

function kindLabel(catalog: ManagementReportCatalogEntry[], key: string) {
  if (key === 'pergunta') return 'Pergunta livre'
  return catalog.find((entry) => entry.key === key)?.label ?? key
}

function referenceLink(type: string, code: string) {
  return type === 'movimento' ? `/movimentos/${encodeURIComponent(code)}` : `/contas/${encodeURIComponent(code)}`
}

export function ManagementReportsPage() {
  const { activeCompany, source, managementReportCatalog, aiSettings, saveAiSettings, generateManagementReport, managementReports, managementReport } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [catalog, setCatalog] = useState<ManagementReportCatalogEntry[]>([])
  const [settings, setSettings] = useState<AiSettings | null>(null)
  const [history, setHistory] = useState<ManagementReportView[]>([])
  const [mode, setMode] = useState<'kind' | 'question'>('kind')
  const [kind, setKind] = useState('sintese-mensal')
  const [question, setQuestion] = useState('')
  const [report, setReport] = useState<ManagementReportView | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)

  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, companyId)

  useEffect(() => {
    void managementReportCatalog().then((entries) => {
      setCatalog(entries)
      if (entries.length > 0) setKind((current) => entries.some((entry) => entry.key === current) ? current : entries[0].key)
    }).catch(() => setCatalog([]))
  }, [managementReportCatalog])

  useEffect(() => {
    if (!onDatabase) { setSettings(null); setHistory([]); return }
    void aiSettings(companyId).then(setSettings).catch(() => setSettings(null))
    void managementReports(companyId).then(setHistory).catch(() => setHistory([]))
  }, [companyId, onDatabase, aiSettings, managementReports, reload])

  const canGenerate = onDatabase && Boolean(settings?.enabled) && Boolean(settings?.narratorConfigured) && Boolean(fromDate) && Boolean(toDate)
    && (mode === 'kind' ? Boolean(kind) : question.trim().length > 0)

  const toggle = async (patch: Partial<Pick<AiSettings, 'enabled' | 'autoMonthly'>>) => {
    if (!settings) return
    try {
      const next = await saveAiSettings(companyId, { enabled: patch.enabled ?? settings.enabled, autoMonthly: patch.autoMonthly ?? settings.autoMonthly })
      setSettings(next)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar as definições.')
    }
  }

  const generate = async () => {
    if (!canGenerate) return
    setBusy(true)
    setError('')
    try {
      const target = mode === 'kind' ? { kind } : { question: question.trim() }
      const generated = await generateManagementReport(companyId, target, { from: fromDate, to: toDate })
      setReport(generated)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o relatório.')
    } finally {
      setBusy(false)
    }
  }

  const openFromHistory = async (id: string) => {
    try {
      setReport(await managementReport(companyId, id))
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível abrir o relatório.')
    }
  }

  const result = report?.result ?? null

  const exportIndicators = () => {
    if (!result) return
    const body: (string | number)[][] = [['Indicador', 'Valor', 'Comentário']]
    for (const indicator of result.indicators) body.push([indicator.name, indicator.value, indicator.comment ?? ''])
    downloadCsv(csvFilename('relatorio-gestao-indicadores', activeCompany.legalName), toCsv(body))
  }

  const selectedKind = useMemo(() => catalog.find((entry) => entry.key === kind), [catalog, kind])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Análise</p><h1>Relatórios de gestão</h1><p>Sínteses redigidas por IA sobre os dados já reconciliados do período.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Sparkles /></span><h2>Requer ligação ao SQL Server</h2><p>Os relatórios de gestão com IA leem o balancete, o cruzamento e os movimentos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack report-page">
    <header className="page-heading"><div><p className="eyebrow">Análise</p><h1>Relatórios de gestão</h1><p>Sínteses redigidas por IA sobre os dados já reconciliados do período — sempre com ligação à conta ou ao lançamento de origem. Um apoio à análise; não substitui a revisão.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <section className="panel no-print">
      <div className="panel-heading"><div><h2>Configuração</h2><p>O interruptor da funcionalidade nesta empresa.</p></div></div>
      <div className="builder-row">
        <label className="toggle-inline">
          <input type="checkbox" checked={settings?.enabled ?? false} disabled={!settings} onChange={(event) => void toggle({ enabled: event.target.checked })} />
          Relatórios de gestão com IA ativos
        </label>
        <label className="toggle-inline">
          <input type="checkbox" checked={settings?.autoMonthly ?? false} disabled={!settings || !settings.enabled} onChange={(event) => void toggle({ autoMonthly: event.target.checked })} />
          Gerar a síntese mensal automaticamente
        </label>
      </div>
      {settings && !settings.narratorConfigured && <p className="mr-hint">Não há chave de API de IA configurada no servidor — a geração fica indisponível até um administrador a definir.</p>}
      {settings?.narratorConfigured && <p className="mr-hint">Modelo: {settings.model}.</p>}
    </section>

    <section className="panel no-print">
      <div className="panel-heading"><div><h2>Gerar</h2><p>Um tipo pré-definido ou uma pergunta livre, para um período.</p></div></div>
      <div className="builder-row">
        <label className="toggle-inline"><input type="radio" name="mr-mode" checked={mode === 'kind'} onChange={() => setMode('kind')} /> Tipo pré-definido</label>
        <label className="toggle-inline"><input type="radio" name="mr-mode" checked={mode === 'question'} onChange={() => setMode('question')} /> Pergunta livre</label>
      </div>
      {mode === 'kind'
        ? <div className="builder-row">
            <label>Tipo
              <select value={kind} onChange={(event) => setKind(event.target.value)}>
                {catalog.map((entry) => <option key={entry.key} value={entry.key}>{entry.label}</option>)}
              </select>
            </label>
            {selectedKind && <span className="mr-hint">{selectedKind.description}</span>}
          </div>
        : <div className="builder-row">
            <label className="mr-grow">Pergunta
              <textarea value={question} onChange={(event) => setQuestion(event.target.value)} rows={2} placeholder="Ex.: Como evoluiu a margem face ao ano passado?" />
            </label>
          </div>}
      <div className="builder-row">
        <label>De <input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
        <label>A <input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
        <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
        <button type="button" className="button button-primary" onClick={() => void generate()} disabled={!canGenerate || busy}>
          <Sparkles size={15} /> {busy ? 'A gerar…' : 'Gerar'}
        </button>
      </div>
      {!settings?.enabled && <p className="mr-hint">Ative os relatórios com IA acima para gerar.</p>}
    </section>

    {result && <section className="panel management-report">
      <div className="panel-heading">
        <div><h2>{report ? kindLabel(catalog, report.kind) : ''}</h2><p>{report?.question ?? ''} {report?.createdAtUtc ? `· ${formatMovementDate(report.createdAtUtc.slice(0, 10))}` : ''} {report?.model ? `· ${report.model}` : ''}</p></div>
        <div className="mr-actions no-print">
          <button type="button" className="button button-secondary" onClick={() => window.print()}><Printer size={15} /> Imprimir / PDF</button>
          {result.indicators.length > 0 && <button type="button" className="button button-secondary" onClick={exportIndicators}><Download size={15} /> Indicadores CSV</button>}
        </div>
      </div>

      <p className="management-report-summary">{result.summary}</p>

      {result.sections.map((section, index) => <article key={index} className="management-report-section">
        <h3>{section.title}</h3>
        <p>{section.body}</p>
        {section.references.length > 0 && <p className="management-report-refs">
          Origem: {section.references.map((reference, refIndex) => <span key={refIndex}>
            {refIndex > 0 && ', '}
            <Link to={referenceLink(reference.type, reference.code)}>{reference.type === 'movimento' ? 'movimento' : 'conta'} {reference.code}</Link>
          </span>)}
        </p>}
      </article>)}

      {result.alerts.length > 0 && <div className="management-report-alerts">
        <h3>Alertas</h3>
        <ul>{result.alerts.map((alert, index) => <li key={index}>{alert}</li>)}</ul>
      </div>}

      {result.indicators.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th>Indicador</th><th>Valor</th><th>Comentário</th></tr></thead>
        <tbody>{result.indicators.map((indicator, index) => <tr key={index}>
          <td>{indicator.name}</td><td className="numeric">{indicator.value}</td><td>{indicator.comment ?? ''}</td>
        </tr>)}</tbody>
      </table></div>}

      <p className="management-report-notice">{result.aiNotice}</p>
    </section>}

    <section className="panel no-print">
      <div className="panel-heading"><div><h2>Histórico</h2><p>Os últimos relatórios gerados nesta empresa.</p></div></div>
      {history.length === 0
        ? <p className="empty-state">Ainda não há relatórios gerados.</p>
        : <div className="table-wrap"><table>
            <thead><tr><th>Tipo</th><th>Período</th><th>Data</th><th>Modelo</th><th /></tr></thead>
            <tbody>{history.map((item) => <tr key={item.id}>
              <td>{kindLabel(catalog, item.kind)}{item.question ? ` — ${item.question}` : ''}</td>
              <td>{formatMovementDate(item.periodFrom)} … {formatMovementDate(item.periodTo)}</td>
              <td>{item.createdAtUtc ? formatMovementDate(item.createdAtUtc.slice(0, 10)) : '—'}</td>
              <td>{item.model}</td>
              <td><button type="button" className="button button-secondary button-small" onClick={() => void openFromHistory(item.id)}>Abrir</button></td>
            </tr>)}</tbody>
          </table></div>}
    </section>
  </div>
}
