import { CalendarCheck, Database, Lock } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, type Movement } from '../app/demo-data'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type ClosingLine = { accountCode: string; accountName: string; debit: number; credit: number }
type Closing = {
  year: number; income: number; expenses: number; netResult: number
  alreadyClosed: boolean; closingReference: string | null; lines: ClosingLine[]
}

function demoPreview(movements: Movement[], nameOf: (code: string) => string, year: number): Closing {
  const map = new Map<string, ClosingLine & { debit: number; credit: number }>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (movement.date.slice(0, 4) !== String(year)) continue
    for (const line of movement.lines) {
      if (!/^[67]/.test(line.account)) continue
      const row = map.get(line.account) ?? { accountCode: line.account, accountName: line.accountName ?? nameOf(line.account), debit: 0, credit: 0 }
      row.debit += line.debit
      row.credit += line.credit
      map.set(line.account, row)
    }
  }
  let income = 0
  let expenses = 0
  const lines: ClosingLine[] = []
  for (const row of [...map.values()].sort((a, b) => a.accountCode.localeCompare(b.accountCode, 'pt-PT'))) {
    const net = row.debit - row.credit
    if (net === 0) continue
    lines.push({ accountCode: row.accountCode, accountName: row.accountName, debit: net < 0 ? -net : 0, credit: net > 0 ? net : 0 })
    if (row.accountCode.startsWith('7')) income += -net
    else expenses += net
  }
  const netResult = income - expenses
  if (lines.length > 0 && netResult !== 0) {
    lines.push({
      accountCode: '81', accountName: 'Resultado líquido do período',
      debit: netResult > 0 ? 0 : -netResult, credit: netResult > 0 ? netResult : 0,
    })
  }
  return { year, income, expenses, netResult, alreadyClosed: false, closingReference: null, lines }
}

export function YearEndClosingPage() {
  const { activeCompany, accountingSetup, movements, source, user, refreshAccounting } = useWorkContext()
  const [year, setYear] = useState(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear())
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [data, setData] = useState<Closing>(() => demoPreview(movements, nameOf, year))
  const [status, setStatus] = useState<'idle' | 'loading' | 'saving' | 'error' | 'saved'>('idle')
  const [message, setMessage] = useState('')

  useEffect(() => { setYear(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear()) }, [activeCompany.companyId, activeCompany.settings.defaultFiscalYear])

  const load = useCallback((signal?: AbortSignal) => {
    if (source !== 'database') {
      setData(demoPreview(movements, nameOf, year))
      setStatus('idle')
      return
    }
    setStatus('loading')
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-end-closing?year=${year}`, { signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(await problemMessage(response))
        setData(await response.json() as Closing)
        setStatus('idle')
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setData(demoPreview(movements, nameOf, year))
        setMessage(error instanceof Error ? error.message : 'Não foi possível consultar o apuramento.')
        setStatus('error')
      })
  }, [activeCompany.companyId, movements, nameOf, source, user.id, year])

  useEffect(() => {
    const controller = new AbortController()
    setMessage('')
    load(controller.signal)
    return () => controller.abort()
  }, [load])

  async function register() {
    setStatus('saving')
    setMessage('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-end-closing?year=${year}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      const movement = await response.json() as { id: string }
      setStatus('saved')
      setMessage(`Apuramento registado como ${movement.id}.`)
      refreshAccounting()
      load()
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof Error ? error.message : 'Não foi possível registar o apuramento.')
    }
  }

  async function reopen() {
    setStatus('saving')
    setMessage('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-end-closing/reopen?year=${year}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      const movement = await response.json() as { id: string }
      setStatus('saved')
      setMessage(`Apuramento ${movement.id} anulado. O exercício pode ser reapurado.`)
      refreshAccounting()
      load()
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof Error ? error.message : 'Não foi possível reabrir o exercício.')
    }
  }

  const totalDebit = data.lines.reduce((sum, line) => sum + line.debit, 0)
  const totalCredit = data.lines.reduce((sum, line) => sum + line.credit, 0)
  const canRegister = source === 'database' && !data.alreadyClosed && data.lines.length >= 2 && status !== 'saving'
  const canReopen = source === 'database' && data.alreadyClosed && status !== 'saving'

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Apuramento de resultados</h1><p>Fecha as contas das classes 6 e 7 do exercício contra a conta 81, no diário de apuramento. Só uma vez por exercício.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <section className="metric-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><CalendarCheck /></span><div><small>Rendimentos</small><strong>{euro.format(data.income)}</strong><span>Classe 7 · {year}</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><CalendarCheck /></span><div><small>Gastos</small><strong>{euro.format(data.expenses)}</strong><span>Classe 6 · {year}</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${data.netResult >= 0 ? 'blue' : 'rose'}`}><CalendarCheck /></span><div><small>Resultado líquido</small><strong>{euro.format(data.netResult)}</strong><span>{data.netResult >= 0 ? 'Lucro' : 'Prejuízo'}</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><Lock /></span><div><small>Estado</small><strong>{data.alreadyClosed ? 'Apurado' : 'Por apurar'}</strong><span>{data.alreadyClosed ? data.closingReference : `Exercício ${year}`}</span></div></article>
    </section>

    <div className="filterbar series-filterbar">
      <label className="year-filter"><span>Exercício</span><input aria-label="Exercício do apuramento" type="number" min="2000" max="2200" value={year} onChange={(event) => setYear(Number(event.target.value))} /></label>
      {data.alreadyClosed
        ? <button type="button" className="button button-secondary" onClick={() => void reopen()} disabled={!canReopen}><Lock size={15} /> Reabrir exercício</button>
        : <button type="button" className="button button-primary" onClick={() => void register()} disabled={!canRegister}><Lock size={15} /> Registar apuramento</button>}
    </div>

    {message && <div className={`series-message ${status === 'error' ? 'error' : 'success'}`}>{message}</div>}
    {source !== 'database' && <div className="series-message">Pré-visualização de demonstração. O registo do apuramento requer ligação ao SQL Server.</div>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Lançamento de apuramento</h2><p>Uma linha por conta de resultado, mais a contrapartida em 81.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Descrição</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead>
        <tbody>{data.lines.map((line) => <tr key={line.accountCode}>
          <td><Link className="primary-cell" to={`/contas/${line.accountCode}`}>{line.accountCode}</Link></td>
          <td>{line.accountName}</td>
          <td className="numeric">{line.debit ? euro.format(line.debit) : '—'}</td>
          <td className="numeric">{line.credit ? euro.format(line.credit) : '—'}</td>
        </tr>)}</tbody>
        <tfoot><tr><td colSpan={2}>Totais</td><td className="numeric strong">{euro.format(totalDebit)}</td><td className="numeric strong">{euro.format(totalCredit)}</td></tr></tfoot>
      </table></div>
      {status === 'loading' && <div className="series-table-status">A consultar o apuramento…</div>}
      {status !== 'loading' && data.lines.length === 0 && <div className="series-table-status">Sem rendimentos nem gastos para apurar em {year}.</div>}
    </section>
  </div>
}
