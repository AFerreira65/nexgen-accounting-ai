import { ChevronDown, ChevronRight, Database, Sparkles, TrendingDown, TrendingUp } from 'lucide-react'
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { signalLoadFailure } from '../app/loadFailure'
import { useWorkContext } from '../app/WorkContext'

type Party = { name: string; current: number; previous: number; delta: number }
type Row = { accountCode: string; accountName: string; kind: 'income' | 'expense'; current: number; previous: number; delta: number; percent: number | null; favorable: boolean; topParties: Party[] }
type Report = {
  from: string; to: string; compareFrom: string; compareTo: string
  totals: { income: number; previousIncome: number; expenses: number; previousExpenses: number; result: number; previousResult: number }
  rows: Row[]
}
type Outcome = { refusal: string | null; explanation: { text: string; model: string } | null }
type Preset = 'month' | 'ytd' | 'custom'

const amountFormat = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })
const iso = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
const endOfMonth = (year: number, month: number) => new Date(year, month + 1, 0)

/** Os quatro limites dos dois períodos de uma predefinição, a partir de hoje. */
export function presetPeriods(preset: Exclude<Preset, 'custom'>, today: Date): [string, string, string, string] {
  if (preset === 'month') {
    const last = endOfMonth(today.getFullYear(), today.getMonth() - 1)
    const before = endOfMonth(last.getFullYear(), last.getMonth() - 1)
    return [iso(new Date(last.getFullYear(), last.getMonth(), 1)), iso(last), iso(new Date(before.getFullYear(), before.getMonth(), 1)), iso(before)]
  }

  // Do início do ano ao fim do último mês fechado, contra o mesmo período do ano anterior.
  const last = endOfMonth(today.getFullYear(), today.getMonth() - 1)
  const year = last.getFullYear()
  const previous = endOfMonth(year - 1, last.getMonth())
  return [`${year}-01-01`, iso(last), `${year - 1}-01-01`, iso(previous)]
}

/**
 * As variações entre dois períodos (F4F-03): o que mudou, conta a conta, e quem mais pesou. A explicação
 * em texto, se for pedida, parte dos mesmos números.
 */
export function VariancePage() {
  const { activeCompany, source, user } = useWorkContext()
  const [preset, setPreset] = useState<Preset>('month')
  const [custom, setCustom] = useState<[string, string, string, string]>(() => presetPeriods('month', new Date()))
  const [report, setReport] = useState<Report | null>(null)
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [explanation, setExplanation] = useState<Outcome | null>(null)
  const [explaining, setExplaining] = useState(false)
  const [error, setError] = useState('')
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/variance`

  const periods = useMemo(() => preset === 'custom' ? custom : presetPeriods(preset, new Date()), [preset, custom])
  const query = useCallback(() => new URLSearchParams({ from: periods[0], to: periods[1], compareFrom: periods[2], compareTo: periods[3] }), [periods])

  const load = useCallback(() => {
    if (source !== 'database') return () => undefined
    const controller = new AbortController()
    setError(''); setExplanation(null); setOpen(new Set())
    void fetch(`${root}?${query()}`, { signal: controller.signal })
      .then(async (response) => {
        if (response.ok) return await response.json() as Report
        const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
        throw new Error(Object.values(problem?.errors ?? {}).flat()[0] ?? 'Não foi possível calcular as variações.')
      })
      .then((value) => { if (!controller.signal.aborted) setReport(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { signalLoadFailure(); setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível calcular as variações.') } })
    return () => controller.abort()
  }, [root, source, query])
  useEffect(() => load(), [load])

  async function explain() {
    setExplaining(true); setExplanation(null)
    try {
      const response = await fetch(`${root}/explain?${query()}`, { method: 'POST' })
      if (!response.ok) { setError('Não foi possível pedir a explicação.'); return }
      setExplanation(await response.json() as Outcome)
    } finally {
      setExplaining(false)
    }
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Variações</h1><p>As variações precisam da base de dados; em demonstração não estão disponíveis.</p></div></header></div>
  }

  const toggle = (code: string) => setOpen((current) => { const next = new Set(current); if (!next.delete(code)) next.add(code); return next })
  const totals = report?.totals
  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gestão</p><h1>Variações</h1><p>O que mudou nos gastos e nos rendimentos entre dois períodos, conta a conta, e quem mais pesou em cada uma.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert">{error}</p>}

    <section className="panel">
      <div className="filterbar">
        <label className="inline-check">Comparar
          <select aria-label="Comparação" value={preset} onChange={(event) => setPreset(event.target.value as Preset)}>
            <option value="month">O último mês fechado com o mês antes</option>
            <option value="ytd">O ano até ao último mês fechado com o mesmo período do ano anterior</option>
            <option value="custom">Períodos à escolha</option>
          </select>
        </label>
        {preset === 'custom' && <>
          <label className="inline-check">Período <input type="date" aria-label="Período, de" value={custom[0]} onChange={(event) => setCustom([event.target.value, custom[1], custom[2], custom[3]])} /> a <input type="date" aria-label="Período, até" value={custom[1]} onChange={(event) => setCustom([custom[0], event.target.value, custom[2], custom[3]])} /></label>
          <label className="inline-check">com <input type="date" aria-label="Anterior, de" value={custom[2]} onChange={(event) => setCustom([custom[0], custom[1], event.target.value, custom[3]])} /> a <input type="date" aria-label="Anterior, até" value={custom[3]} onChange={(event) => setCustom([custom[0], custom[1], custom[2], event.target.value])} /></label>
        </>}
        <button type="button" className="button button-secondary" disabled={!report || report.rows.length === 0 || explaining} onClick={() => void explain()}><Sparkles size={15} /> {explaining ? 'A escrever…' : 'Explicar com IA'}</button>
      </div>
      {report && <p className="statement-note">Período: {periods[0]} a {periods[1]}. Comparado com: {periods[2]} a {periods[3]}.</p>}
    </section>

    {explanation && <section className="panel" aria-label="Explicação">
      {explanation.refusal && <p className="save-status">{explanation.refusal}</p>}
      {explanation.explanation && <>
        <p>{explanation.explanation.text}</p>
        <p className="statement-note">Texto escrito pela IA ({explanation.explanation.model}) a partir dos números desta página; as causas são hipóteses a confirmar.</p>
      </>}
    </section>}

    {totals && <section className="metadata-strip" aria-label="Totais">
      <span><small>Rendimentos</small><strong>{amountFormat.format(totals.income)}</strong><small>antes {amountFormat.format(totals.previousIncome)}</small></span>
      <span><small>Gastos</small><strong>{amountFormat.format(totals.expenses)}</strong><small>antes {amountFormat.format(totals.previousExpenses)}</small></span>
      <span><small>Resultado</small><strong>{amountFormat.format(totals.result)}</strong><small>antes {amountFormat.format(totals.previousResult)}</small></span>
    </section>}

    {report && report.rows.length === 0 && <p className="empty-state">Não há variações entre os dois períodos.</p>}
    {report && report.rows.length > 0 && <section className="panel"><div className="table-wrap"><table aria-label="Variações por conta">
      <thead><tr><th>Conta</th><th className="numeric">Período</th><th className="numeric">Anterior</th><th className="numeric">Diferença</th><th className="numeric">%</th></tr></thead>
      <tbody>{report.rows.map((row) => <Fragment key={row.accountCode}>
        <tr>
          <td>
            {row.topParties.length > 0
              ? <button type="button" className="link-button" aria-expanded={open.has(row.accountCode)} onClick={() => toggle(row.accountCode)}>{open.has(row.accountCode) ? <ChevronDown size={14} /> : <ChevronRight size={14} />} {row.accountCode} · {row.accountName}</button>
              : <span>{row.accountCode} · {row.accountName}</span>}
            <small style={{ marginLeft: 6 }}>{row.kind === 'income' ? 'rendimento' : 'gasto'}</small>
          </td>
          <td className="numeric">{amountFormat.format(row.current)}</td>
          <td className="numeric">{amountFormat.format(row.previous)}</td>
          <td className={`numeric ${row.favorable ? '' : 'negative'}`}>{row.favorable ? <TrendingUp size={13} aria-label="Favorável" /> : <TrendingDown size={13} aria-label="Desfavorável" />} {amountFormat.format(row.delta)}</td>
          <td className="numeric">{row.percent === null ? '—' : `${amountFormat.format(row.percent)} %`}</td>
        </tr>
        {open.has(row.accountCode) && row.topParties.map((party) => <tr key={`${row.accountCode}-${party.name}`} className="muted-row">
          <td style={{ paddingLeft: 36 }}><small>{party.name}</small></td>
          <td className="numeric"><small>{amountFormat.format(party.current)}</small></td>
          <td className="numeric"><small>{amountFormat.format(party.previous)}</small></td>
          <td className="numeric"><small>{amountFormat.format(party.delta)}</small></td>
          <td />
        </tr>)}
      </Fragment>)}</tbody>
    </table></div></section>}
  </div>
}
