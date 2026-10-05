import { Database, Download, HandCoins, Info, Search, Users } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro } from '../app/demo-data'
import { Pager, usePagedList } from '../app/Pager'
import { ReportChrome } from '../app/ReportChrome'
import { useWorkContext } from '../app/WorkContext'
import { signalLoadFailure } from '../app/loadFailure'

type Row = { accountCode: string; accountName: string; balance: number }
type TrialBalance = { rows: { accountCode: string; accountName: string; debit: number; credit: number; balance: number }[] }

function fromMovements(movements: ReturnType<typeof useWorkContext>['movements'], nameOf: (code: string) => string, to: string): Row[] {
  const map = new Map<string, Row>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: nameOf(line.account), balance: 0 }
      row.balance += line.debit - line.credit
      map.set(line.account, row)
    }
  }
  return [...map.values()]
}

export function PartyBalancesPage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [search, setSearch] = useState('')
  const [cutoff, setCutoff] = useState('')
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [rows, setRows] = useState<Row[]>(() => fromMovements(movements, nameOf, ''))

  useEffect(() => {
    const controller = new AbortController()
    if (source !== 'database') {
      setRows(fromMovements(movements, nameOf, cutoff))
      return () => controller.abort()
    }
    const params = new URLSearchParams()
    if (cutoff) params.set('to', cutoff)
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as TrialBalance : Promise.reject(new Error()))
      .then((result) => setRows(result.rows.map((row) => ({ accountCode: row.accountCode, accountName: row.accountName, balance: row.balance }))))
      .catch(() => { if (!controller.signal.aborted) { signalLoadFailure(); setRows(fromMovements([], nameOf, cutoff)) } })
    return () => controller.abort()
  }, [activeCompany.companyId, cutoff, movements, nameOf, source, user.id])

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const pick = (prefix: string) => rows
    .filter((row) => row.accountCode.startsWith(prefix) && row.balance !== 0)
    .filter((row) => !term || `${row.accountCode} ${row.accountName}`.toLocaleLowerCase('pt-PT').includes(term))
    .sort((a, b) => a.accountCode.localeCompare(b.accountCode, 'pt-PT'))
  const clientes = pick('21')
  const fornecedores = pick('22')
  const totalClientes = clientes.reduce((sum, row) => sum + row.balance, 0)
  const totalFornecedores = fornecedores.reduce((sum, row) => sum + row.balance, 0)
  // Uma empresa importada de SAF-T tem centenas de contas de terceiros com
  // saldo; pagina-se cada painel pelo tamanho de página da empresa.
  const pageSize = activeCompany.settings.pageSize || 50
  const clientesPage = usePagedList(clientes, `${term}|${cutoff}`, pageSize)
  const fornecedoresPage = usePagedList(fornecedores, `${term}|${cutoff}`, pageSize)

  const exportCsv = () => {
    const body: (string | number)[][] = [['Grupo', 'Conta', 'Nome', 'Saldo']]
    for (const row of clientes) body.push(['Clientes', row.accountCode, row.accountName, row.balance])
    body.push(['Total de clientes', '', '', totalClientes])
    for (const row of fornecedores) body.push(['Fornecedores', row.accountCode, row.accountName, row.balance])
    body.push(['Total de fornecedores', '', '', totalFornecedores])
    downloadCsv(csvFilename('contas-correntes', activeCompany.legalName), toCsv(body))
  }

  const table = (page: ReturnType<typeof usePagedList<Row>>, total: number, emptyText: string) => <>
    <div className="table-wrap"><table>
      <thead><tr><th>Conta</th><th>Nome</th><th className="numeric">Saldo</th></tr></thead>
      <tbody>{page.paged.map((row) => <tr key={row.accountCode}>
        <td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td>
        <td>{row.accountName}</td>
        <td className="numeric strong">{euro.format(row.balance)}</td>
      </tr>)}</tbody>
      <tfoot><tr><td colSpan={2}>Total</td><td className="numeric strong">{euro.format(total)}</td></tr></tfoot>
    </table></div>
    <Pager currentPage={page.currentPage} totalPages={page.totalPages} onPage={page.setPage} />
    {page.count === 0 && <p className="empty-state"><Info size={15} /> {emptyText}</p>}
  </>

  return <div className="page-stack report-page">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Contas correntes</h1><p>Saldos das contas de movimento de clientes (21) e fornecedores (22), sem os lançamentos anulados. Saldo positivo = a receber; negativo = a pagar.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <ReportChrome reportKey="contas-correntes" title="Contas correntes" to={cutoff || undefined} />

    <section className="metric-grid compact-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><Users /></span><div><small>Total de clientes</small><strong>{euro.format(totalClientes)}</strong><span>{clientes.length} contas com saldo</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><HandCoins /></span><div><small>Total de fornecedores</small><strong>{euro.format(totalFornecedores)}</strong><span>{fornecedores.length} contas com saldo</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><Database /></span><div><small>Saldo líquido</small><strong>{euro.format(totalClientes + totalFornecedores)}</strong><span>Clientes + fornecedores</span></div></article>
    </section>

    <div className="filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar contas correntes</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou nome…" /></label>
      <label><span className="sr-only">Saldos até</span><input type="date" aria-label="Saldos até" value={cutoff} onChange={(event) => setCutoff(event.target.value)} /></label>
      {cutoff && <button className="button button-secondary" type="button" onClick={() => setCutoff('')}>Limpar data</button>}
      <button type="button" className="button button-secondary" onClick={exportCsv} disabled={clientes.length === 0 && fornecedores.length === 0}><Download size={15} /> Exportar CSV</button>
    </div>

    <section className="panel">
      <div className="panel-heading"><div><h2>Clientes</h2><p>Contas de movimento sob a classe 21.</p></div></div>
      {table(clientesPage, totalClientes, 'Sem saldos de clientes.')}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Fornecedores</h2><p>Contas de movimento sob a classe 22.</p></div></div>
      {table(fornecedoresPage, totalFornecedores, 'Sem saldos de fornecedores.')}
    </section>
  </div>
}
