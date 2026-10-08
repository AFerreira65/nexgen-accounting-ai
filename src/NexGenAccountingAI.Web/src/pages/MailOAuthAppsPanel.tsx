import { Check, CircleAlert, Mail, Save, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { problemMessage } from '../app/WorkContext'

type App = { provider: 'Microsoft' | 'Google'; configured: boolean; clientId: string | null; tenant: string | null; hasSecret: boolean; updatedAtUtc: string | null }
type Response = { redirectUri: string; apps: App[] }

const labels = { Microsoft: 'Microsoft 365 / Outlook (Azure, Entra ID)', Google: 'Gmail / Google Workspace (Google Cloud)' } as const
const root = '/api/v1/producer/mail-oauth'

/**
 * As aplicações OAuth2 do email, só do produtor (ADR-0019). Para um utilizador ligar a conta Microsoft 365 ou
 * Google com o MFA do fornecedor, a aplicação tem de estar registada no Azure (Entra ID) ou na Google Cloud;
 * aqui guardam-se o ID e o segredo dessa aplicação, que ficam cifrados e nunca voltam ao ecrã.
 */
export function MailOAuthAppsPanel() {
  const [data, setData] = useState<Response | null>(null)
  const [drafts, setDrafts] = useState<Record<string, { clientId: string; clientSecret: string; tenant: string }>>({})
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    const response = await fetch(root)
    if (!response.ok) throw new Error(await problemMessage(response))
    const value = await response.json() as Response
    if (!Array.isArray(value.apps)) throw new Error('Resposta inesperada do servidor.')
    setData(value)
    setDrafts(Object.fromEntries(value.apps.map((app) => [app.provider, { clientId: app.clientId ?? '', clientSecret: '', tenant: app.tenant ?? '' }])))
  }, [])

  useEffect(() => { void load().catch((loadError: unknown) => setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar.')) }, [load])

  async function run(provider: string, action: () => Promise<Response | undefined>, done: string) {
    setBusy(provider); setError(''); setMessage('')
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

  if (!data) return <section className="panel"><div className="panel-heading"><div><h2><Mail size={17} /> Email — aplicações OAuth2</h2><p>{error || 'A carregar…'}</p></div></div></section>

  return <section className="panel">
    <div className="panel-heading"><div>
      <h2><Mail size={17} /> Email — aplicações OAuth2</h2>
      <p>Para os utilizadores ligarem o email Microsoft 365 ou Gmail com MFA, registe uma aplicação no Azure (Entra ID) ou na Google Cloud e indique aqui o ID e o segredo dela. No registo, o endereço de resposta (redirect URI) é: <code>{data.redirectUri}</code>.</p>
    </div></div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}
    {data.apps.map((app) => {
      const draft = drafts[app.provider] ?? { clientId: '', clientSecret: '', tenant: '' }
      const change = (patch: Partial<typeof draft>) => setDrafts({ ...drafts, [app.provider]: { ...draft, ...patch } })
      return <form key={app.provider} className="builder-row" aria-label={labels[app.provider]} onSubmit={(event) => {
        event.preventDefault()
        void run(app.provider, async () => {
          const response = await fetch(root, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ provider: app.provider, ...draft }) })
          if (!response.ok) throw new Error(await problemMessage(response))
          return undefined
        }, `${app.provider === 'Microsoft' ? 'Microsoft' : 'Google'}: aplicação guardada.`)
      }}>
        <strong>{labels[app.provider]} <span className={`badge ${app.configured ? 'success' : ''}`}>{app.configured ? 'Registada' : 'Por registar'}</span></strong>
        <label>ID da aplicação (cliente)<input value={draft.clientId} onChange={(event) => change({ clientId: event.target.value })} autoComplete="off" /></label>
        <label>Segredo{app.hasSecret ? ' (em branco mantém)' : ''}<input type="password" value={draft.clientSecret} onChange={(event) => change({ clientSecret: event.target.value })} autoComplete="new-password" /></label>
        {app.provider === 'Microsoft' && <label>Diretório (tenant) — vazio = qualquer<input value={draft.tenant} onChange={(event) => change({ tenant: event.target.value })} autoComplete="off" /></label>}
        <button type="submit" className="button button-primary" disabled={busy !== '' || !draft.clientId}><Save size={15} /> Guardar</button>
        {app.configured && <button type="button" className="button" disabled={busy !== ''} onClick={() => void run(app.provider, async () => {
          const response = await fetch(`${root}/${app.provider}`, { method: 'DELETE' })
          if (!response.ok && response.status !== 404) throw new Error(await problemMessage(response))
          return undefined
        }, 'Aplicação retirada.')}><Trash2 size={15} /> Retirar</button>}
      </form>
    })}
  </section>
}
