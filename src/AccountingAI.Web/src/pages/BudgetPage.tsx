import { CircleAlert, Database, Download, Printer, Target, Trash2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Chart } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type BudgetReport } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
const percent = (value: number | null) => (value === null ? '—' : `${value > 0 ? '+' : ''}${value.toLocaleString('pt-PT', { maximumFractionDigits: 1 })} %`)

export function BudgetPage() {
  const { activeCompany, source, budgetReport, setBudgetLine } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [month, setMonth] = useState(12)
  const [report, setReport] = useState<BudgetReport | null>(null)
  const [accountCode, setAccountCode] = useState('')
  const [costCenterCode, setCostCenterCode] = useState('')
  const [annualAmount, setAnnualAmount] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void budgetReport(companyId, year, month)
      .then((value) => { if (!controller.signal.aborted) setReport(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o orçamento.') } })
    return () => controller.abort()
  }, [budgetReport, companyId, onDatabase, year, month])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Orçamento</h1><p>O previsto por conta de gastos e de rendimentos, comparado com o realizado.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Target /></span><h2>Requer ligação ao SQL Server</h2><p>O orçamento é guardado por empresa e por exercício.</p></section>
    </div>
  }

  async function save(input: { accountCode: string; costCenterCode: string | null; annualAmount: number }) {
    setSaving(true)
    setError('')
    try {
      setReport(await setBudgetLine(companyId, year, month, input))
      return true
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a linha do orçamento.')
      return false
    } finally {
      setSaving(false)
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const amount = Number(annualAmount.replace(/\s/g, '').replace(',', '.'))
    if (!Number.isFinite(amount) || amount <= 0) { setError('Indique um valor anual positivo.'); return }
    if (await save({ accountCode: accountCode.trim(), costCenterCode: costCenterCode.trim() || null, annualAmount: amount })) {
      setAccountCode('')
      setCostCenterCode('')
      setAnnualAmount('')
    }
  }

  const lines = report?.lines ?? []
  const general = lines.filter((line) => line.costCenterCode === null)
  const exportCsv = () => downloadCsv(
    csvFilename(`orcamento-${year}`, activeCompany.legalName),
    toCsv([
      ['Conta', 'Nome', 'Centro de custo', 'Tipo', 'Orçamento anual', `Orçamento até ${monthLabels[month - 1]}`, 'Realizado', 'Desvio', 'Desvio (%)'],
      ...lines.map((line) => [line.accountCode, line.accountName, line.costCenterCode ?? '', line.kind, line.annualBudget, line.budget, line.actual, line.deviation, line.deviationPercent ?? '']),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Orçamento</h1><p>O valor anual previsto por conta de gastos ou de rendimentos — uma conta ou o grupo que a agrega, e opcionalmente um <Link to="/centros-custo">centro de custo</Link> — repartido por doze meses iguais e comparado com o realizado.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <label className="inline-check">Realizado até <select aria-label="Mês" value={month} onChange={(event) => setMonth(Number(event.target.value))}>{monthLabels.map((label, index) => <option key={label} value={index + 1}>{label}</option>)}</select></label>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={lines.length === 0}><Download size={16} /> Exportar CSV</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={lines.length === 0}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Orçamentar uma conta</h2><p>Guardar de novo a mesma conta substitui o valor. Uma conta e uma das suas subcontas não podem ter orçamento ao mesmo tempo.</p></div></div>
      <form className="filterbar" onSubmit={submit}>
        <label className="inline-check">Conta <input aria-label="Conta" value={accountCode} onChange={(event) => setAccountCode(event.target.value)} placeholder="ex.: 62 ou 7211" /></label>
        <label className="inline-check">Centro de custo <input aria-label="Centro de custo" value={costCenterCode} onChange={(event) => setCostCenterCode(event.target.value.toUpperCase())} placeholder="opcional" /></label>
        <label className="inline-check">Valor anual <input aria-label="Valor anual" inputMode="decimal" value={annualAmount} onChange={(event) => setAnnualAmount(event.target.value)} placeholder="0,00" /></label>
        <button className="button button-primary" type="submit" disabled={saving || accountCode.trim() === ''}>{saving ? 'A guardar…' : 'Guardar'}</button>
      </form>
    </section>

    {report && lines.length === 0 && <section className="panel empty-state"><h2>Sem orçamento para {year}</h2><p>Comece pelos grupos principais — por exemplo 72 (prestações de serviços), 62 (fornecimentos e serviços externos) e 63 (gastos com o pessoal).</p></section>}

    {report && lines.length > 0 && <>
      <section className="metadata-strip">
        <span><small>Rendimentos — realizado / orçamento</small><strong>{euro.format(report.actualIncome)}</strong><small>de {euro.format(report.budgetIncome)}</small></span>
        <span><small>Gastos — realizado / orçamento</small><strong>{euro.format(report.actualExpenses)}</strong><small>de {euro.format(report.budgetExpenses)}</small></span>
        <span><small>Resultado das contas orçamentadas</small><strong>{euro.format(report.actualResult)}</strong><small>orçamento {euro.format(report.budgetResult)}</small></span>
        <span><small>Fora do orçamento</small><strong>{euro.format(report.unbudgetedExpenses)}</strong><small>de gastos · {euro.format(report.unbudgetedIncome)} de rendimentos</small></span>
      </section>

      {general.length > 0 && <section className="panel chart-panel">
        <div className="panel-heading"><div><h2>Orçamento e realizado até {monthLabels[month - 1]}</h2><p>Por conta orçamentada, sem o detalhe por centro de custo.</p></div></div>
        <Chart title={`Orçamento e realizado por conta até ${monthLabels[month - 1]} de ${year}`} labels={general.map((line) => line.accountCode)} format={euro.format} height={230} series={[
          { name: 'Orçamento', kind: 'bar', tone: 'muted', values: general.map((line) => line.budget) },
          { name: 'Realizado', kind: 'bar', tone: 'primary', values: general.map((line) => line.actual) },
        ]} />
      </section>}

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>Conta</th><th>Centro de custo</th><th className="numeric">Orçamento anual</th><th className="numeric">Orçamento até {monthLabels[month - 1]}</th><th className="numeric">Realizado</th><th className="numeric">Desvio</th><th className="numeric">%</th><th /></tr></thead>
          <tbody>{lines.map((line) => <tr key={`${line.accountCode}-${line.costCenterCode ?? ''}`} className={line.costCenterCode ? 'detail-row' : undefined}>
            <td><Link to={`/contas/${line.accountCode}`}>{line.accountCode}</Link> {line.accountName}<br /><small>{line.kind === 'rendimento' ? 'Rendimento' : 'Gasto'}</small></td>
            <td>{line.costCenterCode ?? '—'}</td>
            <td className="numeric">{euro.format(line.annualBudget)}</td>
            <td className="numeric">{euro.format(line.budget)}</td>
            <td className="numeric strong">{euro.format(line.actual)}</td>
            <td className="numeric"><span className={`badge ${line.deviation === 0 ? 'neutral' : line.favourable ? 'success' : 'warning'}`}>{euro.format(line.deviation)}</span></td>
            <td className="numeric">{percent(line.deviationPercent)}</td>
            <td><button className="icon-button" type="button" aria-label={`Retirar ${line.accountCode} do orçamento`} disabled={saving} onClick={() => void save({ accountCode: line.accountCode, costCenterCode: line.costCenterCode, annualAmount: 0 })}><Trash2 size={16} /></button></td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <p className="statement-note">O desvio é o realizado menos o orçamentado: a verde quando ajuda o resultado (mais rendimento ou menos gasto), a laranja quando o prejudica. O realizado não conta os lançamentos anulados nem o apuramento. O valor anual é repartido por doze meses iguais — não há sazonalidade.</p>
    </>}
  </div>
}
