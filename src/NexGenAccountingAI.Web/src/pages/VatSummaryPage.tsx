import { Database, Download, Info, Receipt, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'
import { signalLoadFailure } from '../app/loadFailure'

type Row = { accountCode: string; accountName: string; debit: number; credit: number }
type TrialBalance = { rows: (Row & { openingBalance: number; balance: number })[] }

// Contas de IVA no SNC: 2432 dedutível, 2433 liquidado, 2434 apuramento,
// 2435 IVA a pagar, 2436 IVA a recuperar. As subcontas herdam o prefixo.
const IVA_PREFIX = '243'

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, from: string, to: string): Row[] {
  const map = new Map<string, Row>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (from && movement.date < from) continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: nameOf(line.account), debit: 0, credit: 0 }
      row.debit += line.debit
      row.credit += line.credit
      map.set(line.account, row)
    }
  }
  return [...map.values()]
}

export function VatSummaryPage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  // O resumo de IVA é sempre de um período; arranca no exercício da empresa
  // em vez de somar todos os anos. «Limpar datas» abre tudo.
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, activeCompany.companyId)
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [rows, setRows] = useState<Row[]>(() => fromMovements(movements, nameOf, '', ''))

  useEffect(() => {
    const controller = new AbortController()
    if (source !== 'database') {
      setRows(fromMovements(movements, nameOf, fromDate, toDate))
      return () => controller.abort()
    }
    const params = new URLSearchParams()
    if (fromDate) params.set('from', fromDate)
    if (toDate) params.set('to', toDate)
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
      .then((result) => setRows(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, debit: row.debit, credit: row.credit }))))
      .catch(() => { if (!controller.signal.aborted) { signalLoadFailure(); setRows(fromMovements([], nameOf, fromDate, toDate)) } })
    return () => controller.abort()
  }, [activeCompany.companyId, fromDate, movements, nameOf, source, toDate, user.id])

  const ivaRows = useMemo(
    () => rows.filter((row) => row.accountCode.startsWith(IVA_PREFIX)).sort((a, b) => a.accountCode.localeCompare(b.accountCode, 'pt-PT')),
    [rows],
  )
  const sumBy = (prefix: string, pick: (row: Row) => number) => ivaRows.filter((row) => row.accountCode.startsWith(prefix)).reduce((total, row) => total + pick(row), 0)
  const dedutivel = sumBy('2432', (row) => row.debit - row.credit)
  const liquidado = sumBy('2433', (row) => row.credit - row.debit)
  const posicao = liquidado - dedutivel
  const posicaoLabel = posicao > 0 ? 'A entregar ao Estado' : posicao < 0 ? 'A recuperar ou a reportar' : 'Nulo'

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const shown = term ? ivaRows.filter((row) => `${row.accountCode} ${row.accountName}`.toLocaleLowerCase('pt-PT').includes(term)) : ivaRows

  const exportCsv = () => {
    const body: (string | number)[][] = [['Conta', 'Descrição', 'Débito', 'Crédito', 'Saldo']]
    for (const row of shown) body.push([row.accountCode, row.accountName, row.debit, row.credit, row.debit - row.credit])
    body.push(['IVA dedutível (2432)', '', '', '', dedutivel])
    body.push(['IVA liquidado (2433)', '', '', '', liquidado])
    body.push([`Posição do período — ${posicaoLabel}`, '', '', '', posicao])
    downloadCsv(csvFilename('resumo-iva', activeCompany.legalName), toCsv(body))
  }

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Resumo de IVA</h1><p>Movimento das contas de IVA (24.3) no período, sem os lançamentos anulados. É um apoio de leitura, não substitui a declaração periódica.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <ReportChrome reportKey="iva" title="Resumo de IVA" from={fromDate} to={toDate} />

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon amber"><Receipt /></span><div><small>IVA liquidado</small><strong>{euro.format(liquidado)}</strong><span>Contas 2433</span></div></article>
      <article className="metric-card"><span className="metric-icon teal"><Receipt /></span><div><small>IVA dedutível</small><strong>{euro.format(dedutivel)}</strong><span>Contas 2432</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${posicao > 0 ? 'rose' : posicao < 0 ? 'blue' : 'amber'}`}><Receipt /></span><div><small>Posição do período</small><strong>{euro.format(posicao)}</strong><span>{posicaoLabel}</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar contas de IVA</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta de IVA…" /></label>
      <label><span className="sr-only">Data inicial</span><input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      {(fromDate || toDate) && <button type="button" className="button button-secondary" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>}
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={ivaRows.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Contas de IVA</h2><p>Débito, crédito e saldo de cada conta 24.3 no período.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{shown.map((row) => <tr key={row.accountCode}>
          <td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td>
          <td>{row.accountName}</td>
          <td className="numeric">{euro.format(row.debit)}</td>
          <td className="numeric">{euro.format(row.credit)}</td>
          <td className="numeric strong">{euro.format(row.debit - row.credit)}</td>
        </tr>)}</tbody>
        <tfoot>
          <tr><td colSpan={2}>IVA liquidado (2433)</td><td /><td /><td className="numeric strong">{euro.format(liquidado)}</td></tr>
          <tr><td colSpan={2}>IVA dedutível (2432)</td><td /><td /><td className="numeric strong">{euro.format(dedutivel)}</td></tr>
          <tr><td colSpan={2}>Posição do período — {posicaoLabel}</td><td /><td /><td className="numeric strong">{euro.format(posicao)}</td></tr>
        </tfoot>
      </table></div>
      {ivaRows.length === 0 && <p className="empty-state"><Info size={15} /> Sem movimento em contas de IVA (24.3) no período.</p>}
    </section>
  </div>
}
