import { Check, CircleAlert, Gauge, Plus, Save } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { problemMessage } from '../app/WorkContext'

type Budget = { allowanceUsd: number | null; allowanceIsDefault: boolean; spentThisMonthUsd: number; extraCreditUsd: number; availableUsd: number | null; callsThisMonth: number; unlimited: boolean; exhausted: boolean }
type CompanyRow = { companyId: string; companyName: string; firmName: string; status: Budget }
type Config = { defaultMonthlyAllowanceUsd: number | null; chatModel: string | null; reportModel: string | null; quickModel: string | null; pricesJson: string | null }
type ConfigResponse = { config: Config; models: string[]; defaults: { chat: string; report: string; quick: string }; prices: Record<string, { input: number; output: number }> }

const root = '/api/v1/producer/ai-settings'
const usd = (value: number | null) => value === null ? 'ilimitado' : `${value.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} US$`
const modelLabel = (model: string) => model.includes('haiku') ? `${model} (o mais barato)` : model.includes('sonnet') ? `${model} (intermédio)` : model.includes('opus') ? `${model} (o mais capaz e caro)` : model

/**
 * O consumo de IA e os limites por empresa, só do produtor, que paga a chave. Define-se uma franquia mensal por empresa (ou uma por
 * omissão para todas) em USD, a moeda em que a Anthropic cobra; quando acaba, a IA recusa até ao mês seguinte, a não ser que se junte
 * crédito extra. Também se escolhe aqui o modelo de cada função: o pequeno é o mais barato.
 */
export function AiUsagePanel() {
  const [data, setData] = useState<ConfigResponse | null>(null)
  const [rows, setRows] = useState<CompanyRow[]>([])
  const [form, setForm] = useState<{ allowance: string; chat: string; report: string; quick: string; prices: string }>({ allowance: '', chat: '', report: '', quick: '', prices: '' })
  const [allowances, setAllowances] = useState<Record<string, string>>({})
  const [credits, setCredits] = useState<Record<string, string>>({})
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState('')

  const load = useCallback(async () => {
    const [configResponse, budgetsResponse] = await Promise.all([fetch(`${root}/usage-config`), fetch(`${root}/budgets`)])
    if (!configResponse.ok) throw new Error(await problemMessage(configResponse))
    if (!budgetsResponse.ok) throw new Error(await problemMessage(budgetsResponse))
    const config = await configResponse.json() as ConfigResponse
    if (!config.config) throw new Error('Resposta inesperada do servidor.')
    setData(config)
    setRows(await budgetsResponse.json() as CompanyRow[])
    setForm({
      allowance: config.config.defaultMonthlyAllowanceUsd?.toString() ?? '',
      chat: config.config.chatModel ?? '', report: config.config.reportModel ?? '', quick: config.config.quickModel ?? '',
      prices: config.config.pricesJson ?? '',
    })
  }, [])

  useEffect(() => { void load().catch((loadError: unknown) => setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar.')) }, [load])

  async function run(label: string, action: () => Promise<void>, done: string) {
    setBusy(label); setError(''); setMessage('')
    try {
      await action()
      await load()
      setMessage(done)
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy('')
    }
  }

  const put = async (url: string, body: unknown) => {
    const response = await fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
    if (!response.ok) throw new Error(await problemMessage(response))
  }
  const number = (text: string) => text.trim() === '' ? null : Number(text.replace(',', '.'))

  if (!data) return <section className="panel"><div className="panel-heading"><div><h2><Gauge size={17} /> IA — consumo e limites</h2><p>{error || 'A carregar…'}</p></div></div></section>

  return <section className="panel" aria-label="Consumo e limites da IA">
    <div className="panel-heading"><div>
      <h2><Gauge size={17} /> IA — consumo e limites por empresa</h2>
      <p>Os custos da chave são seus. Defina uma franquia mensal por empresa (em dólares, como a Anthropic cobra): quando acaba, a IA dessa empresa recusa até ao mês seguinte, e o crédito extra que juntar cobre o que falta. Sem franquia, o consumo só se regista.</p>
    </div></div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}

    <form className="builder-row" onSubmit={(event) => {
      event.preventDefault()
      void run('config', () => put(`${root}/usage-config`, {
        defaultMonthlyAllowanceUsd: number(form.allowance), chatModel: form.chat || null, reportModel: form.report || null, quickModel: form.quick || null, pricesJson: form.prices.trim() || null,
      }), 'Configuração guardada.')
    }}>
      <label>Franquia mensal por omissão (US$)<input inputMode="decimal" value={form.allowance} onChange={(event) => setForm({ ...form, allowance: event.target.value })} placeholder="vazio = ilimitado" /></label>
      {([['chat', 'Modelo do assistente', data.defaults.chat], ['report', 'Modelo dos relatórios', data.defaults.report], ['quick', 'Modelo das explicações, listagens e classificação', data.defaults.quick]] as const).map(([key, label, fallback]) =>
        <label key={key}>{label}<select value={form[key]} onChange={(event) => setForm({ ...form, [key]: event.target.value })}>
          <option value="">De origem — {modelLabel(fallback)}</option>{data.models.map((model) => <option key={model} value={model}>{modelLabel(model)}</option>)}</select></label>)}
      <label style={{ minWidth: 320 }}>Preços (US$ por milhão de tokens) — JSON, opcional
        <input value={form.prices} onChange={(event) => setForm({ ...form, prices: event.target.value })} placeholder={JSON.stringify(Object.fromEntries(Object.entries(data.prices).slice(0, 1).map(([model, price]) => [model, [price.input, price.output]])))} /></label>
      <button className="button button-primary" type="submit" disabled={busy !== ''}><Save size={15} /> Guardar</button>
    </form>
    <p className="mr-hint">Os preços de origem são de referência ({Object.entries(data.prices).map(([model, price]) => `${model.replace('claude-', '')}: ${price.input}/${price.output}`).join(' · ')}): confirme em anthropic.com/pricing e corrija-os aqui se mudaram.</p>

    <div className="table-wrap"><table>
      <thead><tr><th>Empresa</th><th className="numeric">Gasto no mês</th><th className="numeric">Franquia</th><th className="numeric">Crédito extra</th><th className="numeric">Resta</th><th>Alterar franquia</th><th>Juntar crédito</th></tr></thead>
      <tbody>{rows.map((row) => <tr key={row.companyId}>
        <td>{row.companyName}<small>{row.firmName} · {row.status.callsThisMonth} pedidos</small></td>
        <td className="numeric">{usd(row.status.spentThisMonthUsd)}</td>
        <td className="numeric">{usd(row.status.allowanceUsd)}{row.status.allowanceIsDefault && row.status.allowanceUsd !== null && <small>por omissão</small>}</td>
        <td className="numeric">{usd(row.status.extraCreditUsd)}</td>
        <td className="numeric">{row.status.exhausted ? <span className="badge warning">esgotado</span> : usd(row.status.availableUsd)}</td>
        <td><span className="inline-check"><input aria-label={`Franquia mensal de ${row.companyName}`} inputMode="decimal" style={{ width: 90 }} value={allowances[row.companyId] ?? ''} placeholder="US$" onChange={(event) => setAllowances({ ...allowances, [row.companyId]: event.target.value })} />
          <button type="button" className="button button-secondary" disabled={busy !== ''} onClick={() => void run(`a${row.companyId}`, () => put(`${root}/budgets/${row.companyId}`, { monthlyAllowanceUsd: number(allowances[row.companyId] ?? '') }), `Franquia de ${row.companyName} guardada (vazio = a por omissão).`)}>Guardar</button></span></td>
        <td><span className="inline-check"><input aria-label={`Crédito a juntar a ${row.companyName}`} inputMode="decimal" style={{ width: 90 }} value={credits[row.companyId] ?? ''} placeholder="US$" onChange={(event) => setCredits({ ...credits, [row.companyId]: event.target.value })} />
          <button type="button" className="button button-secondary" disabled={busy !== '' || !(number(credits[row.companyId] ?? '') ?? 0)} onClick={() => void run(`c${row.companyId}`, async () => {
            const response = await fetch(`${root}/budgets/${row.companyId}/credit`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ amountUsd: number(credits[row.companyId] ?? ''), note: null }) })
            if (!response.ok) throw new Error(await problemMessage(response))
            setCredits({ ...credits, [row.companyId]: '' })
          }, `Crédito juntado a ${row.companyName}.`)}><Plus size={14} /> Juntar</button></span></td>
      </tr>)}
      {rows.length === 0 && <tr><td colSpan={7} className="empty-state">Ainda não há empresas.</td></tr>}</tbody>
    </table></div>
  </section>
}
