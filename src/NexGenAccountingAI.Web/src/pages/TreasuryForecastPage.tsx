import { CircleAlert, Database, PiggyBank, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Chart } from '../app/charts'
import { euro, formatMovementDate, type TreasuryForecast, type TreasuryForecastItem } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthNames = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']
const parseNumber = (text: string) => Number(text.replace(/\s/g, '').replace(',', '.'))

function Items({ title, items }: { title: string; items: TreasuryForecastItem[] }) {
  return <section className="panel">
    <div className="panel-heading"><div><h2>{title}</h2></div></div>
    {items.length === 0
      ? <p className="statement-note">Nenhum no horizonte da previsão.</p>
      : <div className="table-wrap"><table>
        <thead><tr><th>Terceiro</th><th>Documento</th><th>Previsto</th><th className="numeric">Valor</th></tr></thead>
        <tbody>{items.map((item, index) => <tr key={`${item.partyTaxNumber}-${item.documentDate}-${index}`}>
          <td>{item.partyName}<small>NIF {item.partyTaxNumber}</small></td>
          <td>{formatMovementDate(item.documentDate)}</td>
          <td>{formatMovementDate(item.expectedDate)}</td>
          <td className="numeric">{euro.format(item.amount)}</td>
        </tr>)}</tbody>
      </table></div>}
  </section>
}

export function TreasuryForecastPage() {
  const { activeCompany, source, treasuryForecast } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [months, setMonths] = useState(3)
  const [collectionDays, setCollectionDays] = useState(30)
  const [paymentDays, setPaymentDays] = useState(30)
  const [fixedText, setFixedText] = useState('')
  const [forecast, setForecast] = useState<TreasuryForecast | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !date) { setForecast(null); return }
    const fixed = fixedText.trim() === '' ? null : parseNumber(fixedText)
    if (fixed !== null && (!Number.isFinite(fixed) || fixed < 0)) { setError('Indique os gastos fixos como um valor positivo, ou deixe em branco.'); return }
    const controller = new AbortController()
    setError('')
    void treasuryForecast(companyId, { date, months, collectionDays, paymentDays, fixedMonthly: fixed })
      .then((value) => { if (!controller.signal.aborted) setForecast(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setForecast(null); setError(problem instanceof Error ? problem.message : 'Não foi possível calcular a previsão.') } })
    return () => controller.abort()
  }, [collectionDays, companyId, date, fixedText, months, onDatabase, paymentDays, treasuryForecast])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Previsão de tesouraria</h1><p>Quanto haverá em caixa e bancos nos próximos meses, pelo que está por receber e por pagar.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><PiggyBank /></span><h2>Requer ligação à base de dados</h2><p>A previsão parte dos saldos e dos documentos em aberto da empresa.</p></section>
    </div>
  }

  const labels = forecast?.months.map((month) => `${monthNames[month.month - 1]} ${String(month.year).slice(2)}`) ?? []

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Previsão de tesouraria</h1><p>Parte do saldo de caixa e bancos e soma o que os clientes devem, menos o que se deve aos fornecedores, no mês em que se espera receber ou pagar — a data do documento mais o prazo indicado —, menos os gastos fixos de cada mês. Os critérios estão à vista e pode mudá-los.</p></div>
      <div className="heading-actions no-print"><button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!forecast}><Printer size={16} /> Imprimir / PDF</button></div>
    </header>

    <div className="filterbar no-print">
      <label className="inline-check">Data <input type="date" aria-label="Data da previsão" value={date} onChange={(event) => setDate(event.target.value)} /></label>
      <label className="inline-check">Meses <select aria-label="Meses" value={months} onChange={(event) => setMonths(Number(event.target.value))}>{[1, 2, 3, 4, 6, 9, 12].map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
      <label className="inline-check">Prazo de recebimento <input type="number" aria-label="Prazo de recebimento em dias" min={0} max={365} value={collectionDays} onChange={(event) => setCollectionDays(Math.max(0, Math.min(365, Number(event.target.value))))} style={{ width: '5em' }} /> dias</label>
      <label className="inline-check">Prazo de pagamento <input type="number" aria-label="Prazo de pagamento em dias" min={0} max={365} value={paymentDays} onChange={(event) => setPaymentDays(Math.max(0, Math.min(365, Number(event.target.value))))} style={{ width: '5em' }} /> dias</label>
      <label className="inline-check">Gastos fixos por mês <input aria-label="Gastos fixos por mês" inputMode="decimal" value={fixedText} onChange={(event) => setFixedText(event.target.value)} placeholder={forecast?.fixedFromHistory ? forecast.fixedMonthly.toLocaleString('pt-PT') : 'pela média do pessoal'} style={{ width: '9em' }} /></label>
    </div>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    {forecast && <>
      <section className="metadata-strip">
        <span><small>Caixa e bancos em {formatMovementDate(forecast.date)}</small><strong>{euro.format(forecast.openingCash)}</strong></span>
        <span><small>Saldo previsto no fim</small><strong>{euro.format(forecast.months[forecast.months.length - 1]?.closing ?? forecast.openingCash)}</strong></span>
        <span><small>Saldo mais baixo</small><strong><span className={`badge ${forecast.lowestClosing < 0 ? 'warning' : 'success'}`}>{euro.format(forecast.lowestClosing)}</span></strong></span>
        <span><small>Gastos fixos por mês</small><strong>{euro.format(forecast.fixedMonthly)}</strong><small>{forecast.fixedFromHistory ? 'média do pessoal (63) dos últimos 3 meses' : 'indicados'}</small></span>
        <span><small>Duvidosos (mais de {forecast.doubtfulAfterDays} dias)</small><strong>{euro.format(forecast.doubtfulReceivables)}</strong><small>de clientes · {euro.format(forecast.doubtfulPayables)} a fornecedores</small></span>
      </section>

      {forecast.lowestClosing < 0 && <p className="save-status error"><CircleAlert size={15} /> Com estes critérios, a tesouraria fica negativa num dos meses da previsão.</p>}

      <section className="panel chart-panel">
        <div className="panel-heading"><div><h2>Saldo previsto</h2><p>Recebimentos e pagamentos de cada mês e o saldo no fim do mês.</p></div></div>
        <Chart title="Previsão de tesouraria por mês" labels={labels} format={euro.format} series={[
          { name: 'Recebimentos', kind: 'bar', tone: 'primary', values: forecast.months.map((month) => month.receipts) },
          { name: 'Pagamentos e gastos fixos', kind: 'bar', tone: 'danger', values: forecast.months.map((month) => month.payments + month.fixed) },
          { name: 'Saldo no fim do mês', kind: 'line', tone: 'accent', values: forecast.months.map((month) => month.closing) },
        ]} />
      </section>

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>Mês</th><th className="numeric">Saldo inicial</th><th className="numeric">Recebimentos</th><th className="numeric">Pagamentos</th><th className="numeric">Gastos fixos</th><th className="numeric">Saldo final</th></tr></thead>
          <tbody>{forecast.months.map((month, index) => <tr key={`${month.year}-${month.month}`}>
            <td>{labels[index]}</td>
            <td className="numeric">{euro.format(month.opening)}</td>
            <td className="numeric">{euro.format(month.receipts)}</td>
            <td className="numeric">{euro.format(month.payments)}</td>
            <td className="numeric">{euro.format(month.fixed)}</td>
            <td className={`numeric strong${month.closing < 0 ? ' negative' : ''}`}>{euro.format(month.closing)}</td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <div className="chart-grid-layout">
        <Items title="Maiores recebimentos previstos" items={forecast.topReceipts} />
        <Items title="Maiores pagamentos previstos" items={forecast.topPayments} />
      </div>

      <p className="statement-note">Critérios da aplicação, não um modelo estatístico. Os documentos em aberto saem da regra da idade de saldos: os pagamentos abatem os documentos mais antigos de cada terceiro, e só entram as linhas das contas 21 e 22 com terceiro. Um documento cuja data prevista já passou conta no primeiro mês; os com mais de {forecast.doubtfulAfterDays} dias ficam de fora como duvidosos. No mês da data, os gastos fixos contam só pelos dias que faltam. Ficam de fora o IVA a entregar, os impostos e os investimentos que não passem por fornecedores.</p>
    </>}
  </div>
}
