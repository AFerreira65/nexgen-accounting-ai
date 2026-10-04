import { CircleAlert, Database, Download, HandCoins, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type WithholdingReport } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthLabels = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

export function WithholdingsPage() {
  const { activeCompany, source, withholdings } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [report, setReport] = useState<WithholdingReport | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void withholdings(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setReport(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as retenções na fonte.') } })
    return () => controller.abort()
  }, [withholdings, companyId, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Retenções na fonte</h1><p>O retido e o entregue ao Estado, por conta, por mês e por beneficiário.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><HandCoins /></span><h2>Requer ligação à base de dados</h2><p>As retenções são lidas das contas 242 da empresa.</p></section>
    </div>
  }

  const accounts = report?.accounts ?? []
  const months = report?.months ?? []
  const beneficiaries = report?.beneficiaries ?? []
  const monthNumbers = [...new Set(months.map((row) => row.month))]
  const cell = (month: number, accountCode: string) => months.find((row) => row.month === month && row.accountCode === accountCode)
  const exportCsv = () => downloadCsv(
    csvFilename(`retencoes-na-fonte-${year}`, activeCompany.legalName),
    toCsv([
      ['País', 'NIF', 'Beneficiário', 'Conta', 'Retido', 'Rendimento (estimativa)', 'Lançamentos'],
      ...beneficiaries.map((row) => [row.countryCode, row.taxNumber, row.name, row.accountCode, row.withheld, row.incomeEstimate, row.entries]),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Retenções na fonte</h1><p>O movimento das contas 242 no exercício: o retido (créditos), o entregue ao Estado (débitos) e o que falta entregar, por conta, por mês e por beneficiário.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={beneficiaries.length === 0}><Download size={16} /> Exportar beneficiários (CSV)</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!report}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {report && accounts.length === 0 && <section className="panel empty-state"><h2>Sem retenções em {year}</h2><p>Não há movimentos nas contas 242 neste exercício.</p></section>}

    {report && accounts.length > 0 && <>
      <section className="metadata-strip">
        <span><small>Retido em {year}</small><strong>{euro.format(report.totalWithheld)}</strong></span>
        <span><small>Entregue ao Estado</small><strong>{euro.format(report.totalDelivered)}</strong></span>
        <span><small>Por entregar no fim do exercício</small><strong>{euro.format(report.totalBalance)}</strong></span>
        <span><small>Retido sem terceiro identificado</small><strong>{euro.format(report.withheldWithoutParty)}</strong></span>
      </section>

      {report.withheldWithoutParty !== 0 && <p className="save-status error"><CircleAlert size={15} /> Há {euro.format(report.withheldWithoutParty)} retidos em lançamentos sem terceiro (ou com mais de um): não é possível atribuí-los a um beneficiário. É o normal nos salários processados em conjunto.</p>}

      <section className="panel">
        <div className="panel-heading"><div><h2>Por conta</h2><p>A natureza do rendimento é a que o plano de contas der a cada subconta da 242.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Conta</th><th className="numeric">Por entregar no início</th><th className="numeric">Retido</th><th className="numeric">Entregue</th><th className="numeric">Por entregar no fim</th></tr></thead>
          <tbody>
            {accounts.map((row) => <tr key={row.accountCode}>
              <td><Link to={`/contas/${row.accountCode}`}>{row.accountCode}</Link> {row.accountName}</td>
              <td className="numeric">{euro.format(row.opening)}</td>
              <td className="numeric">{euro.format(row.withheld)}</td>
              <td className="numeric">{euro.format(row.delivered)}</td>
              <td className="numeric strong">{euro.format(row.balance)}</td>
            </tr>)}
            <tr className="subtotal-row"><td>Total</td><td className="numeric">{euro.format(accounts.reduce((sum, row) => sum + row.opening, 0))}</td><td className="numeric">{euro.format(report.totalWithheld)}</td><td className="numeric">{euro.format(report.totalDelivered)}</td><td className="numeric">{euro.format(report.totalBalance)}</td></tr>
          </tbody>
        </table></div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Por mês</h2><p>O retido em cada mês é o que se entrega no mês seguinte.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Mês</th>{accounts.map((row) => <th key={row.accountCode} className="numeric">{row.accountCode} — retido</th>)}<th className="numeric">Total retido</th><th className="numeric">Entregue no mês</th></tr></thead>
          <tbody>{monthNumbers.map((month) => <tr key={month}>
            <td>{monthLabels[month - 1]}</td>
            {accounts.map((row) => <td key={row.accountCode} className="numeric">{euro.format(cell(month, row.accountCode)?.withheld ?? 0)}</td>)}
            <td className="numeric strong">{euro.format(months.filter((row) => row.month === month).reduce((sum, row) => sum + row.withheld, 0))}</td>
            <td className="numeric">{euro.format(months.filter((row) => row.month === month).reduce((sum, row) => sum + row.delivered, 0))}</td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Por beneficiário</h2><p>O terceiro da linha da retenção ou, se a linha não o tem, o único terceiro do lançamento. O rendimento é uma estimativa: os gastos (classe 6) dos mesmos lançamentos.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>NIF</th><th>Beneficiário</th><th>Conta</th><th className="numeric">Rendimento (estimativa)</th><th className="numeric">Retido</th><th className="numeric">Lançamentos</th></tr></thead>
          <tbody>{beneficiaries.map((row) => <tr key={`${row.countryCode}-${row.taxNumber}-${row.accountCode}`} className={row.taxNumber === '' ? 'muted-row' : undefined}>
            <td className="primary-cell">{row.taxNumber === '' ? '—' : `${row.countryCode === 'PT' ? '' : `${row.countryCode} `}${row.taxNumber}`}</td>
            <td>{row.name}</td>
            <td>{row.accountCode}</td>
            <td className="numeric">{row.taxNumber === '' ? '—' : euro.format(row.incomeEstimate)}</td>
            <td className="numeric strong">{euro.format(row.withheld)}</td>
            <td className="numeric">{row.entries}</td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <p className="statement-note">{report.disclaimer}</p>
    </>}
  </div>
}
