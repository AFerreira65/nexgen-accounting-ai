import { CircleAlert, CircleCheck, Database, Download, Landmark, Printer } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type BalanceSheetStatement, type IncomeStatementByNature, type StatementLine } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

type Tab = 'balance' | 'income'

const shortDate = (value: string) => value.split('-').reverse().join('/')

function StatementTable({ lines, currentLabel, comparativeLabel }: { lines: StatementLine[]; currentLabel: string; comparativeLabel: string }) {
  return <div className="table-wrap"><table>
    <thead><tr><th>Rubricas</th><th className="numeric">{currentLabel}</th><th className="numeric">{comparativeLabel}</th></tr></thead>
    <tbody>{lines.map((line, index) => {
      const startsSection = line.section !== '' && (index === 0 || lines[index - 1].section !== line.section)
      return <Fragment key={`${line.section}-${line.label}`}>
        {startsSection && <tr className="analyses-subtotal"><td colSpan={3}>{line.section}</td></tr>}
        <tr className={line.isTotal ? 'subtotal-row' : undefined}>
          <td>{line.label}</td>
          <td className="numeric">{euro.format(line.amount)}</td>
          <td className="numeric">{euro.format(line.comparative)}</td>
        </tr>
      </Fragment>
    })}</tbody>
  </table></div>
}

export function FinancialStatementsPage() {
  const { activeCompany, source, balanceSheetStatement, incomeStatementByNature } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const [tab, setTab] = useState<Tab>('balance')
  const [balance, setBalance] = useState<BalanceSheetStatement | null>(null)
  const [income, setIncome] = useState<IncomeStatementByNature | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setBalance(null); setIncome(null); return }
    const controller = new AbortController()
    setError('')
    void Promise.all([balanceSheetStatement(companyId, toDate), incomeStatementByNature(companyId, { from: fromDate, to: toDate })])
      .then(([balanceValue, incomeValue]) => { if (!controller.signal.aborted) { setBalance(balanceValue); setIncome(incomeValue) } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as demonstrações financeiras.') })
    return () => controller.abort()
  }, [balanceSheetStatement, incomeStatementByNature, companyId, onDatabase, fromDate, toDate])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Demonstrações financeiras</h1><p>Balanço e demonstração de resultados por naturezas nos modelos do SNC.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Landmark /></span><h2>Requer ligação ao SQL Server</h2><p>As demonstrações são montadas a partir das taxonomias das contas na base de dados da empresa.</p></section>
    </div>
  }

  const statement = tab === 'balance' ? balance : income
  const excluded = statement?.excluded ?? []
  const exportCsv = () => {
    if (!statement) return
    downloadCsv(
      csvFilename(tab === 'balance' ? 'balanco-snc' : 'demonstracao-resultados-snc', activeCompany.legalName),
      toCsv([
        ['Secção', 'Rubrica', 'Período', 'Período anterior'],
        ...statement.lines.map((line) => [line.section, line.label, line.amount, line.comparative]),
      ]),
    )
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Demonstrações financeiras</h1><p>Balanço e demonstração de resultados por naturezas nos modelos do SNC, com o período anterior ao lado. Cada conta entra na rubrica que a tabela da AT atribui à sua taxonomia.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <div className="period-shortcuts" role="tablist" aria-label="Demonstração">
        <button type="button" role="tab" aria-selected={tab === 'balance'} className={`period-chip${tab === 'balance' ? ' active' : ''}`} onClick={() => setTab('balance')}>Balanço</button>
        <button type="button" role="tab" aria-selected={tab === 'income'} className={`period-chip${tab === 'income' ? ' active' : ''}`} onClick={() => setTab('income')}>Demonstração de resultados</button>
      </div>
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={!statement}><Download size={16} /> Exportar CSV</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!statement}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {tab === 'balance' && balance && <>
      <section className="metadata-strip">
        <span><small>Total do ativo</small><strong>{euro.format(balance.totalAssets)}</strong></span>
        <span><small>Capital próprio</small><strong>{euro.format(balance.totalEquity)}</strong></span>
        <span><small>Passivo</small><strong>{euro.format(balance.totalLiabilities)}</strong></span>
        <span><small>Modelo</small><strong>{balance.model}</strong></span>
      </section>
      {balance.difference === 0
        ? <p className="save-status success"><CircleCheck size={15} /> O ativo é igual ao capital próprio mais o passivo.</p>
        : <p className="save-status error"><CircleAlert size={15} /> O balanço não fecha: o ativo difere do capital próprio mais o passivo em {euro.format(balance.difference)}. Veja as contas fora da demonstração, em baixo.</p>}
      <section className="panel">
        <div className="panel-heading"><div><h2>Balanço em {shortDate(balance.date)}</h2><p>O balanço lê os movimentos do exercício até à data — conta com o lançamento de abertura para os saldos anteriores.</p></div></div>
        <StatementTable lines={balance.lines} currentLabel={shortDate(balance.date)} comparativeLabel={shortDate(balance.comparativeDate)} />
      </section>
    </>}

    {tab === 'income' && income && <>
      <section className="metadata-strip">
        <span><small>Resultado líquido do período</small><strong>{euro.format(income.netResult)}</strong></span>
        <span><small>Modelo</small><strong>{income.model}</strong></span>
      </section>
      <section className="panel">
        <div className="panel-heading"><div><h2>Demonstração de resultados por naturezas</h2><p>De {shortDate(income.from)} a {shortDate(income.to)}, sem os lançamentos de apuramento. Os gastos aparecem com sinal negativo.</p></div></div>
        <StatementTable lines={income.lines} currentLabel="Período" comparativeLabel="Período anterior" />
      </section>
    </>}

    {statement && excluded.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Contas fora da demonstração</h2><p>Têm saldo mas não cabem em nenhuma rubrica. Corrija a taxonomia no plano de contas ou regularize o saldo.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Motivo</th><th className="numeric">Saldo</th></tr></thead>
        <tbody>{excluded.map((account) => <tr key={account.accountCode}>
          <td><Link to={`/contas/${account.accountCode}`}>{account.accountCode}</Link> {account.accountName}</td>
          <td>{account.reason}</td>
          <td className="numeric">{euro.format(account.balance)}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {statement && <p className="statement-note">Rubricas segundo a tabela «{statement.source}». A ordem das rubricas segue os modelos da Portaria n.º 220/2015 e está por conferir; não é uma demonstração validada pela AT.</p>}
  </div>
}
