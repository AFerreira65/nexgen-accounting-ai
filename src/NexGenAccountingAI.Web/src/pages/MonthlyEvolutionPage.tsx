import { Database } from 'lucide-react'
import { useEffect, useState } from 'react'
import { BarList, Chart } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type ExerciseStructure, type MonthlyEvolution } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun', 'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez']

/** Dados de exemplo para o modo de demonstração: nove meses de um exercício fictício. */
function demoEvolution(year: number): MonthlyEvolution {
  const income = [18200, 19400, 22100, 20800, 23900, 25200, 21700, 14300, 24600]
  const expenses = [16900, 17100, 18800, 19900, 19200, 20700, 20100, 16800, 19900]
  const previous = [15800, 17900, 19100, 18700, 20400, 21900, 19800, 13100, 21200, 23400, 22100, 26800]
  let cash = 31000
  return {
    year,
    lastMonth: income.length,
    months: monthLabels.map((_, index) => {
      const monthIncome = income[index] ?? 0
      const monthExpenses = expenses[index] ?? 0
      cash += (monthIncome - monthExpenses) * 0.8
      return {
        month: index + 1,
        income: monthIncome,
        expenses: monthExpenses,
        result: monthIncome - monthExpenses,
        previousIncome: previous[index],
        previousExpenses: Math.round(previous[index] * 0.9),
        previousResult: Math.round(previous[index] * 0.1),
        cash: Math.round(cash),
        receivables: Math.round(monthIncome * 1.4),
        payables: Math.round(monthExpenses * 0.9),
      }
    }),
  }
}

/** Estrutura de exemplo para o modo de demonstração. */
function demoStructure(year: number): ExerciseStructure {
  const shares = (items: [string, number][]) => {
    const total = items.reduce((value, [, amount]) => value + amount, 0)
    return items.map(([label, amount]) => ({ label, amount, share: Math.round((amount / total) * 1000) / 10 }))
  }
  return {
    year,
    expenses: shares([['Fornecimentos e serviços externos', 71200], ['Gastos com o pessoal', 58400], ['Custo das mercadorias vendidas e das matérias consumidas', 26900], ['Gastos / reversões de depreciação e de amortização', 9100], ['Outros gastos', 3800]]),
    income: shares([['Vendas e serviços prestados', 182300], ['Outros rendimentos', 7900]]),
    topCustomers: shares([['Cliente Alfa, Lda.', 61200], ['Beta Indústria, S.A.', 44800], ['Gama Serviços', 31500], ['Delta & Filhos', 18700]]),
    topSuppliers: shares([['Fornecedor Norte, Lda.', 38900], ['Energia do Sul, S.A.', 17400], ['Papelaria Central', 6200]]),
  }
}

const sum = (values: number[]) => values.reduce((total, value) => total + value, 0)

/** A variação face ao período homólogo, ou vazio quando não há base de comparação. */
function change(current: number, previous: number) {
  if (previous === 0) return 'sem período homólogo'
  const ratio = (current - previous) / Math.abs(previous)
  return `${ratio >= 0 ? '+' : ''}${(ratio * 100).toLocaleString('pt-PT', { maximumFractionDigits: 1 })} % face ao período homólogo`
}

export function MonthlyEvolutionPage() {
  const { activeCompany, source, monthlyEvolution, exerciseStructure } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [loaded, setLoaded] = useState<MonthlyEvolution | null>(null)
  const [loadedStructure, setLoadedStructure] = useState<ExerciseStructure | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setLoaded(null); return }
    const controller = new AbortController()
    setError('')
    void monthlyEvolution(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setLoaded(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar a evolução mensal.') })
    return () => controller.abort()
  }, [monthlyEvolution, companyId, onDatabase, year])

  useEffect(() => {
    if (!onDatabase) { setLoadedStructure(null); return }
    const controller = new AbortController()
    void exerciseStructure(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setLoadedStructure(value) })
      .catch(() => { if (!controller.signal.aborted) setLoadedStructure(null) })
    return () => controller.abort()
  }, [exerciseStructure, companyId, onDatabase, year])

  const structure = onDatabase ? loadedStructure : demoStructure(year)
  const evolution = onDatabase ? loaded : demoEvolution(year)
  const months = evolution?.months ?? []
  const last = evolution?.lastMonth ?? 0
  const elapsed = months.slice(0, last)
  // Depois do último mês com movimentos não há nada a desenhar no exercício corrente.
  const upTo = (pick: (month: MonthlyEvolution['months'][number]) => number) => months.map((month) => (month.month <= last ? pick(month) : null))
  const cumulative = (values: number[]) => values.map((_, index) => sum(values.slice(0, index + 1)))
  const resultToDate = cumulative(months.map((month) => month.result)).map((value, index) => (index < last ? value : null))
  const previousResultToDate = cumulative(months.map((month) => month.previousResult))
  const hasPrevious = months.some((month) => month.previousIncome !== 0 || month.previousExpenses !== 0)

  const income = sum(elapsed.map((month) => month.income))
  const expenses = sum(elapsed.map((month) => month.expenses))
  const previousIncome = sum(elapsed.map((month) => month.previousIncome))
  const previousExpenses = sum(elapsed.map((month) => month.previousExpenses))
  const closing = elapsed[elapsed.length - 1]

  const exportCsv = () => downloadCsv(
    csvFilename(`evolucao-mensal-${year}`, activeCompany.legalName),
    toCsv([
      ['Mês', 'Rendimentos', 'Gastos', 'Resultado', `Rendimentos ${year - 1}`, `Gastos ${year - 1}`, `Resultado ${year - 1}`, 'Tesouraria', 'Clientes', 'Fornecedores'],
      ...months.map((month) => [monthLabels[month.month - 1], month.income, month.expenses, month.result, month.previousIncome, month.previousExpenses, month.previousResult, month.cash, month.receivables, month.payables]),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Evolução mensal</h1><p>Rendimentos, gastos e resultado mês a mês, com o exercício anterior ao lado, e os saldos de tesouraria, clientes e fornecedores no fim de cada mês.</p></div>
      {onDatabase
        ? <span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span>
        : <span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração — dados de exemplo</span>}
    </header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const value = Number(event.target.value); if (value >= 2000 && value <= 9999) setYear(value) }} /></label>
      <ExportButtons run={exportCsv} disabled={months.length === 0} />
    </div>

    {evolution && last === 0 && <section className="panel empty-state"><h2>Sem movimentos em {year}</h2><p>Escolha outro exercício ou importe os movimentos.</p></section>}

    {evolution && last > 0 && <>
      <section className="metadata-strip">
        <span><small>Rendimentos até {monthLabels[last - 1]}</small><strong>{euro.format(income)}</strong><small>{change(income, previousIncome)}</small></span>
        <span><small>Gastos até {monthLabels[last - 1]}</small><strong>{euro.format(expenses)}</strong><small>{change(expenses, previousExpenses)}</small></span>
        <span><small>Resultado antes do apuramento</small><strong>{euro.format(income - expenses)}</strong><small>{change(income - expenses, previousIncome - previousExpenses)}</small></span>
        <span><small>Tesouraria no fim de {monthLabels[last - 1]}</small><strong>{euro.format(closing.cash)}</strong><small>Clientes {euro.format(closing.receivables)} · Fornecedores {euro.format(closing.payables)}</small></span>
      </section>

      <div className="chart-grid-layout">
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Rendimentos, gastos e resultado</h2><p>Movimentos de cada mês, sem o apuramento de resultados.</p></div></div>
          <Chart title={`Rendimentos, gastos e resultado por mês em ${year}`} labels={monthLabels} format={euro.format} series={[
            { name: 'Rendimentos', kind: 'bar', tone: 'primary', values: upTo((month) => month.income) },
            { name: 'Gastos', kind: 'bar', tone: 'danger', values: upTo((month) => month.expenses) },
            { name: 'Resultado', kind: 'line', tone: 'accent', values: upTo((month) => month.result) },
          ]} />
        </section>

        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Resultado acumulado</h2><p>{hasPrevious ? `Comparado com ${year - 1}, mês a mês.` : `Sem movimentos em ${year - 1} para comparar.`}</p></div></div>
          <Chart title={`Resultado acumulado em ${year} e em ${year - 1}`} labels={monthLabels} format={euro.format} series={[
            { name: String(year), kind: 'area', tone: 'primary', values: resultToDate },
            ...(hasPrevious ? [{ name: String(year - 1), kind: 'line' as const, tone: 'muted' as const, dashed: true, values: previousResultToDate }] : []),
          ]} />
        </section>

        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Tesouraria</h2><p>Saldo de caixa e bancos no fim de cada mês.</p></div></div>
          <Chart title={`Saldo de caixa e bancos no fim de cada mês de ${year}`} labels={monthLabels} format={euro.format} series={[
            { name: 'Caixa e bancos', kind: 'area', tone: 'accent', values: upTo((month) => month.cash) },
          ]} />
        </section>

        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Clientes e fornecedores</h2><p>O que há a receber e a pagar no fim de cada mês.</p></div></div>
          <Chart title={`Saldos de clientes e de fornecedores no fim de cada mês de ${year}`} labels={monthLabels} format={euro.format} series={[
            { name: 'Clientes', kind: 'line', tone: 'primary', values: upTo((month) => month.receivables) },
            { name: 'Fornecedores', kind: 'line', tone: 'warning', values: upTo((month) => month.payables) },
          ]} />
        </section>
      </div>

      {structure && <div className="chart-grid-layout">
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Gastos por natureza</h2><p>As rubricas da demonstração de resultados de {year}, com o peso de cada uma.</p></div></div>
          {structure.expenses.length > 0 ? <BarList title={`Gastos por natureza em ${year}`} items={structure.expenses} format={euro.format} tone="danger" /> : <p className="empty-state">Sem gastos no exercício.</p>}
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Rendimentos por natureza</h2><p>De onde vêm os rendimentos de {year}.</p></div></div>
          {structure.income.length > 0 ? <BarList title={`Rendimentos por natureza em ${year}`} items={structure.income} format={euro.format} /> : <p className="empty-state">Sem rendimentos no exercício.</p>}
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Maiores clientes</h2><p>Faturação lançada nas contas 21 (com IVA), pelos lançamentos com terceiro.</p></div></div>
          {structure.topCustomers.length > 0 ? <BarList title={`Maiores clientes em ${year}`} items={structure.topCustomers} format={euro.format} tone="accent" /> : <p className="empty-state">Sem lançamentos de clientes com terceiro indicado.</p>}
        </section>
        <section className="panel chart-panel">
          <div className="panel-heading"><div><h2>Maiores fornecedores</h2><p>Compras lançadas nas contas 22 (com IVA), pelos lançamentos com terceiro.</p></div></div>
          {structure.topSuppliers.length > 0 ? <BarList title={`Maiores fornecedores em ${year}`} items={structure.topSuppliers} format={euro.format} tone="warning" /> : <p className="empty-state">Sem lançamentos de fornecedores com terceiro indicado.</p>}
        </section>
      </div>}

      <section className="panel">
        <div className="panel-heading"><div><h2>Valores</h2><p>Os números por trás dos gráficos.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Mês</th><th className="numeric">Rendimentos</th><th className="numeric">Gastos</th><th className="numeric">Resultado</th><th className="numeric">Resultado {year - 1}</th><th className="numeric">Tesouraria</th><th className="numeric">Clientes</th><th className="numeric">Fornecedores</th></tr></thead>
          <tbody>
            {elapsed.map((month) => <tr key={month.month}>
              <td>{monthLabels[month.month - 1]}</td>
              <td className="numeric">{euro.format(month.income)}</td>
              <td className="numeric">{euro.format(month.expenses)}</td>
              <td className="numeric strong">{euro.format(month.result)}</td>
              <td className="numeric">{euro.format(month.previousResult)}</td>
              <td className="numeric">{euro.format(month.cash)}</td>
              <td className="numeric">{euro.format(month.receivables)}</td>
              <td className="numeric">{euro.format(month.payables)}</td>
            </tr>)}
            <tr className="subtotal-row">
              <td>Total</td>
              <td className="numeric">{euro.format(income)}</td>
              <td className="numeric">{euro.format(expenses)}</td>
              <td className="numeric">{euro.format(income - expenses)}</td>
              <td className="numeric">{euro.format(previousIncome - previousExpenses)}</td>
              <td colSpan={3} />
            </tr>
          </tbody>
        </table></div>
      </section>
    </>}
  </div>
}
