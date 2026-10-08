import { CircleAlert, Database, LayoutDashboard } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { BarList, Chart } from '../app/charts'
import { formatMovementDate, type FirmDashboard, type FirmDashboardCompany } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const monthNames = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez']
const percent = (share: number) => `${Math.round(share * 100)} %`
const monthLabel = (month: string) => `${monthNames[Number(month.slice(5, 7)) - 1]} ${month.slice(2, 4)}`

// A tendência da automação de uma empresa: os últimos três meses contra os três anteriores.
function trend(company: FirmDashboardCompany) {
  const last = company.automaticShareLast3Months
  const previous = company.automaticSharePrevious3Months
  if (last === null) return null
  if (previous === null) return `${percent(last)} nos últimos 3 meses`
  const arrow = last > previous ? '▲' : last < previous ? '▼' : '='
  return `${arrow} ${percent(last)} nos últimos 3 meses (antes ${percent(previous)})`
}

export function FirmDashboardPage() {
  const { source, firmDashboard, selectCompany, user } = useWorkContext()
  const navigate = useNavigate()
  const onDatabase = source === 'database'
  const [dashboard, setDashboard] = useState<FirmDashboard | null>(null)
  const [error, setError] = useState('')
  // A carteira: as empresas de que um responsável do gabinete trata (F3-28C).
  const [responsible, setResponsible] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setDashboard(null); return }
    const controller = new AbortController()
    void firmDashboard(responsible || undefined)
      .then((value) => { if (!controller.signal.aborted) setDashboard(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o painel.') })
    return () => controller.abort()
  }, [firmDashboard, onDatabase, responsible])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Painel do gabinete</h1><p>Todas as empresas, pelo que precisa de atenção.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><LayoutDashboard /></span><h2>Requer ligação à base de dados</h2><p>O painel junta o calendário, os lançamentos por conferir, as compras e os bancos de cada empresa.</p></section>
    </div>
  }

  // Abre a empresa e o ecrã onde está o trabalho.
  const open = (company: FirmDashboardCompany, path: string) => {
    selectCompany(company.companyId)
    navigate(path)
  }

  const companies = dashboard?.companies ?? []
  const totals = companies.reduce((sum, company) => ({
    overdue: sum.overdue + company.overdueObligations,
    dueSoon: sum.dueSoon + company.dueSoonObligations,
    pending: sum.pending + company.pendingReview,
    purchases: sum.purchases + company.purchasesToPost,
    bank: sum.bank + company.unreconciledBankLines,
  }), { overdue: 0, dueSoon: 0, pending: 0, purchases: 0, bank: 0 })
  const automation = dashboard?.automation ?? []
  const automaticTotal = automation.reduce((sum, month) => sum + month.automatic, 0)
  const sourceTotals = automaticTotal === 0 ? [] : [
    { label: 'SAF-T de contabilidade importado', amount: automation.reduce((sum, month) => sum + month.saftImport, 0) },
    { label: 'Compras do e-Fatura', amount: automation.reduce((sum, month) => sum + month.eFatura, 0) },
    { label: 'Vendas do SAF-T de faturação', amount: automation.reduce((sum, month) => sum + month.saftBilling, 0) },
    { label: 'Extrato bancário', amount: automation.reduce((sum, month) => sum + month.bankStatement, 0) },
  ].filter((item) => item.amount > 0).map((item) => ({ ...item, share: (item.amount / automaticTotal) * 100 }))
  const count = (value: number, path: string, company: FirmDashboardCompany, warning = false) =>
    value === 0 ? <span className="muted-zero">0</span> : <button className="text-link" type="button" onClick={() => open(company, path)}>{warning ? <span className="badge warning">{value}</span> : value}</button>

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Painel do gabinete</h1><p>As empresas a que tem acesso, da que mais precisa de atenção para a que menos: obrigações em atraso e a vencer nos próximos {dashboard?.dueSoonDays ?? 15} dias, lançamentos por conferir, faturas do e-Fatura por lançar (últimos três meses), movimentos bancários por conciliar e a parte dos lançamentos que já não é feita à mão.</p></div></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    {dashboard && (dashboard.responsibles?.length ?? 0) > 0 && <div className="builder-row">
      <label>Carteira <select aria-label="Carteira" value={responsible} onChange={(event) => setResponsible(event.target.value)}>
        <option value="">Todas as empresas</option>
        {dashboard.responsibles?.map((item) => <option key={item.userId} value={item.userId}>{item.userId === user.id ? `A minha (${item.displayName})` : item.displayName} · {item.companies} {item.companies === 1 ? 'empresa' : 'empresas'}</option>)}
      </select></label>
    </div>}

    {dashboard && <section className="metadata-strip">
      <span><small>Empresas</small><strong>{companies.length}</strong></span>
      <span><small>Obrigações em atraso</small><strong>{totals.overdue > 0 ? <span className="badge warning">{totals.overdue}</span> : 0}</strong></span>
      <span><small>A vencer</small><strong>{totals.dueSoon}</strong></span>
      <span><small>Por conferir</small><strong>{totals.pending}</strong></span>
      <span><small>Compras por lançar</small><strong>{totals.purchases}</strong></span>
      <span><small>Bancos por conciliar</small><strong>{totals.bank}</strong></span>
    </section>}

    {dashboard && companies.length > 0 && <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Empresa</th><th className="numeric">Em atraso</th><th>Próxima obrigação</th><th className="numeric">Por conferir</th><th className="numeric">Compras por lançar</th><th className="numeric">Bancos por conciliar</th><th className="numeric">Tarefas</th><th className="numeric">Automáticos (12 meses)</th></tr></thead>
        <tbody>{companies.map((company) => <tr key={company.companyId}>
          <td><button className="text-link" type="button" onClick={() => open(company, '/')}>{company.legalName}</button><small>NIF {company.taxRegistrationNumber}{company.responsible && company.responsible.length > 0 ? ` · responsável: ${company.responsible.map((item) => item.displayName).join(', ')}` : ''}</small></td>
          <td className="numeric">{count(company.overdueObligations, '/obrigacoes', company, true)}</td>
          <td>{company.nextObligation ? <><button className="text-link" type="button" onClick={() => open(company, '/obrigacoes')}>{company.nextObligation}</button><small>{company.nextDueDate ? formatMovementDate(company.nextDueDate) : ''}{company.dueSoonObligations > 1 ? ` · mais ${company.dueSoonObligations - 1}` : ''}</small></> : <span className="muted-zero">—</span>}</td>
          <td className="numeric">{count(company.pendingReview, '/movimentos', company)}</td>
          <td className="numeric">{count(company.purchasesToPost, '/lancamentos-sugeridos', company)}</td>
          <td className="numeric">{count(company.unreconciledBankLines, '/bancos', company)}</td>
          <td className="numeric">{count(company.openTasks ?? 0, '/tarefas-gabinete', company)}{(company.overdueTasks ?? 0) > 0 && <small><span className="badge warning">{company.overdueTasks} em atraso</span></small>}</td>
          <td className="numeric">{company.entriesLast12Months === 0 ? '—' : percent(company.automaticShare)}<small>{company.automaticEntriesLast12Months} de {company.entriesLast12Months}</small>{trend(company) && <small>{trend(company)}</small>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {automation.some((month) => month.entries > 0) && <section className="panel">
      <div className="panel-heading"><div><h2>Evolução da automação</h2><p>Os lançamentos de todas as empresas do painel, mês a mês, feitos à mão e criados por importação ou sugestão. Calculado a partir dos próprios lançamentos, pela origem de cada um.</p></div></div>
      <div className="chart-grid-layout">
        <Chart title="Lançamentos por mês: à mão e automáticos" labels={automation.map((month) => monthLabel(month.month))} format={(value) => String(Math.round(value))} height={230} series={[
          { name: 'À mão', kind: 'bar', tone: 'muted', values: automation.map((month) => month.manual) },
          { name: 'Automáticos', kind: 'bar', tone: 'primary', values: automation.map((month) => month.automatic) },
        ]} />
        <Chart title="Parte automática por mês" labels={automation.map((month) => monthLabel(month.month))} format={(value) => percent(value)} height={230} series={[
          { name: 'Automáticos', kind: 'line', tone: 'accent', values: automation.map((month) => month.automaticShare) },
        ]} />
        {sourceTotals.length > 0 && <BarList title="Automáticos nos doze meses, pela origem" items={sourceTotals} format={(value) => String(value)} />}
      </div>
    </section>}

    {dashboard && companies.length === 0 && <section className="panel empty-state"><h2>Sem empresas</h2><p>{responsible ? 'Nenhuma das suas empresas tem este responsável.' : 'Não tem acesso a nenhuma empresa ativa.'}</p></section>}

    <p className="statement-note">«Automáticos» são os lançamentos dos últimos doze meses criados por importação do SAF-T, por sugestão do e-Fatura ou do SAF-T de faturação, ou a partir do extrato — sem a abertura nem o apuramento. As obrigações vêm do calendário de cada empresa; a ordem dá mais peso às que estão em atraso.</p>
  </div>
}
