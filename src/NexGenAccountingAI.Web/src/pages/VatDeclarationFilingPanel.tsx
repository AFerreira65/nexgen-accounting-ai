import { CircleAlert, CircleCheck, FileDown, Send, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type AtMessage = { annex: string | null; section: string | null; field: string | null; code: string | null; text: string }
type AtResult = { reached: boolean; code: number | null; message: string; errors: AtMessage[]; alerts: AtMessage[]; declarationId: number | null; succeeded: boolean }
type Outcome = { environment: string; refusal: string | null; result: AtResult | null }
type Exchange = { operation: string; production: boolean; period: string; requestedAtUtc: string; requestedByName: string | null; reached: boolean; resultCode: number | null; message: string; declarationId: number | null }
type Exchanges = { gateway: { ready: boolean; problem: string | null }; exchanges: Exchange[] }

const OPERATIONS: Record<string, string> = { validarDeclaracao: 'Validação', submeterDeclaracao: 'Submissão' }
const ENVIRONMENTS: Record<string, string> = { Testes: 'testes', Producao: 'produção' }
const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

/**
 * A entrega da declaração periódica de IVA (F4A-02): o ficheiro XML oficial, a validação
 * pelas regras da AT e a submissão (por agora só no ambiente de testes da AT).
 */
export function VatDeclarationFilingPanel({ companyId, from, to, priorCredit }: { companyId: string; from: string; to: string; priorCredit: number | null }) {
  const { user } = useWorkContext()
  const [busy, setBusy] = useState<'validate' | 'submit' | 'xml' | null>(null)
  const [outcome, setOutcome] = useState<{ operation: string; value: Outcome } | null>(null)
  const [error, setError] = useState('')
  const [history, setHistory] = useState<Exchanges | null>(null)
  const [reload, setReload] = useState(0)

  const base = `/api/v1/development/users/${user.id}/companies/${companyId}`
  const query = () => {
    const params = new URLSearchParams({ from, to })
    if (priorCredit !== null) params.set('priorCredit', String(priorCredit))
    return params
  }

  useEffect(() => { setOutcome(null); setError('') }, [companyId, from, to, priorCredit])

  useEffect(() => {
    const controller = new AbortController()
    void fetch(`${base}/at/exchanges`, { signal: controller.signal })
      .then(async (response) => (response.ok ? await response.json() as Exchanges : null))
      .then((value) => { if (!controller.signal.aborted) setHistory(value) })
      .catch(() => { /* painel informativo */ })
    return () => controller.abort()
  }, [base, reload])

  async function downloadXml() {
    setBusy('xml'); setError('')
    try {
      const response = await fetch(`${base}/vat-declaration/xml?${query()}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      const name = /filename="?([^";]+)/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? 'dpiva.xml'
      const url = URL.createObjectURL(await response.blob())
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = name
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o ficheiro.')
    } finally {
      setBusy(null)
    }
  }

  async function send(operation: 'validate' | 'submit') {
    setBusy(operation); setError(''); setOutcome(null)
    try {
      const params = query()
      if (operation === 'submit') params.set('acceptAlerts', 'true')
      const response = await fetch(`${base}/vat-declaration/at/${operation}?${params}`, { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      setOutcome({ operation, value: await response.json() as Outcome })
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível contactar a AT.')
    } finally {
      setBusy(null)
    }
  }

  const result = outcome?.value.result
  const messages = (title: string, items: AtMessage[]) => items.length > 0 && <div className="table-wrap"><table className="dense-table">
    <thead><tr><th>{title}</th><th>Anexo</th><th>Quadro</th><th>Campo</th><th>Código</th></tr></thead>
    <tbody>{items.map((item, index) => <tr key={index}><td>{item.text}</td><td>{item.annex ?? '—'}</td><td>{item.section ?? '—'}</td><td>{item.field ?? '—'}</td><td>{item.code ?? '—'}</td></tr>)}</tbody>
  </table></div>

  return <section className="panel no-print">
    <div className="panel-heading"><div><h2>Entrega na AT</h2><p>O ficheiro XML da declaração no formato oficial, a validação pelas regras da AT e a submissão. O ficheiro leva os quadros 02 a 06-A e 20 tal como estão no impresso acima; o Anexo R e os anexos das regularizações não são gerados. A submissão por aqui só funciona, por agora, no ambiente de testes da AT: em produção, valide e entregue o ficheiro no Portal das Finanças.</p></div></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={busy !== null} onClick={() => void downloadXml()}><FileDown size={16} /> {busy === 'xml' ? 'A gerar…' : 'Ficheiro XML'}</button>
      <button className="button button-primary" type="button" disabled={busy !== null} onClick={() => void send('validate')}><ShieldCheck size={16} /> {busy === 'validate' ? 'A validar…' : 'Validar na AT'}</button>
      <button className="button button-secondary" type="button" disabled={busy !== null} onClick={() => { if (window.confirm('Submeter a declaração no ambiente de testes da AT?')) void send('submit') }}><Send size={16} /> {busy === 'submit' ? 'A submeter…' : 'Submeter (testes)'}</button>
    </div>
    {history && !history.gateway.ready && <p className="save-status"><CircleAlert size={15} /> A ligação à AT ainda não está configurada: {history.gateway.problem} Veja <Link to="/comunicacao-at">Comunicação com a AT</Link>. O ficheiro XML pode ser gerado na mesma.</p>}
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {outcome?.value.refusal && <p className="save-status error"><CircleAlert size={15} /> {outcome.value.refusal}</p>}
    {result && <p className={`save-status ${result.succeeded ? 'success' : 'error'}`}>
      {result.succeeded ? <CircleCheck size={15} /> : <CircleAlert size={15} />} AT ({ENVIRONMENTS[outcome!.value.environment] ?? outcome!.value.environment}): {result.message}
      {result.code !== null && result.code !== 0 ? ` (código ${result.code})` : ''}
      {result.declarationId !== null ? ` Declaração n.º ${result.declarationId}.` : ''}
    </p>}
    {result && messages('Erros', result.errors)}
    {result && messages('Alertas', result.alerts)}
    {history && history.exchanges.length > 0 && <>
      <p className="statement-note">Últimos pedidos à AT desta empresa:</p>
      <div className="table-wrap"><table className="dense-table">
        <thead><tr><th>Quando</th><th>Pedido</th><th>Período</th><th>Ambiente</th><th>Quem</th><th>Resposta</th></tr></thead>
        <tbody>{history.exchanges.map((exchange, index) => <tr key={index}>
          <td>{dateTime.format(new Date(exchange.requestedAtUtc))}</td>
          <td>{OPERATIONS[exchange.operation] ?? exchange.operation}</td>
          <td>{exchange.period}</td>
          <td>{exchange.production ? 'Produção' : 'Testes'}</td>
          <td>{exchange.requestedByName ?? '—'}</td>
          <td><span className={`badge ${exchange.reached && exchange.resultCode === 0 ? 'success' : 'warning'}`}>{exchange.reached ? (exchange.resultCode === 0 ? 'Aceite' : `Código ${exchange.resultCode ?? '?'}`) : 'Sem resposta'}</span> {exchange.message}{exchange.declarationId !== null ? ` · n.º ${exchange.declarationId}` : ''}</td>
        </tr>)}</tbody>
      </table></div>
    </>}
  </section>
}
