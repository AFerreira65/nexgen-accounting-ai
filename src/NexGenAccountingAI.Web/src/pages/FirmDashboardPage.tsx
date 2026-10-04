import { CircleAlert, Database, LayoutDashboard } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatMovementDate, type FirmDashboard, type FirmDashboardCompany } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

export function FirmDashboardPage() {
  const { source, firmDashboard, selectCompany } = useWorkContext()
  const navigate = useNavigate()
  const onDatabase = source === 'database'
  const [dashboard, setDashboard] = useState<FirmDashboard | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setDashboard(null); return }
    const controller = new AbortController()
    void firmDashboard()
      .then((value) => { if (!controller.signal.aborted) setDashboard(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o painel.') })
    return () => controller.abort()
  }, [firmDashboard, onDatabase])

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
  const count = (value: number, path: string, company: FirmDashboardCompany, warning = false) =>
    value === 0 ? <span className="muted-zero">0</span> : <button className="text-link" type="button" onClick={() => open(company, path)}>{warning ? <span className="badge warning">{value}</span> : value}</button>

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Painel do gabinete</h1><p>As empresas a que tem acesso, da que mais precisa de atenção para a que menos: obrigações em atraso e a vencer nos próximos {dashboard?.dueSoonDays ?? 15} dias, lançamentos por conferir, faturas do e-Fatura por lançar (últimos três meses), movimentos bancários por conciliar e a parte dos lançamentos que já não é feita à mão.</p></div></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

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
        <thead><tr><th>Empresa</th><th className="numeric">Em atraso</th><th>Próxima obrigação</th><th className="numeric">Por conferir</th><th className="numeric">Compras por lançar</th><th className="numeric">Bancos por conciliar</th><th className="numeric">Automáticos (12 meses)</th></tr></thead>
        <tbody>{companies.map((company) => <tr key={company.companyId}>
          <td><button className="text-link" type="button" onClick={() => open(company, '/')}>{company.legalName}</button><small>NIF {company.taxRegistrationNumber}</small></td>
          <td className="numeric">{count(company.overdueObligations, '/obrigacoes', company, true)}</td>
          <td>{company.nextObligation ? <><button className="text-link" type="button" onClick={() => open(company, '/obrigacoes')}>{company.nextObligation}</button><small>{company.nextDueDate ? formatMovementDate(company.nextDueDate) : ''}{company.dueSoonObligations > 1 ? ` · mais ${company.dueSoonObligations - 1}` : ''}</small></> : <span className="muted-zero">—</span>}</td>
          <td className="numeric">{count(company.pendingReview, '/movimentos', company)}</td>
          <td className="numeric">{count(company.purchasesToPost, '/lancamentos-sugeridos', company)}</td>
          <td className="numeric">{count(company.unreconciledBankLines, '/bancos', company)}</td>
          <td className="numeric">{company.entriesLast12Months === 0 ? '—' : `${Math.round(company.automaticShare * 100)} %`}<small>{company.automaticEntriesLast12Months} de {company.entriesLast12Months}</small></td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {dashboard && companies.length === 0 && <section className="panel empty-state"><h2>Sem empresas</h2><p>Não tem acesso a nenhuma empresa ativa.</p></section>}

    <p className="statement-note">«Automáticos» são os lançamentos dos últimos doze meses criados por importação do SAF-T, por sugestão do e-Fatura ou do SAF-T de faturação, ou a partir do extrato — sem a abertura nem o apuramento. As obrigações vêm do calendário de cada empresa; a ordem dá mais peso às que estão em atraso.</p>
  </div>
}
