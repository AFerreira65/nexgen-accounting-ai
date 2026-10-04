import { ChevronDown, ChevronRight, CircleAlert, CircleCheck, Database, Download, Landmark, Printer } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type BalanceSheetStatement, type CashFlowLine, type CashFlowStatement, type EquityChangesStatement, type IncomeStatementByNature, type StatementLine } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

type Tab = 'balance' | 'income' | 'cash' | 'equity'

const tabs: { key: Tab; label: string; file: string }[] = [
  { key: 'balance', label: 'Balanço', file: 'balanco-snc' },
  { key: 'income', label: 'Demonstração de resultados', file: 'demonstracao-resultados-snc' },
  { key: 'cash', label: 'Fluxos de caixa', file: 'fluxos-de-caixa' },
  { key: 'equity', label: 'Alterações no capital próprio', file: 'alteracoes-capital-proprio' },
]

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

function CashFlowTable({ lines }: { lines: CashFlowLine[] }) {
  const [open, setOpen] = useState<Set<string>>(new Set())
  const toggle = (key: string) => setOpen((current) => {
    const next = new Set(current)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })
  return <div className="table-wrap"><table>
    <thead><tr><th>Rubricas</th><th className="numeric">Período</th><th className="numeric">Período anterior</th></tr></thead>
    <tbody>{lines.map((line, index) => {
      const key = `${line.section}-${line.label}`
      const startsSection = line.section !== '' && (index === 0 || lines[index - 1].section !== line.section)
      const expanded = open.has(key)
      return <Fragment key={key}>
        {startsSection && <tr className="analyses-subtotal"><td colSpan={3}>{line.section}</td></tr>}
        <tr className={line.isTotal ? 'subtotal-row' : undefined}>
          <td>{line.accounts.length > 0
            ? <button type="button" className="row-toggle" aria-expanded={expanded} onClick={() => toggle(key)}>{expanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}{line.label}</button>
            : line.label}</td>
          <td className="numeric">{euro.format(line.amount)}</td>
          <td className="numeric">{euro.format(line.comparative)}</td>
        </tr>
        {expanded && line.accounts.map((account) => <tr key={`${key}-${account.accountCode}`} className="detail-row">
          <td><Link to={`/contas/${account.accountCode}`}>{account.accountCode}</Link> {account.accountName}</td>
          <td className="numeric">{euro.format(account.amount)}</td>
          <td />
        </tr>)}
      </Fragment>
    })}</tbody>
  </table></div>
}

function EquityTable({ statement }: { statement: EquityChangesStatement }) {
  return <div className="table-wrap"><table>
    <thead><tr><th>Descrição</th>{statement.columns.map((column) => <th key={column} className="numeric">{column}</th>)}<th className="numeric">Total do capital próprio</th></tr></thead>
    <tbody>{statement.rows.map((row) => <tr key={row.label} className={row.isTotal ? 'subtotal-row' : undefined}>
      <td>{row.label}</td>
      {row.amounts.map((amount, index) => <td key={statement.columns[index]} className="numeric">{amount === 0 ? '—' : euro.format(amount)}</td>)}
      <td className="numeric strong">{euro.format(row.total)}</td>
    </tr>)}</tbody>
  </table></div>
}

export function FinancialStatementsPage() {
  const { activeCompany, source, balanceSheetStatement, incomeStatementByNature, cashFlowStatement, equityChangesStatement } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const [tab, setTab] = useState<Tab>('balance')
  const [balance, setBalance] = useState<BalanceSheetStatement | null>(null)
  const [income, setIncome] = useState<IncomeStatementByNature | null>(null)
  const [cash, setCash] = useState<CashFlowStatement | null>(null)
  const [equity, setEquity] = useState<EquityChangesStatement | null>(null)
  const sameYear = fromDate.slice(0, 4) === toDate.slice(0, 4)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setBalance(null); setIncome(null); setCash(null); setEquity(null); return }
    const controller = new AbortController()
    setError('')
    const period = { from: fromDate, to: toDate }
    void Promise.all([
      balanceSheetStatement(companyId, toDate),
      incomeStatementByNature(companyId, period),
      cashFlowStatement(companyId, period),
      sameYear ? equityChangesStatement(companyId, period) : Promise.resolve(null),
    ])
      .then(([balanceValue, incomeValue, cashValue, equityValue]) => {
        if (controller.signal.aborted) return
        setBalance(balanceValue); setIncome(incomeValue); setCash(cashValue); setEquity(equityValue)
      })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as demonstrações financeiras.') })
    return () => controller.abort()
  }, [balanceSheetStatement, incomeStatementByNature, cashFlowStatement, equityChangesStatement, companyId, onDatabase, fromDate, toDate, sameYear])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Demonstrações financeiras</h1><p>Balanço, demonstração de resultados, fluxos de caixa e alterações no capital próprio nos modelos do SNC.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Landmark /></span><h2>Requer ligação à base de dados</h2><p>As demonstrações são montadas a partir das taxonomias das contas na base de dados da empresa.</p></section>
    </div>
  }

  const statement = tab === 'balance' ? balance : tab === 'income' ? income : null
  const excluded = statement?.excluded ?? []
  const loaded = tab === 'balance' ? balance !== null : tab === 'income' ? income !== null : tab === 'cash' ? cash !== null : equity !== null
  const exportCsv = () => {
    const lines = tab === 'cash' ? cash?.lines : statement?.lines
    const rows = tab === 'equity'
      ? equity && [['Descrição', ...equity.columns, 'Total do capital próprio'], ...equity.rows.map((row) => [row.label, ...row.amounts, row.total])]
      : lines && [['Secção', 'Rubrica', 'Período', 'Período anterior'], ...lines.map((line) => [line.section, line.label, line.amount, line.comparative])]
    if (!rows) return
    downloadCsv(csvFilename(tabs.find((item) => item.key === tab)!.file, activeCompany.legalName), toCsv(rows))
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Demonstrações financeiras</h1><p>As demonstrações nos modelos do SNC. No balanço e na demonstração de resultados cada conta entra na rubrica que a tabela da AT atribui à sua taxonomia; os fluxos de caixa e as alterações no capital próprio são classificados pelas contas de contrapartida.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <div className="period-shortcuts" role="tablist" aria-label="Demonstração">
        {tabs.map((item) => <button key={item.key} type="button" role="tab" aria-selected={tab === item.key} className={`period-chip${tab === item.key ? ' active' : ''}`} onClick={() => setTab(item.key)}>{item.label}</button>)}
      </div>
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={!loaded}><Download size={16} /> Exportar CSV</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!loaded}><Printer size={15} /> Imprimir / PDF</button>
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

    {tab === 'cash' && cash && <>
      <section className="metadata-strip">
        <span><small>Operacionais</small><strong>{euro.format(cash.operating)}</strong></span>
        <span><small>Investimento</small><strong>{euro.format(cash.investing)}</strong></span>
        <span><small>Financiamento</small><strong>{euro.format(cash.financing)}</strong></span>
        <span><small>Caixa no fim do período</small><strong>{euro.format(cash.closingCash)}</strong></span>
      </section>
      <section className="panel">
        <div className="panel-heading"><div><h2>Demonstração dos fluxos de caixa — método direto</h2><p>De {shortDate(cash.from)} a {shortDate(cash.to)}. Recebimentos positivos, pagamentos negativos. Abra uma rubrica para ver as contas de contrapartida que a formam.</p></div></div>
        <CashFlowTable lines={cash.lines} />
      </section>
      <p className="statement-note">Cada lançamento que mexe em caixa ou bancos é repartido pelas contas de contrapartida, segundo os códigos do SNC (21 clientes, 22 fornecedores, 43 ativos fixos, 25 financiamentos…). É um critério da aplicação, não uma tabela oficial: confira as rubricas pelas contas que as formam.</p>
    </>}

    {tab === 'equity' && !sameYear && <p className="save-status error"><CircleAlert size={15} /> Indique um período dentro do mesmo exercício.</p>}
    {tab === 'equity' && equity && <>
      <section className="metadata-strip">
        <span><small>Capital próprio no fim do período</small><strong>{euro.format(equity.closingEquity)}</strong></span>
        <span><small>Modelo</small><strong>{equity.model}</strong></span>
      </section>
      <section className="panel">
        <div className="panel-heading"><div><h2>Demonstração das alterações no capital próprio</h2><p>De {shortDate(equity.from)} a {shortDate(equity.to)}. A posição no fim coincide com o capital próprio do balanço.</p></div></div>
        <EquityTable statement={equity} />
      </section>
      <p className="statement-note">As linhas são classificadas pelas contas de cada lançamento: só capital próprio é transferência; com as contas 264 ou 265 é distribuição; aumentos de capital e prémios são realizações. É um critério da aplicação, a conferir.</p>
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
