import { Database, DoorOpen, Lock } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, type Movement } from '../app/demo-data'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type OpeningLine = { accountCode: string; accountName: string; debit: number; credit: number }
type Opening = {
  year: number; previousYear: number; accountCount: number; total: number
  previousYearClosed: boolean; alreadyOpened: boolean; openingReference: string | null; lines: OpeningLine[]
}

function demoPreview(movements: Movement[], nameOf: (code: string) => string, year: number): Opening {
  const previousYear = year - 1
  const carry = new Map<string, { name: string; balance: number }>()
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (Number(movement.date.slice(0, 4)) > previousYear) continue
    for (const line of movement.lines) {
      // Classes 1-5 transportam-se; o resultado (classes 6, 7 e 81) vai para 56.
      const toRetained = /^[678]/.test(line.account)
      if (!/^[1-5]/.test(line.account) && !toRetained) continue
      const code = toRetained ? '56' : line.account
      const name = toRetained ? 'Resultados transitados' : (line.accountName ?? nameOf(line.account))
      const current = carry.get(code) ?? { name, balance: 0 }
      current.balance += line.debit - line.credit
      carry.set(code, current)
    }
  }
  const lines: OpeningLine[] = [...carry.entries()]
    .filter(([, value]) => value.balance !== 0)
    .sort(([a], [b]) => a.localeCompare(b, 'pt-PT'))
    .map(([code, value]) => ({
      accountCode: code, accountName: value.name,
      debit: value.balance > 0 ? value.balance : 0, credit: value.balance < 0 ? -value.balance : 0,
    }))
  return {
    year, previousYear, accountCount: lines.length,
    total: lines.reduce((sum, line) => sum + line.debit, 0),
    previousYearClosed: false, alreadyOpened: false, openingReference: null, lines,
  }
}

export function YearOpeningPage() {
  const { activeCompany, accountingSetup, movements, source, user, refreshAccounting } = useWorkContext()
  const [year, setYear] = useState((activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear()) + 1)
  const nameOf = useMemo(() => (code: string) => accountingSetup.accounts.find((account) => account.code === code)?.name ?? code, [accountingSetup.accounts])
  const [data, setData] = useState<Opening>(() => demoPreview(movements, nameOf, year))
  const [status, setStatus] = useState<'idle' | 'loading' | 'saving' | 'error' | 'saved'>('idle')
  const [message, setMessage] = useState('')

  useEffect(() => { setYear((activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear()) + 1) }, [activeCompany.companyId, activeCompany.settings.defaultFiscalYear])

  const load = useCallback((signal?: AbortSignal) => {
    if (source !== 'database') {
      setData(demoPreview(movements, nameOf, year))
      setStatus('idle')
      return
    }
    setStatus('loading')
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-opening?year=${year}`, { signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(await problemMessage(response))
        setData(await response.json() as Opening)
        setStatus('idle')
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setData(demoPreview(movements, nameOf, year))
        setMessage(error instanceof Error ? error.message : 'Não foi possível consultar a abertura.')
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
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-opening?year=${year}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      const movement = await response.json() as { id: string }
      setStatus('saved')
      setMessage(`Abertura registada como ${movement.id}.`)
      refreshAccounting()
      load()
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof Error ? error.message : 'Não foi possível registar a abertura.')
    }
  }

  async function reopen() {
    setStatus('saving')
    setMessage('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/year-opening/reopen?year=${year}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      const movement = await response.json() as { id: string }
      setStatus('saved')
      setMessage(`Abertura ${movement.id} anulada. O exercício pode ser reaberto.`)
      refreshAccounting()
      load()
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof Error ? error.message : 'Não foi possível reabrir o exercício.')
    }
  }

  const totalDebit = data.lines.reduce((sum, line) => sum + line.debit, 0)
  const totalCredit = data.lines.reduce((sum, line) => sum + line.credit, 0)
  const canRegister = source === 'database' && !data.alreadyOpened && data.previousYearClosed && data.lines.length >= 2 && status !== 'saving'
  const canReopen = source === 'database' && data.alreadyOpened && status !== 'saving'

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Abertura do exercício</h1><p>Transporta os saldos de balanço do exercício anterior e leva o resultado à conta 56, no diário de abertura. Só uma vez por exercício.</p></div><span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>

    <section className="metric-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><DoorOpen /></span><div><small>Contas a transportar</small><strong>{data.accountCount}</strong><span>Saldos de {data.previousYear}</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><DoorOpen /></span><div><small>Total do lançamento</small><strong>{euro.format(data.total)}</strong><span>Débito = crédito</span></div></article>
      <article className="metric-card"><span className={`metric-icon ${data.previousYearClosed ? 'blue' : 'amber'}`}><Lock /></span><div><small>Exercício de {data.previousYear}</small><strong>{data.previousYearClosed ? 'Apurado' : 'Por apurar'}</strong><span>{data.previousYearClosed ? 'Pronto para abrir' : 'Apurar antes de abrir'}</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><Lock /></span><div><small>Estado</small><strong>{data.alreadyOpened ? 'Aberto' : 'Por abrir'}</strong><span>{data.alreadyOpened ? data.openingReference : `Exercício ${year}`}</span></div></article>
    </section>

    <div className="filterbar series-filterbar">
      <label className="year-filter"><span>Exercício</span><input aria-label="Exercício da abertura" type="number" min="2001" max="2200" value={year} onChange={(event) => setYear(Number(event.target.value))} /></label>
      {data.alreadyOpened
        ? <button type="button" className="button button-secondary" onClick={() => void reopen()} disabled={!canReopen}><DoorOpen size={15} /> Reabrir exercício</button>
        : <button type="button" className="button button-primary" onClick={() => void register()} disabled={!canRegister}><DoorOpen size={15} /> Registar abertura</button>}
    </div>

    {message && <div className={`series-message ${status === 'error' ? 'error' : 'success'}`}>{message}</div>}
    {source !== 'database' && <div className="series-message">Pré-visualização de demonstração. O registo da abertura requer ligação à base de dados.</div>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Lançamento de abertura</h2><p>Uma linha por conta de balanço com saldo, e o resultado do exercício anterior em 56.</p></div></div>
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
      {status === 'loading' && <div className="series-table-status">A consultar a abertura…</div>}
      {status !== 'loading' && data.lines.length === 0 && <div className="series-table-status">Sem saldos de balanço para transportar para {year}.</div>}
    </section>
  </div>
}
