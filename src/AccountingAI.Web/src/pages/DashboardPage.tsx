import { AlertTriangle, ArrowRight, Clock3, Database, HandCoins, Landmark, Receipt, Scale, TrendingUp, Users, Wallet } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { useMovementsSummary } from '../app/movementsSummary'
import { PeriodShortcuts } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'

type Balance = { accountCode: string; balance: number }

// Uma empresa com um histórico muito grande não deve desenhar todos os
// movimentos aqui — é só um resumo; a lista completa e paginada fica em
// Movimentos.
const RECENT_MOVEMENTS = 8

// A data de referência dos indicadores fica guardada por empresa no
// navegador, para não voltar ao presente a cada visita ao painel.
const REFERENCE_DATE_KEY = 'accounting-ai.dashboard-reference-date.'
function readReferenceDate(companyId: string): string {
  try {
    return window.localStorage.getItem(`${REFERENCE_DATE_KEY}${companyId}`) ?? ''
  } catch {
    return ''
  }
}

// O saldo de cada conta é sempre cumulativo até `to` — `from` só existe para
// os relatórios que separam abertura de movimento do período, o que aqui não
// interessa: só queremos a posição financeira numa data de referência.
function balancesFromMovements(movements: ReturnType<typeof useWorkContext>['movements'], to: string): Balance[] {
  const map = new Map<string, number>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (to && movement.date > to) continue
    for (const line of movement.lines) {
      map.set(line.account, (map.get(line.account) ?? 0) + line.debit - line.credit)
    }
  }
  return [...map.entries()].map(([accountCode, balance]) => ({ accountCode, balance }))
}

export function DashboardPage() {
  const { accountingSource, activeCompany, movements, source, user } = useWorkContext()
  const onDatabase = accountingSource === 'database'
  // Contagens e movimentos por conferir vêm do resumo agregado; a lista
  // recente, numa empresa ligada à BD, de um pedido só da primeira página.
  const summary = useMovementsSummary()
  const activeCount = summary.total - summary.voided
  const pendingCount = summary.pendingReview
  const firstName = user.displayName.replace(' (Demonstração)', '').split(' ')[0]
  const [toDate, setToDate] = useState(() => readReferenceDate(activeCompany.companyId))
  const [balances, setBalances] = useState<Balance[]>(() => balancesFromMovements(movements, ''))
  useEffect(() => setToDate(readReferenceDate(activeCompany.companyId)), [activeCompany.companyId])
  const pickReferenceDate = (value: string) => {
    setToDate(value)
    try {
      if (value) window.localStorage.setItem(`${REFERENCE_DATE_KEY}${activeCompany.companyId}`, value)
      else window.localStorage.removeItem(`${REFERENCE_DATE_KEY}${activeCompany.companyId}`)
    } catch { /* storage é opcional */ }
  }

  const [recent, setRecent] = useState<Movement[] | null>(null)
  useEffect(() => {
    if (!onDatabase) { setRecent(null); return }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements?page=1&pageSize=${RECENT_MOVEMENTS}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as { items: Movement[] }
      })
      .then((body) => setRecent(body.items))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setRecent(null)
      })
    return () => controller.abort()
  }, [onDatabase, activeCompany.companyId, user.id])
  const recentRows = recent ?? movements.slice(0, RECENT_MOVEMENTS)

  useEffect(() => {
    if (source !== 'database') {
      setBalances(balancesFromMovements(movements, toDate))
      return
    }
    const params = new URLSearchParams()
    if (toDate) params.set('to', toDate)
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/trial-balance?${params}`, { signal: controller.signal })
      .then(async (response) => response.ok ? await response.json() as { rows: Balance[] } : Promise.reject(new Error()))
      .then((result) => setBalances(result.rows.map((row) => ({ accountCode: row.accountCode, balance: row.balance }))))
      .catch(() => { if (!controller.signal.aborted) setBalances(balancesFromMovements(movements, toDate)) })
    return () => controller.abort()
  }, [activeCompany.companyId, movements, source, toDate, user.id])

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

  const asOf = toDate ? `Até ${formatMovementDate(toDate)}` : null

  return (
    <div className="page-stack">
      <header className="page-heading dashboard-heading">
        <div><p className="eyebrow">Núcleo contabilístico local</p><h1>Bom dia, {firstName}</h1><p>Dados de teste persistidos, com acesso direto ao movimento, diário e conta.</p></div>
        <Link className="button button-primary" to="/movimentos"><Database size={17} /> Consultar dados</Link>
      </header>

      <div className="filterbar">
        <label><span className="sr-only">Data de referência</span><input type="date" value={toDate} onChange={(event) => pickReferenceDate(event.target.value)} /></label>
        <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(_from, to) => pickReferenceDate(to)} />
        {toDate && <button type="button" className="button button-secondary" onClick={() => pickReferenceDate('')}>Hoje</button>}
      </div>

      <section className="metric-grid" aria-label="Indicadores do período">
        <article className="metric-card"><span className="metric-icon teal"><Scale /></span><div><small>Movimentos</small><strong>{activeCount}</strong><span>{accountingSource === 'database' ? 'Lidos do SQL Server' : 'Fallback visual local'}</span></div></article>
        <article className="metric-card"><span className={`metric-icon ${netResult >= 0 ? 'blue' : 'rose'}`}><TrendingUp /></span><div><small>Resultado</small><strong>{euro.format(netResult)}</strong><span>{asOf ?? (netResult >= 0 ? 'Lucro acumulado' : 'Prejuízo acumulado')}</span></div></article>
        <article className="metric-card"><span className="metric-icon blue"><Wallet /></span><div><small>Total do ativo</small><strong>{euro.format(totalAssets)}</strong><span>{asOf ?? 'Classes 1, 3, 4 e 2 devedora'}</span></div></article>
        <article className="metric-card"><span className="metric-icon amber"><Clock3 /></span><div><small>Por conferir</small><strong>{pendingCount}</strong><span>Movimentos pendentes de conferência</span></div></article>
      </section>

      <section className="metric-grid" aria-label="Posição financeira">
        <Link className="metric-card" to="/balancete"><span className="metric-icon navy"><Wallet /></span><div><small>Caixa e bancos</small><strong>{euro.format(cashAndBank)}</strong><span>{asOf ?? 'Contas 11 e 12'}</span></div></Link>
        <Link className="metric-card" to="/contas-correntes"><span className="metric-icon teal"><Users /></span><div><small>Saldo de clientes</small><strong>{euro.format(receivables)}</strong><span>{asOf ?? 'Classe 21 · a receber'}</span></div></Link>
        <Link className="metric-card" to="/contas-correntes"><span className="metric-icon amber"><HandCoins /></span><div><small>Saldo de fornecedores</small><strong>{euro.format(payables)}</strong><span>{asOf ?? 'Classe 22 · a pagar'}</span></div></Link>
        <Link className="metric-card" to="/iva"><span className="metric-icon navy"><Receipt /></span><div><small>Posição de IVA</small><strong>{euro.format(vatPosition)}</strong><span>{asOf ?? (vatPosition > 0 ? 'A entregar ao Estado' : vatPosition < 0 ? 'A recuperar ou a reportar' : 'Nulo')}</span></div></Link>
      </section>

      <div className="dashboard-grid">
        <section className="panel">
          <div className="panel-heading"><div><h2>Movimentos recentes</h2><p>Últimas operações da empresa selecionada</p></div><Link className="text-link" to="/movimentos">Ver todos <ArrowRight size={15} /></Link></div>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th></tr></thead>
              <tbody>{recentRows.map((movement) => <tr key={movement.id}>
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
          <div className="panel-heading"><div><h2>A sua atenção</h2><p>Movimentos por conferir</p></div><span className="attention-count">{pendingCount}</span></div>
          {summary.pendingPreview.map((movement) => <Link className="attention-item" key={movement.id} to={`/movimentos/${movement.id}`}><span className="attention-icon amber"><AlertTriangle /></span><span><strong>{movement.description}</strong><small>{movement.id.replaceAll('-', ' ')} · {euro.format(movement.amount)}</small></span><ArrowRight size={16} /></Link>)}
          {pendingCount > summary.pendingPreview.length && <Link className="text-link" to="/movimentos">Ver todos os {pendingCount} <ArrowRight size={15} /></Link>}
          {pendingCount === 0 && <div className="attention-item muted-item"><span className="attention-icon green"><Scale /></span><span><strong>Todos os movimentos conferidos</strong><small>Nada pendente</small></span></div>}
        </aside>
      </div>
    </div>
  )
}
