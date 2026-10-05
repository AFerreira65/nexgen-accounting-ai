import { ArrowLeftRight, CircleAlert, CircleCheck, Database, FileUp } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, formatMovementDate, type CrossCheckReport, type SaftSalesInvoicesSummary } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'

const monthFormatter = new Intl.DateTimeFormat('pt-PT', { month: 'long', year: 'numeric' })

function monthLabel(year: number, month: number): string {
  const label = monthFormatter.format(new Date(Date.UTC(year, month - 1, 1)))
  return label.charAt(0).toUpperCase() + label.slice(1)
}

function signed(value: number): string {
  return `${value > 0 ? '+' : ''}${euro.format(value)}`
}

type SalesInvoicesState = { summary: SaftSalesInvoicesSummary; fileName: string }

export function CrossCheckPage() {
  const { activeCompany, crossCheck, saftSalesInvoicesSummary, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, companyId)
  const [report, setReport] = useState<CrossCheckReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const salesInvoicesInput = useRef<HTMLInputElement>(null)
  const [salesInvoices, setSalesInvoices] = useState<SalesInvoicesState | null>(null)
  const [salesInvoicesError, setSalesInvoicesError] = useState('')

  useEffect(() => { setSalesInvoices(null); setSalesInvoicesError('') }, [companyId])

  async function onSalesInvoicesFile(file: File | undefined) {
    if (!file) return
    setSalesInvoicesError('')
    try {
      const summary = await saftSalesInvoicesSummary(companyId, file)
      setSalesInvoices({ summary, fileName: file.name })
    } catch (problem) {
      setSalesInvoices(null)
      setSalesInvoicesError(problem instanceof Error ? problem.message : 'Não foi possível ler o SAF-T de faturação.')
    }
  }

  useEffect(() => {
    if (source !== 'database') { setReport(null); return }
    if (!fromDate || !toDate) { setReport(null); setError(''); return }
    const controller = new AbortController()
    setLoading(true)
    void crossCheck(companyId, { from: fromDate, to: toDate })
      .then((body) => { setReport(body); setError('') })
      .catch((problem: unknown) => {
        if (problem instanceof DOMException && problem.name === 'AbortError') return
        setReport(null)
        setError(problem instanceof Error ? problem.message : 'Não foi possível montar o cruzamento.')
      })
      .finally(() => setLoading(false))
    return () => controller.abort()
  }, [companyId, crossCheck, fromDate, toDate, source])

  function exportCsv() {
    if (!report) return
    const rows: (string | number)[][] = [
      ['Cruzamento', `${report.from} a ${report.to}`],
      [],
      ['Compras — por mês'],
      ['Mês', 'e-Fatura base', 'e-Fatura IVA', 'Contab. base', 'Contab. IVA', 'Δ base', 'Δ IVA'],
      ...report.purchases.months.map((month) => [
        monthLabel(month.year, month.month),
        month.efaturaBase, month.efaturaVat, month.accountingBase, month.accountingVat, month.baseDelta, month.vatDelta,
      ]),
      [],
      ['Faturas do e-Fatura sem correspondência na contabilidade'],
      ['Data', 'Fornecedor', 'NIF', 'Fatura', 'Base', 'IVA', 'Total'],
      ...report.purchases.efaturaWithoutAccounting.map((invoice) => [
        invoice.invoiceDate, invoice.supplierName, invoice.supplierTaxId, invoice.invoiceNo,
        invoice.taxBase, invoice.taxAmount, invoice.grossTotal,
      ]),
      [],
      ['Lançamentos de compra sem fatura no e-Fatura'],
      ['Data', 'Referência', 'Documento', 'Descrição', 'Base', 'IVA dedutível', 'Total'],
      ...report.purchases.accountingWithoutEfatura.map((entry) => [
        entry.entryDate, entry.reference, entry.documentReference, entry.description,
        entry.purchaseBase, entry.deductibleVat, entry.total,
      ]),
      [],
      ['Vendas — por mês'],
      ['Mês', 'Rendimentos', 'IVA liquidado', 'Total'],
      ...report.sales.months.map((month) => [
        monthLabel(month.year, month.month), month.income, month.outputVat, month.total,
      ]),
    ]
    downloadCsv(csvFilename('cruzamento', activeCompany.legalName), toCsv(rows))
  }

  if (source !== 'database') {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Conferência</p><h1>Cruzamento</h1><p>Compras do e-Fatura face à contabilidade, e as vendas lançadas por mês.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><ArrowLeftRight /></span><h2>Requer ligação à base de dados</h2><p>O cruzamento lê as faturas do e-Fatura e os lançamentos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Conferência</p><h1>Cruzamento</h1><p>As compras conhecidas pelo e-Fatura face aos lançamentos da contabilidade, e as vendas lançadas por mês. É um apoio de conferência — não altera lançamentos.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    <section className="panel">
      <div className="panel-heading"><div><h2>Período</h2><p>Um conjunto de meses do exercício.</p></div>{report && <ExportButtons run={exportCsv} />}</div>
      <div className="filterbar">
        <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
        <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
        <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      </div>
      {(!fromDate || !toDate) && <p className="save-status"><CircleAlert size={15} /> Indique as datas de início e de fim.</p>}
      {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
      {loading && !report && <p className="save-status">A montar o cruzamento…</p>}
    </section>

    {report && <>
      <section className="panel">
        <div className="panel-heading"><div><h2>Compras</h2><p>{report.purchases.efaturaCount} {report.purchases.efaturaCount === 1 ? 'fatura' : 'faturas'} do e-Fatura · {report.purchases.accountingEntryCount} {report.purchases.accountingEntryCount === 1 ? 'lançamento' : 'lançamentos'} de compra · {report.purchases.matchedCount} com correspondência</p></div>
          {report.purchases.balanced
            ? <span className="badge success"><CircleCheck size={13} /> Base e IVA batem certo</span>
            : <span className="badge warning"><CircleAlert size={13} /> Δ base {signed(report.purchases.baseDelta)} · Δ IVA {signed(report.purchases.vatDelta)}</span>}
        </div>
        <div className="table-wrap"><table><thead><tr><th>Mês</th><th className="numeric">e-Fatura base</th><th className="numeric">e-Fatura IVA</th><th className="numeric">Contab. base</th><th className="numeric">Contab. IVA</th><th className="numeric">Δ base</th><th className="numeric">Δ IVA</th></tr></thead><tbody>
          {report.purchases.months.map((month) => <tr key={`${month.year}-${month.month}`}>
            <td>{monthLabel(month.year, month.month)}</td>
            <td className="numeric">{euro.format(month.efaturaBase)}</td>
            <td className="numeric">{euro.format(month.efaturaVat)}</td>
            <td className="numeric">{euro.format(month.accountingBase)}</td>
            <td className="numeric">{euro.format(month.accountingVat)}</td>
            <td className={`numeric${month.baseDelta === 0 ? '' : ' strong'}`}>{signed(month.baseDelta)}</td>
            <td className={`numeric${month.vatDelta === 0 ? '' : ' strong'}`}>{signed(month.vatDelta)}</td>
          </tr>)}
          {report.purchases.months.length === 0 && <tr><td colSpan={7} className="empty-state">Sem compras no período.</td></tr>}
        </tbody></table></div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Faturas do e-Fatura sem correspondência</h2><p>Faturas que o e-Fatura conhece e não se encontraram lançadas como compra.</p></div><span className="attention-count">{report.purchases.efaturaWithoutAccounting.length}</span></div>
        {report.purchases.efaturaWithoutAccounting.length === 0
          ? <p className="save-status"><CircleCheck size={15} /> Todas as faturas do e-Fatura têm correspondência.</p>
          : <div className="table-wrap"><table><thead><tr><th>Data</th><th>Fornecedor</th><th>Fatura</th><th className="numeric">Base</th><th className="numeric">IVA</th><th className="numeric">Total</th><th><span className="sr-only">Abrir</span></th></tr></thead><tbody>
            {report.purchases.efaturaWithoutAccounting.map((invoice) => <tr key={invoice.id}>
              <td>{formatMovementDate(invoice.invoiceDate)}</td>
              <td>{invoice.supplierName}<small>{invoice.supplierTaxId}</small></td>
              <td>{invoice.invoiceNo}{invoice.atcud ? <small>ATCUD {invoice.atcud}</small> : null}</td>
              <td className="numeric">{euro.format(invoice.taxBase)}</td>
              <td className="numeric">{euro.format(invoice.taxAmount)}</td>
              <td className="numeric strong">{euro.format(invoice.grossTotal)}</td>
              <td><Link className="text-link" to="/compras-efatura">e-Fatura</Link></td>
            </tr>)}
          </tbody></table></div>}
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Lançamentos de compra sem fatura no e-Fatura</h2><p>Lançamentos com gasto e/ou IVA dedutível que não casaram com nenhuma fatura do e-Fatura.</p></div><span className="attention-count">{report.purchases.accountingWithoutEfatura.length}</span></div>
        {report.purchases.accountingWithoutEfatura.length === 0
          ? <p className="save-status"><CircleCheck size={15} /> Todos os lançamentos de compra têm fatura no e-Fatura.</p>
          : <div className="table-wrap"><table><thead><tr><th>Data</th><th>Documento</th><th>Descrição</th><th className="numeric">Base</th><th className="numeric">IVA dedutível</th><th className="numeric">Total</th><th><span className="sr-only">Abrir</span></th></tr></thead><tbody>
            {report.purchases.accountingWithoutEfatura.map((entry) => <tr key={entry.reference}>
              <td>{formatMovementDate(entry.entryDate)}</td>
              <td>{entry.documentReference || '—'}<small>{entry.reference.replaceAll('-', ' ')}</small></td>
              <td>{entry.description}</td>
              <td className="numeric">{euro.format(entry.purchaseBase)}</td>
              <td className="numeric">{euro.format(entry.deductibleVat)}</td>
              <td className="numeric strong">{euro.format(entry.total)}</td>
              <td><Link className="text-link" to={`/movimentos/${entry.reference}`}>Movimento</Link></td>
            </tr>)}
          </tbody></table></div>}
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Vendas</h2><p>Rendimentos (classe 71/72) e IVA liquidado (2433) da contabilidade, por mês. A conferência ao nível do documento depende do SAF-T de faturação.</p></div><span className="attention-count">{euro.format(report.sales.total)}</span></div>
        <div className="table-wrap"><table><thead><tr><th>Mês</th><th className="numeric">Rendimentos</th><th className="numeric">IVA liquidado</th><th className="numeric">Total</th></tr></thead><tbody>
          {report.sales.months.map((month) => <tr key={`${month.year}-${month.month}`}>
            <td>{monthLabel(month.year, month.month)}</td>
            <td className="numeric">{euro.format(month.income)}</td>
            <td className="numeric">{euro.format(month.outputVat)}</td>
            <td className="numeric strong">{euro.format(month.total)}</td>
          </tr>)}
          {report.sales.months.length === 0 && <tr><td colSpan={4} className="empty-state">Sem vendas lançadas no período.</td></tr>}
        </tbody></table></div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>SAF-T de faturação (opcional)</h2><p>Carregue o SAF-T de faturação do período para comparar as vendas ao nível do documento com o que está lançado.</p></div>
          <input ref={salesInvoicesInput} type="file" accept=".xml,text/xml,application/xml" className="sr-only" aria-label="Escolher SAF-T de faturação" onChange={(event) => void onSalesInvoicesFile(event.target.files?.[0])} />
          <button className="button button-secondary" type="button" onClick={() => salesInvoicesInput.current?.click()}><FileUp size={15} /> Escolher ficheiro</button>
        </div>
        {salesInvoicesError && <p className="save-status error"><CircleAlert size={15} /> {salesInvoicesError}</p>}
        {salesInvoices && !salesInvoices.summary.hasSalesInvoices && <p className="save-status"><CircleAlert size={15} /> {salesInvoices.fileName}: sem bloco de faturas de venda.</p>}
        {salesInvoices?.summary.hasSalesInvoices && (() => {
          const saft = salesInvoices.summary
          const keys = Array.from(new Set([
            ...saft.months.map((month) => month.year * 12 + (month.month - 1)),
            ...report.sales.months.map((month) => month.year * 12 + (month.month - 1)),
          ])).sort((a, b) => a - b)
          const overlaps = saft.months.some((s) => report.sales.months.some((a) => a.year === s.year && a.month === s.month))
          return <>
            {!overlaps && <p className="save-status"><CircleAlert size={15} /> O SAF-T de faturação não cobre nenhum mês do período escolhido.</p>}
            <p className="save-status"><CircleCheck size={15} /> {salesInvoices.fileName}: {saft.invoiceCount} {saft.invoiceCount === 1 ? 'fatura' : 'faturas'}{saft.voidedCount > 0 ? ` (${saft.voidedCount} anuladas)` : ''} · base {euro.format(saft.net)} · IVA {euro.format(saft.tax)}{saft.declaredTotalCredit != null ? ` · TotalCredit declarado ${euro.format(saft.declaredTotalCredit)}` : ''}</p>
            <div className="table-wrap"><table><thead><tr><th>Mês</th><th className="numeric">SAF-T base</th><th className="numeric">Contab. rendimentos</th><th className="numeric">Δ base</th><th className="numeric">SAF-T IVA</th><th className="numeric">Contab. IVA liq.</th><th className="numeric">Δ IVA</th></tr></thead><tbody>
              {keys.map((key) => {
                const year = Math.floor(key / 12)
                const month = (key % 12) + 1
                const s = saft.months.find((entry) => entry.year === year && entry.month === month)
                const a = report.sales.months.find((entry) => entry.year === year && entry.month === month)
                const baseDelta = Number(((a?.income ?? 0) - (s?.net ?? 0)).toFixed(2))
                const vatDelta = Number(((a?.outputVat ?? 0) - (s?.tax ?? 0)).toFixed(2))
                return <tr key={key}>
                  <td>{monthLabel(year, month)}</td>
                  <td className="numeric">{euro.format(s?.net ?? 0)}</td>
                  <td className="numeric">{euro.format(a?.income ?? 0)}</td>
                  <td className={`numeric${baseDelta === 0 ? '' : ' strong'}`}>{signed(baseDelta)}</td>
                  <td className="numeric">{euro.format(s?.tax ?? 0)}</td>
                  <td className="numeric">{euro.format(a?.outputVat ?? 0)}</td>
                  <td className={`numeric${vatDelta === 0 ? '' : ' strong'}`}>{signed(vatDelta)}</td>
                </tr>
              })}
            </tbody></table></div>
          </>
        })()}
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Totais do período</h2><p>Débito e crédito dos lançamentos não anulados — a mesma verificação da importação do SAF-T de contabilidade.</p></div>
          {report.totals.balanced
            ? <span className="badge success"><CircleCheck size={13} /> Débito = crédito</span>
            : <span className="badge warning"><CircleAlert size={13} /> Diferença de {euro.format(report.totals.periodDebit - report.totals.periodCredit)}</span>}
        </div>
        <div className="table-wrap"><table><tbody>
          <tr><td>Débito</td><td className="numeric strong">{euro.format(report.totals.periodDebit)}</td></tr>
          <tr><td>Crédito</td><td className="numeric strong">{euro.format(report.totals.periodCredit)}</td></tr>
        </tbody></table></div>
      </section>
    </>}
  </div>
}
