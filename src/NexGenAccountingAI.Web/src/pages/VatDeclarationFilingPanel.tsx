import { CircleAlert, CircleCheck, FileDown, Send, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type AtMessage = { annex: string | null; section: string | null; field: string | null; code: string | null; text: string }
type AtResult = { reached: boolean; code: number | null; message: string; errors: AtMessage[]; alerts: AtMessage[]; declarationId: number | null; succeeded: boolean }
type Outcome = { environment: string; refusal: string | null; result: AtResult | null }
type Exchange = { service: string; operation: string; production: boolean; period: string; requestedAtUtc: string; requestedByName: string | null; reached: boolean; resultCode: number | null; message: string; declarationId: number | null }
type Exchanges = { gateway: { ready: boolean; problem: string | null }; exchanges: Exchange[] }

const OPERATIONS: Record<string, string> = { validarDeclaracao: 'Validação', submeterDeclaracao: 'Submissão' }
const ENVIRONMENTS: Record<string, string> = { Testes: 'testes', Producao: 'produção' }
const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

/**
 * A entrega de uma declaração na AT (F4A-02, F4C-06): o ficheiro XML oficial, a validação pelas
 * regras da AT e a submissão (por agora só no ambiente de testes da AT). Serve a declaração
 * periódica de IVA e o Modelo 22: cada uma diz os seus endereços e o serviço do histórico.
 */
export function AtFilingPanel({ companyId, resetKey, service, xmlPath, sendPath, defaultFileName, description, accountantLogin = false }: {
  companyId: string
  /** Muda quando o que se vai entregar muda (período, exercício…): limpa o último resultado. */
  resetKey: string
  /** O serviço no histórico de pedidos: <c>dpiva</c> ou <c>modelo22</c>. */
  service: string
  /** O caminho do ficheiro XML, a partir da empresa, com a pesquisa. */
  xmlPath: string
  sendPath: (operation: 'validate' | 'submit') => string
  defaultFileName: string
  description: string
  /** A IES é entregue pelo contabilista certificado: pede as credenciais dele em cada pedido, sem as guardar. */
  accountantLogin?: boolean
}) {
  const { user } = useWorkContext()
  const [busy, setBusy] = useState<'validate' | 'submit' | 'xml' | null>(null)
  const [outcome, setOutcome] = useState<{ operation: string; value: Outcome } | null>(null)
  const [error, setError] = useState('')
  const [history, setHistory] = useState<Exchanges | null>(null)
  const [reload, setReload] = useState(0)
  const [login, setLogin] = useState({ accountantUser: '', accountantPassword: '', taxpayerPassword: '' })

  const base = `/api/v1/development/users/${user.id}/companies/${companyId}`

  useEffect(() => { setOutcome(null); setError('') }, [companyId, resetKey])

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
      const response = await fetch(`${base}/${xmlPath}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      const name = /filename="?([^";]+)/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? defaultFileName
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
      const response = await fetch(`${base}/${sendPath(operation)}`, accountantLogin
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...login, taxpayerPassword: login.taxpayerPassword || null }) }
        : { method: 'POST' })
      if (!response.ok) throw new Error(await problemMessage(response))
      setOutcome({ operation, value: await response.json() as Outcome })
      setReload((value) => value + 1)
      // As senhas só servem para este pedido.
      if (accountantLogin) setLogin((value) => ({ ...value, accountantPassword: '', taxpayerPassword: '' }))
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

  return <section className="panel no-print" id="entrega-at">
    <div className="panel-heading"><div><h2>Entrega na AT</h2><p>{description} A submissão por aqui só funciona, por agora, no ambiente de testes da AT: em produção, valide e entregue o ficheiro no Portal das Finanças.</p></div></div>
    {accountantLogin && <div className="filterbar">
      <label className="inline-check">Utilizador do contabilista certificado <input aria-label="Utilizador do contabilista certificado" autoComplete="off" value={login.accountantUser} onChange={(event) => setLogin({ ...login, accountantUser: event.target.value })} placeholder="NIF ou NIF/subutilizador" /></label>
      <label className="inline-check">Senha do contabilista <input type="password" aria-label="Senha do contabilista" autoComplete="new-password" value={login.accountantPassword} onChange={(event) => setLogin({ ...login, accountantPassword: event.target.value })} /></label>
      <label className="inline-check">Senha da empresa (sem plenos poderes) <input type="password" aria-label="Senha da empresa" autoComplete="new-password" value={login.taxpayerPassword} onChange={(event) => setLogin({ ...login, taxpayerPassword: event.target.value })} /></label>
      <small className="atf-hint">A IES é entregue pelo contabilista certificado. As senhas só servem para este pedido: não ficam guardadas. Se o contabilista tem plenos poderes declarativos, deixe a senha da empresa em branco.</small>
    </div>}
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
    {history && history.exchanges.some((exchange) => exchange.service === service) && <>
      <p className="statement-note">Últimos pedidos à AT desta empresa:</p>
      <div className="table-wrap"><table className="dense-table">
        <thead><tr><th>Quando</th><th>Pedido</th><th>Período</th><th>Ambiente</th><th>Quem</th><th>Resposta</th></tr></thead>
        <tbody>{history.exchanges.filter((exchange) => exchange.service === service).map((exchange, index) => <tr key={index}>
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

/** A declaração periódica de IVA na entrega à AT. */
export function VatDeclarationFilingPanel({ companyId, from, to, priorCredit }: { companyId: string; from: string; to: string; priorCredit: number | null }) {
  const query = () => {
    const params = new URLSearchParams({ from, to })
    if (priorCredit !== null) params.set('priorCredit', String(priorCredit))
    return params
  }
  return <AtFilingPanel
    companyId={companyId} resetKey={`${from}|${to}|${priorCredit}`} service="dpiva" defaultFileName="dpiva.xml"
    xmlPath={`vat-declaration/xml?${query()}`}
    sendPath={(operation) => { const params = query(); if (operation === 'submit') params.set('acceptAlerts', 'true'); return `vat-declaration/at/${operation}?${params}` }}
    description="O ficheiro XML da declaração no formato oficial, a validação pelas regras da AT e a submissão. O ficheiro leva os quadros 02 a 06-A e 20 tal como estão no impresso acima, o Anexo R de cada região e, com os campos 40 e 41, os anexos das regularizações com o total em «Outras regularizações»."
  />
}

/** O Modelo 22 na entrega à AT. */
export function Modelo22FilingPanel({ companyId, year }: { companyId: string; year: number }) {
  return <AtFilingPanel
    companyId={companyId} resetKey={String(year)} service="modelo22" defaultFileName={`modelo22-${year}.xml`}
    xmlPath={`modelo-22/xml?year=${year}`}
    sendPath={(operation) => `modelo-22/at/${operation}?year=${year}${operation === 'submit' ? '&acceptAlerts=true' : ''}`}
    description="O ficheiro XML do Modelo 22 no formato oficial (rosto, quadros 07 a 11; sem anexos), a validação pelas regras da AT e a submissão."
  />
}

/** A IES (Anexo A) na entrega à AT. */
export function IesFilingPanel({ companyId, year }: { companyId: string; year: number }) {
  return <AtFilingPanel
    companyId={companyId} resetKey={String(year)} service="ies" defaultFileName={`ies-${year}.xml`} accountantLogin
    xmlPath={`ies/xml?year=${year}`}
    sendPath={(operation) => `ies/at/${operation}?year=${year}`}
    description="O ficheiro XML da IES com o Anexo A (quadros 03 e 04), a validação pelas regras da AT e a submissão."
  />
}
