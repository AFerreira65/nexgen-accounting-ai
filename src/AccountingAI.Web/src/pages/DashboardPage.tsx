import { AlertTriangle, ArrowRight, Clock3, Database, HandCoins, Landmark, Receipt, Scale, TrendingUp, Users, Wallet } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

type Balance = { accountCode: string; balance: number }

function balancesFromMovements(movements: ReturnType<typeof useWorkContext>['movements']): Balance[] {
  const map = new Map<string, number>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    for (const line of movement.lines) {
      map.set(line.account, (map.get(line.account) ?? 0) + line.debit - line.credit)
    }
  }
  return [...map.entries()].map(([accountCode, balance]) => ({ accountCode, balance }))
}

export function DashboardPage() {
  const { accountingSource, activeCompany, movements, source, user } = useWorkContext()
  const active = movements.filter((movement) => movement.status !== 'Anulado')
  const pending = movements.filter((movement) => movement.status === 'Por conferir')
  const firstName = user.displayName.replace(' (Demonstração)', '').split(' ')[0]
  const [balances, setBalances] = useState<Balance[]>(() => balancesFromMovements(movements))

  useEffect(() => {
    if (source !== 'database') {
      setBalances(balancesFromMovements(movements))
      return
    }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as { rows: Balance[] } : Promise.reject(new Error()))
      .then((result) => setBalances(result.rows.map((row) => ({ accountCode: row.accountCode, balance: row.balance }))))
      .catch(() => { if (!controller.signal.aborted) setBalances(balancesFromMovements(movements)) })
    return () => controller.abort()
  }, [activeCompany.companyId, movements, source, user.id])

  const { netResult, totalAssets, cashAndBank, receivables, payables, vatPosition } = useMemo(() => {
    let result = 0
    let assets = 0
    let cash = 0
    let toReceive = 0
    let toPay = 0
    let vatDeductible = 0
    let vatCollected = 0
    for (const { accountCode, balance } of balances) {
      const klass = accountCode.charAt(0)
      if (klass === '6' || klass === '7') result -= balance
      if (klass === '1' || klass === '3' || klass === '4' || (klass === '2' && balance > 0)) assets += balance
      if (accountCode.startsWith('11') || accountCode.startsWith('12')) cash += balance
      if (accountCode.startsWith('21')) toReceive += balance
      if (accountCode.startsWith('22')) toPay += balance
      if (accountCode.startsWith('2432')) vatDeductible += balance
      if (accountCode.startsWith('2433')) vatCollected -= balance
    }
    return { netResult: result, totalAssets: assets, cashAndBank: cash, receivables: toReceive, payables: toPay, vatPosition: vatCollected - vatDeductible }
  }, [balances])

  return (
    <div className="page-stack">
      <header className="page-heading dashboard-heading">
        <div><p className="eyebrow">Núcleo contabilístico local</p><h1>Bom dia, {firstName}</h1><p>Dados de teste persistidos, com acesso direto ao movimento, diário e conta.</p></div>
        <Link className="button button-primary" to="/movimentos"><Database size={17} /> Consultar dados</Link>
      </header>

      <section className="metric-grid" aria-label="Indicadores do período">
        <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Movimentos</small><strong>{active.length}</strong><span>{accountingSource === 'database' ? 'Lidos do SQL Server' : 'Fallback visual local'}</span></div></article>
        <article className="metric-card"><span className={`metric-icon ${netResult >= 0 ? 'blue' : 'rose'}`}><TrendingUp /></span><div><small>Resultado</small><strong>{euro.format(netResult)}</strong><span>{netResult >= 0 ? 'Lucro acumulado' : 'Prejuízo acumulado'}</span></div></article>
        <article className="metric-card"><span className="metric-icon blue"><Wallet /></span><div><small>Total do ativo</small><strong>{euro.format(totalAssets)}</strong><span>Classes 1, 3, 4 e 2 devedora</span></div></article>
        <article className="metric-card"><span className="metric-icon amber"><Clock3 /></span><div><small>Por conferir</small><strong>{pending.length}</strong><span>Movimentos pendentes de conferência</span></div></article>
      </section>

      <section className="metric-grid" aria-label="Posição financeira">
        <Link className="metric-card" to="/balancete"><span className="metric-icon navy"><Wallet /></span><div><small>Caixa e bancos</small><strong>{euro.format(cashAndBank)}</strong><span>Contas 11 e 12</span></div></Link>
        <Link className="metric-card" to="/contas-correntes"><span className="metric-icon teal"><Users /></span><div><small>Saldo de clientes</small><strong>{euro.format(receivables)}</strong><span>Classe 21 · a receber</span></div></Link>
        <Link className="metric-card" to="/contas-correntes"><span className="metric-icon amber"><HandCoins /></span><div><small>Saldo de fornecedores</small><strong>{euro.format(payables)}</strong><span>Classe 22 · a pagar</span></div></Link>
        <Link className="metric-card" to="/iva"><span className="metric-icon navy"><Receipt /></span><div><small>Posição de IVA</small><strong>{euro.format(vatPosition)}</strong><span>{vatPosition > 0 ? 'A entregar ao Estado' : vatPosition < 0 ? 'A recuperar ou a reportar' : 'Nulo'}</span></div></Link>
      </section>

      <div className="dashboard-grid">
        <section className="panel">
          <div className="panel-heading"><div><h2>Movimentos recentes</h2><p>Últimas operações da empresa selecionada</p></div><Link className="text-link" to="/movimentos">Ver todos <ArrowRight size={15} /></Link></div>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th></tr></thead>
              <tbody>{movements.map((movement) => <tr key={movement.id}>
                <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td>
                <td>{movement.description}<small>{movement.document}</small></td>
                <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}><Landmark size={13} /> {movement.journalId}</Link></td>
                <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : movement.status === 'Anulado' ? 'neutral' : 'warning'}`}>{movement.status}</span></td>
                <td className="numeric strong">{euro.format(movement.amount)}</td>
              </tr>)}</tbody>
            </table>
          </div>
        </section>

        <aside className="panel attention-panel">
          <div className="panel-heading"><div><h2>A sua atenção</h2><p>Movimentos por conferir</p></div><span className="attention-count">{pending.length}</span></div>
          {pending.map((movement) => <Link className="attention-item" key={movement.id} to={`/movimentos/${movement.id}`}><span className="attention-icon amber"><AlertTriangle /></span><span><strong>{movement.description}</strong><small>{movement.id.replaceAll('-', ' ')} · {euro.format(movement.amount)}</small></span><ArrowRight size={16} /></Link>)}
          {pending.length === 0 && <div className="attention-item muted-item"><span className="attention-icon green"><Scale /></span><span><strong>Todos os movimentos conferidos</strong><small>Nada pendente</small></span></div>}
        </aside>
      </div>
    </div>
  )
}
