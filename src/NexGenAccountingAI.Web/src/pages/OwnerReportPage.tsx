import { CircleAlert, Database, Info, Newspaper, Printer, Sparkles } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { BarList, Chart } from '../app/charts'
import { euro, type OwnerReport, type OwnerReportFigure } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']
const monthNames = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

function change(figure: OwnerReportFigure) {
  if (figure.comparative === null || figure.comparative === 0) return null
  const ratio = (figure.value - figure.comparative) / Math.abs(figure.comparative)
  return `${ratio >= 0 ? '+' : ''}${(ratio * 100).toLocaleString('pt-PT', { maximumFractionDigits: 1 })} %`
}

function Figures({ items }: { items: OwnerReportFigure[] }) {
  return <section className="metadata-strip">{items.map((figure) => <span key={figure.label}>
    <small>{figure.label}</small>
    <strong>{euro.format(figure.value)}</strong>
    {figure.comparative !== null && <small>homólogo {euro.format(figure.comparative)}{change(figure) ? ` (${change(figure)})` : ''}</small>}
    {figure.note && <small>{figure.note}</small>}
  </span>)}</section>
}

export function OwnerReportPage() {
  const { activeCompany, source, ownerReport } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const today = new Date()
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? today.getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [month, setMonth] = useState(defaultYear === today.getUTCFullYear() ? Math.max(1, today.getUTCMonth()) : 12)
  const [report, setReport] = useState<OwnerReport | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void ownerReport(companyId, year, month)
      .then((value) => { if (!controller.signal.aborted) setReport(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível montar o relatório.') } })
    return () => controller.abort()
  }, [companyId, month, onDatabase, ownerReport, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Relatório do mês</h1><p>O mês da empresa em números e em texto simples, para o empresário.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Newspaper /></span><h2>Requer ligação à base de dados</h2><p>O relatório é montado a partir dos lançamentos da empresa.</p></section>
    </div>
  }

  const months = report?.evolution.months.filter((item) => item.month <= month) ?? []
  const pad = <T,>(values: T[]) => [...values, ...Array<null>(12 - values.length).fill(null)]

  return <div className="page-stack owner-report">
    <header className="page-heading"><div><p className="eyebrow">Gestão · {activeCompany.legalName}</p><h1>Relatório do mês</h1><p>{report ? report.monthLabel : 'O mês da empresa'} em números e em texto simples: vendas, gastos e resultado contra o mesmo período do ano anterior, a tesouraria, quem deve e a quem se deve, e o que merece atenção.</p></div>
      <div className="heading-actions no-print"><button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!report}><Printer size={16} /> Imprimir / PDF</button></div>
    </header>

    <div className="filterbar no-print">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <label className="inline-check">Mês <select aria-label="Mês" value={month} onChange={(event) => setMonth(Number(event.target.value))}>{monthNames.map((label, index) => <option key={label} value={index + 1}>{label}</option>)}</select></label>
      <Link className="text-link" to="/relatorios-gestao"><Sparkles size={15} /> Síntese com IA nos relatórios de gestão</Link>
    </div>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    {report && <>
      <section className="panel">
        <div className="panel-heading"><div><h2>{report.monthLabel}</h2></div></div>
        <ul className="owner-highlights">{report.highlights.map((text) => <li key={text}>{text}</li>)}</ul>
      </section>

      {report.alerts.length > 0 && <section className="panel attention-panel">
        <div className="panel-heading"><div><h2>A ter em atenção</h2></div><span className="attention-count">{report.alerts.length}</span></div>
        <ul className="owner-alerts">{report.alerts.map((alert) => <li key={alert.text} className={alert.severity}>{alert.severity === 'aviso' ? <CircleAlert size={15} /> : <Info size={15} />} {alert.text}</li>)}</ul>
      </section>}

      <h2 className="section-title">O mês</h2>
      <Figures items={report.monthFigures} />
      <h2 className="section-title">O ano até {monthNames[month - 1].toLowerCase()}</h2>
      <Figures items={report.yearFigures} />
      <h2 className="section-title">A posição no fim do mês</h2>
      <Figures items={report.position} />

      <div className="chart-grid-layout">
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Rendimentos por mês</h2><p>{year} contra {year - 1}.</p></div></div>
          <Chart title={`Rendimentos por mês em ${year} e em ${year - 1}`} labels={monthLabels} format={euro.format} series={[
            { name: String(year), kind: 'bar', tone: 'primary', values: pad(months.map((item) => item.income)) },
            { name: String(year - 1), kind: 'line', tone: 'muted', dashed: true, values: report.evolution.months.map((item) => item.previousIncome) },
          ]} />
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Resultado e tesouraria</h2><p>Resultado de cada mês e saldo de caixa e bancos no fim do mês.</p></div></div>
          <Chart title={`Resultado e tesouraria em ${year}`} labels={monthLabels} format={euro.format} series={[
            { name: 'Resultado', kind: 'bar', tone: 'accent', values: pad(months.map((item) => item.result)) },
            { name: 'Tesouraria', kind: 'line', tone: 'primary', values: pad(months.map((item) => item.cash)) },
          ]} />
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Clientes por receber</h2><p>Pela antiguidade dos documentos.</p></div></div>
          <BarList title="Idade dos saldos de clientes" format={euro.format} items={report.receivables.bucketLabels.map((label, index) => ({ label, amount: report.receivables.totalBuckets[index], share: report.receivables.totalOpen === 0 ? 0 : (report.receivables.totalBuckets[index] / report.receivables.totalOpen) * 100 }))} />
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Fornecedores por pagar</h2><p>Pela antiguidade dos documentos.</p></div></div>
          <BarList title="Idade dos saldos de fornecedores" tone="danger" format={euro.format} items={report.payables.bucketLabels.map((label, index) => ({ label, amount: report.payables.totalBuckets[index], share: report.payables.totalOpen === 0 ? 0 : (report.payables.totalBuckets[index] / report.payables.totalOpen) * 100 }))} />
        </section>
      </div>

      <p className="statement-note">Montado a partir dos lançamentos, sem IA: os números vêm da evolução mensal, da idade dos saldos e do orçamento. Os alertas são critérios da aplicação — prejuízo no mês, rendimentos mais de 20 % abaixo do homólogo, tesouraria abaixo de um mês de gastos médios, clientes com mais de 90 dias, desvios desfavoráveis de 10 % ou mais no orçamento — e não referências setoriais. {report.receivables.disclaimer}</p>
    </>}
  </div>
}
