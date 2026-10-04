import { CircleAlert, Database, Download, Percent, Printer, TrendingUp } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Chart } from '../app/charts'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type FinancialRatio, type FinancialRatios, type OperatingMap } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun', 'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez']
const shortDate = (value: string) => value.split('-').reverse().join('/')
const plain = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const whole = new Intl.NumberFormat('pt-PT', { maximumFractionDigits: 0 })

export function OperatingMapPage() {
  const { activeCompany, source, operatingMap } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [map, setMap] = useState<OperatingMap | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setMap(null); return }
    const controller = new AbortController()
    setError('')
    void operatingMap(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setMap(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o mapa de exploração.') })
    return () => controller.abort()
  }, [operatingMap, companyId, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Mapa de exploração</h1><p>A demonstração de resultados por naturezas, mês a mês.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><TrendingUp /></span><h2>Requer ligação ao SQL Server</h2><p>O mapa de exploração é calculado na base de dados da empresa.</p></section>
    </div>
  }

  const rows = map?.rows ?? []
  const last = map?.lastMonth ?? 0
  const shown = monthLabels.slice(0, Math.max(last, 1))
  const row = (label: string) => rows.find((item) => item.label === label)
  const ebitda = row('Resultado antes de depreciações, gastos de financiamento e impostos')
  const net = row('Resultado líquido do período')
  const hasBudget = map?.hasBudget ?? false
  // O orçamentado de janeiro até ao último mês com movimentos, para comparar com o total realizado.
  const budgetToDate = (item: OperatingMap['rows'][number]) => (item.budget ?? []).slice(0, last).reduce((sum, value) => sum + value, 0)
  const exportCsv = () => downloadCsv(
    csvFilename(`mapa-exploracao-${year}`, activeCompany.legalName),
    toCsv(hasBudget
      ? [['Rubrica', 'Valores', ...monthLabels, 'Total'], ...rows.flatMap((item) => [[item.label, 'Realizado', ...item.months, item.total], [item.label, 'Orçamento', ...(item.budget ?? []), (item.budget ?? []).reduce((sum, value) => sum + value, 0)]])]
      : [['Rubrica', ...monthLabels, 'Total'], ...rows.map((item) => [item.label, ...item.months, item.total])]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Mapa de exploração</h1><p>A demonstração de resultados por naturezas, mês a mês, sem o apuramento. Rendimentos positivos, gastos negativos.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const value = Number(event.target.value); if (value >= 2000 && value <= 9999) setYear(value) }} /></label>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={rows.length === 0}><Download size={16} /> Exportar CSV</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={rows.length === 0}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {map && last === 0 && <section className="panel empty-state"><h2>Sem movimentos de exploração em {year}</h2><p>Escolha outro exercício ou importe os movimentos.</p></section>}

    {map && last > 0 && <>
      {ebitda && net && <section className="panel chart-panel">
        <div className="panel-heading"><div><h2>Resultados por mês</h2><p>{map.model}</p></div></div>
        <Chart title={`Resultado antes de depreciações e resultado líquido por mês em ${year}`} labels={shown} format={euro.format} series={[
          { name: 'Antes de depreciações, financiamento e impostos', kind: 'bar', tone: 'primary', values: ebitda.months.slice(0, shown.length) },
          { name: 'Resultado líquido', kind: 'line', tone: 'accent', values: net.months.slice(0, shown.length) },
          ...(net.budget ? [{ name: 'Resultado orçamentado', kind: 'line' as const, tone: 'muted' as const, dashed: true, values: net.budget.slice(0, shown.length) }] : []),
        ]} />
      </section>}

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>Rubricas</th>{shown.map((label) => <th key={label} className="numeric">{label}</th>)}<th className="numeric">Total</th>{hasBudget && <><th className="numeric">Orçamento até {shown[shown.length - 1]}</th><th className="numeric">Desvio</th></>}</tr></thead>
          <tbody>{rows.map((item) => <tr key={item.label} className={item.isTotal ? 'subtotal-row' : undefined}>
            <td>{item.label}</td>
            {shown.map((label, index) => <td key={label} className="numeric">{item.months[index] === 0 ? '—' : plain.format(item.months[index])}</td>)}
            <td className="numeric strong">{plain.format(item.total)}</td>
            {hasBudget && <>
              <td className="numeric">{plain.format(budgetToDate(item))}</td>
              <td className="numeric"><span className={`badge ${item.total === budgetToDate(item) ? 'neutral' : item.total > budgetToDate(item) ? 'success' : 'warning'}`}>{plain.format(item.total - budgetToDate(item))}</span></td>
            </>}
          </tr>)}</tbody>
        </table></div>
      </section>

      {hasBudget && <p className="statement-note">O <Link to="/orcamento">orçamento</Link> entra na rubrica das contas que cada linha abrange, com o sinal do mapa (gastos negativos). O desvio é o realizado menos o orçamentado até {shown[shown.length - 1]}: a verde quando ajuda o resultado, a laranja quando o prejudica. Os subtotais orçamentados só somam as rubricas que têm orçamento.</p>}

      {map.unassignedBudget.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>Orçamento fora do mapa</h2><p>Linhas do orçamento que não cabem numa só rubrica. Orçamente por grupos mais finos (por exemplo 62 em vez de 6) para entrarem no mapa.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Conta</th><th>Centro de custo</th><th>Motivo</th><th className="numeric">Orçamento anual</th></tr></thead>
          <tbody>{map.unassignedBudget.map((line) => <tr key={`${line.accountCode}-${line.costCenterCode ?? ''}`}>
            <td>{line.accountCode}</td>
            <td>{line.costCenterCode ?? '—'}</td>
            <td>{line.reason}</td>
            <td className="numeric">{euro.format(line.amount)}</td>
          </tr>)}</tbody>
        </table></div>
      </section>}

      {map.excluded.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>Contas fora do mapa</h2><p>Contas de gastos ou rendimentos com movimento que não cabem em nenhuma rubrica. Corrija a taxonomia no plano de contas.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Conta</th><th>Motivo</th><th className="numeric">Valor</th></tr></thead>
          <tbody>{map.excluded.map((account) => <tr key={account.accountCode}>
            <td><Link to={`/contas/${account.accountCode}`}>{account.accountCode}</Link> {account.accountName}</td>
            <td>{account.reason}</td>
            <td className="numeric">{euro.format(account.balance)}</td>
          </tr>)}</tbody>
        </table></div>
      </section>}
    </>}
  </div>
}

function ratioValue(ratio: FinancialRatio, value: number | null) {
  if (value === null) return '—'
  switch (ratio.unit) {
    case '%': return `${plain.format(value)} %`
    case 'dias': return `${whole.format(value)} dias`
    case '€': return euro.format(value)
    default: return plain.format(value)
  }
}

export function RatiosPage() {
  const { activeCompany, source, financialRatios } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const [ratios, setRatios] = useState<FinancialRatios | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setRatios(null); return }
    const controller = new AbortController()
    setError('')
    void financialRatios(companyId, { from: fromDate, to: toDate })
      .then((value) => { if (!controller.signal.aborted) setRatios(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setRatios(null); setError(problem instanceof Error ? problem.message : 'Não foi possível calcular os rácios.') } })
    return () => controller.abort()
  }, [financialRatios, companyId, onDatabase, fromDate, toDate])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Rácios</h1><p>Liquidez, estrutura financeira, rendibilidade e prazos médios.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Percent /></span><h2>Requer ligação ao SQL Server</h2><p>Os rácios são calculados sobre as demonstrações financeiras da empresa.</p></section>
    </div>
  }

  const list = ratios?.ratios ?? []
  const exportCsv = () => downloadCsv(
    csvFilename('racios', activeCompany.legalName),
    toCsv([['Grupo', 'Rácio', 'Unidade', 'Período', 'Período homólogo', 'Fórmula'], ...list.map((ratio) => [ratio.group, ratio.label, ratio.unit, ratio.value ?? '', ratio.comparative ?? '', ratio.formula])]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Rácios</h1><p>Calculados sobre o balanço à data final e a demonstração de resultados do período, com o período homólogo ao lado. Cada rácio mostra a fórmula usada.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={list.length === 0}><Download size={16} /> Exportar CSV</button>
    </div>

    {ratios && (ratios.balanceDifference !== 0 || ratios.excludedAccounts > 0) && <p className="save-status error"><CircleAlert size={15} /> {ratios.balanceDifference !== 0 ? `O balanço que serve de base não fecha (diferença de ${euro.format(ratios.balanceDifference)}). ` : ''}{ratios.excludedAccounts > 0 ? `Há ${ratios.excludedAccounts} conta(s) com saldo fora das demonstrações. ` : ''}Os rácios podem não ser fiáveis — veja as <Link to="/demonstracoes">demonstrações financeiras</Link>.</p>}

    {ratios && <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Rácio</th><th className="numeric">{shortDate(ratios.from)} a {shortDate(ratios.to)}</th><th className="numeric">Período homólogo</th><th>Fórmula</th></tr></thead>
        <tbody>{list.map((ratio, index) => <Fragment key={ratio.label}>
          {(index === 0 || list[index - 1].group !== ratio.group) && <tr className="analyses-subtotal"><td colSpan={4}>{ratio.group}</td></tr>}
          <tr>
            <td>{ratio.label}</td>
            <td className="numeric strong">{ratioValue(ratio, ratio.value)}</td>
            <td className="numeric">{ratioValue(ratio, ratio.comparative)}</td>
            <td className="ratio-formula">{ratio.formula}</td>
          </tr>
        </Fragment>)}</tbody>
      </table></div>
    </section>}

    {ratios && <p className="statement-note">Os resultados são os do período ({ratios.days} dias) e não são anualizados; os prazos médios usam os dias do período e saldos com IVA contra vendas e compras sem IVA, pelo que saem sobrestimados. «—» quer dizer que o denominador é zero. Não há valores de referência: a leitura depende do setor.</p>}
  </div>
}
