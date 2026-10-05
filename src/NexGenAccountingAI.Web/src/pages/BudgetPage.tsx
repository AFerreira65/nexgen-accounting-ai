import { CalendarRange, CircleAlert, Database, Printer, Target, Trash2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Chart } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type BudgetReport } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
const parseAmount = (text: string) => (text.trim() === '' ? 0 : Number(text.replace(/\s/g, '').replace(',', '.')))
const emptyMonths = () => Array.from({ length: 12 }, () => '')
const percent = (value: number | null) => (value === null ? '—' : `${value > 0 ? '+' : ''}${value.toLocaleString('pt-PT', { maximumFractionDigits: 1 })} %`)

export function BudgetPage() {
  const { activeCompany, source, budgetReport, setBudgetLine, copyBudget } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [month, setMonth] = useState(12)
  const [report, setReport] = useState<BudgetReport | null>(null)
  const [accountCode, setAccountCode] = useState('')
  const [costCenterCode, setCostCenterCode] = useState('')
  const [annualAmount, setAnnualAmount] = useState('')
  const [byMonth, setByMonth] = useState(false)
  const [monthAmounts, setMonthAmounts] = useState(emptyMonths)
  const [copySource, setCopySource] = useState<'orcamento' | 'realizado'>('orcamento')
  const [copyYear, setCopyYear] = useState(defaultYear - 1)
  const [copyAdjustment, setCopyAdjustment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])
  useEffect(() => { setCopyYear(year - 1) }, [year])

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
      <section className="panel empty-state"><span className="empty-icon"><Target /></span><h2>Requer ligação à base de dados</h2><p>O orçamento é guardado por empresa e por exercício.</p></section>
    </div>
  }

  async function save(input: { accountCode: string; costCenterCode: string | null; annualAmount: number; monthlyAmounts?: number[] }) {
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
    const line = { accountCode: accountCode.trim(), costCenterCode: costCenterCode.trim() || null }
    let saved: boolean
    if (byMonth) {
      const months = monthAmounts.map(parseAmount)
      if (months.some((value) => !Number.isFinite(value) || value < 0) || months.every((value) => value === 0)) { setError('Indique valores mensais positivos; os meses sem orçamento ficam em branco.'); return }
      saved = await save({ ...line, annualAmount: 0, monthlyAmounts: months })
    } else {
      const amount = parseAmount(annualAmount)
      if (!Number.isFinite(amount) || amount <= 0) { setError('Indique um valor anual positivo.'); return }
      saved = await save({ ...line, annualAmount: amount })
    }
    if (saved) {
      setAccountCode('')
      setCostCenterCode('')
      setAnnualAmount('')
      setMonthAmounts(emptyMonths())
    }
  }

  function editMonths(line: BudgetReport['lines'][number]) {
    setAccountCode(line.accountCode)
    setCostCenterCode(line.costCenterCode ?? '')
    setByMonth(true)
    setMonthAmounts(line.monthlyBudget.map((value) => (value === 0 ? '' : String(value).replace('.', ','))))
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  async function copy(event: FormEvent) {
    event.preventDefault()
    const adjustment = parseAmount(copyAdjustment)
    if (!Number.isFinite(adjustment)) { setError('Indique o ajuste em percentagem, por exemplo 3 ou -5.'); return }
    setSaving(true)
    setError('')
    try {
      setReport(await copyBudget(companyId, year, month, { source: copySource, sourceYear: copyYear, adjustmentPercent: adjustment }))
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível copiar o orçamento.')
    } finally {
      setSaving(false)
    }
  }

  const monthsTotal = monthAmounts.map(parseAmount).reduce((sum, value) => sum + (Number.isFinite(value) ? value : 0), 0)

  const lines = report?.lines ?? []
  const general = lines.filter((line) => line.costCenterCode === null)
  const exportCsv = () => downloadCsv(
    csvFilename(`orcamento-${year}`, activeCompany.legalName),
    toCsv([
      ['Conta', 'Nome', 'Centro de custo', 'Tipo', 'Orçamento anual', `Orçamento até ${monthLabels[month - 1]}`, 'Realizado', 'Desvio', 'Desvio (%)', ...monthLabels.map((label) => `Orçamento ${label}`)],
      ...lines.map((line) => [line.accountCode, line.accountName, line.costCenterCode ?? '', line.kind, line.annualBudget, line.budget, line.actual, line.deviation, line.deviationPercent ?? '', ...line.monthlyBudget]),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Orçamento</h1><p>O valor anual previsto por conta de gastos ou de rendimentos — uma conta ou o grupo que a agrega, e opcionalmente um <Link to="/centros-custo">centro de custo</Link> — repartido por doze meses iguais ou indicado mês a mês, e comparado com o realizado.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <label className="inline-check">Realizado até <select aria-label="Mês" value={month} onChange={(event) => setMonth(Number(event.target.value))}>{monthLabels.map((label, index) => <option key={label} value={index + 1}>{label}</option>)}</select></label>
      <ExportButtons run={exportCsv} disabled={lines.length === 0} />
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={lines.length === 0}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Orçamentar uma conta</h2><p>Guardar de novo a mesma conta substitui o valor. Uma conta e uma das suas subcontas não podem ter orçamento ao mesmo tempo.</p></div></div>
      <form className="filterbar" onSubmit={submit}>
        <label className="inline-check">Conta <input aria-label="Conta" value={accountCode} onChange={(event) => setAccountCode(event.target.value)} placeholder="ex.: 62 ou 7211" /></label>
        <label className="inline-check">Centro de custo <input aria-label="Centro de custo" value={costCenterCode} onChange={(event) => setCostCenterCode(event.target.value.toUpperCase())} placeholder="opcional" /></label>
        {byMonth
          ? <span className="inline-check">Total anual <strong>{euro.format(monthsTotal)}</strong></span>
          : <label className="inline-check">Valor anual <input aria-label="Valor anual" inputMode="decimal" value={annualAmount} onChange={(event) => setAnnualAmount(event.target.value)} placeholder="0,00" /></label>}
        <label className="inline-check"><input type="checkbox" checked={byMonth} onChange={(event) => setByMonth(event.target.checked)} /> Valores por mês</label>
        <button className="button button-primary" type="submit" disabled={saving || accountCode.trim() === ''}>{saving ? 'A guardar…' : 'Guardar'}</button>
        {byMonth && <div className="budget-months" style={{ flexBasis: '100%' }}>
          {monthLabels.map((label, index) => <label key={label}>{label}<input aria-label={`Orçamento de ${label}`} inputMode="decimal" value={monthAmounts[index]} onChange={(event) => setMonthAmounts((current) => current.map((value, position) => (position === index ? event.target.value : value)))} placeholder="0,00" /></label>)}
        </div>}
      </form>
    </section>

    {report && lines.length === 0 && <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Copiar de outro exercício</h2><p>Parte do orçamento de outro exercício (as mesmas linhas, mês a mês) ou do que nele se realizou (uma linha por grupo de contas, com o valor de cada mês). Só para um exercício ainda sem orçamento.</p></div></div>
      <form className="filterbar" onSubmit={copy}>
        <label className="inline-check">Origem <select aria-label="Origem" value={copySource} onChange={(event) => setCopySource(event.target.value === 'realizado' ? 'realizado' : 'orcamento')}><option value="orcamento">Orçamento</option><option value="realizado">Realizado</option></select></label>
        <label className="inline-check">Do exercício <input type="number" aria-label="Exercício de origem" min={2000} max={9999} value={copyYear} onChange={(event) => setCopyYear(Number(event.target.value))} /></label>
        <label className="inline-check">Ajuste (%) <input aria-label="Ajuste em percentagem" inputMode="decimal" value={copyAdjustment} onChange={(event) => setCopyAdjustment(event.target.value)} placeholder="0" /></label>
        <button className="button button-secondary" type="submit" disabled={saving || copyYear < 2000 || copyYear > 9999}>{saving ? 'A copiar…' : 'Copiar'}</button>
      </form>
    </section>}

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
            <td><button className="icon-button" type="button" aria-label={`Editar os meses de ${line.accountCode}`} title="Valores por mês" disabled={saving} onClick={() => editMonths(line)}><CalendarRange size={16} /></button><button className="icon-button" type="button" aria-label={`Retirar ${line.accountCode} do orçamento`} disabled={saving} onClick={() => void save({ accountCode: line.accountCode, costCenterCode: line.costCenterCode, annualAmount: 0 })}><Trash2 size={16} /></button></td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <p className="statement-note">O desvio é o realizado menos o orçamentado: a verde quando ajuda o resultado (mais rendimento ou menos gasto), a laranja quando o prejudica. O realizado não conta os lançamentos anulados nem o apuramento. Um valor anual é repartido por doze meses iguais; com «Valores por mês» cada mês tem o seu.</p>
    </>}
  </div>
}
