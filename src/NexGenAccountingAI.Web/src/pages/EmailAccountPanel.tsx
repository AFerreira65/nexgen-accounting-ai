import { Check, CircleAlert, Mail, RefreshCw, Save, Send, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { problemMessage } from '../app/WorkContext'
import { formatMovementDate } from '../app/demo-data'

type Provider = 'Password' | 'Microsoft' | 'Google'
type Security = 'Ssl' | 'StartTls' | 'None'
type MailAccount = {
  configured: boolean
  provider: Provider
  address: string
  displayName: string | null
  smtpHost: string | null
  smtpPort: number | null
  smtpSecurity: Security
  imapHost: string | null
  imapPort: number | null
  imapSecurity: Security
  imapFolder: string
  username: string | null
  hasSecret: boolean
  authorizedAccount: string | null
  dailyDigest: boolean
  pollingEnabled: boolean
  lastUsedAtUtc: string | null
  lastError: string | null
}
type AccountResponse = { account: MailAccount; oAuth: { microsoft: boolean; google: boolean } }

const providerLabels: Record<Provider, string> = {
  Password: 'Servidor próprio ou do alojamento (SMTP/IMAP)',
  Microsoft: 'Microsoft 365 / Outlook',
  Google: 'Gmail / Google Workspace',
}
const securityLabels: Record<Security, string> = { StartTls: 'STARTTLS (portas 587 e 143)', Ssl: 'SSL/TLS (portas 465 e 993)', None: 'Sem cifra' }

/**
 * A configuração de uma conta de email (ADR-0019): a do utilizador, para a aplicação enviar relatórios e
 * avisos em nome dele, ou a caixa de receção do gabinete. Com Microsoft 365 ou Google, o login — e o MFA — faz-se
 * na página do fornecedor e a aplicação só guarda um token de renovação; com servidor próprio, usa-se uma
 * senha de aplicação quando a conta tem MFA. A senha nunca volta ao ecrã.
 */
export function EmailAccountPanel({ firmId }: { firmId?: string }) {
  const isFirm = firmId !== undefined
  const base = isFirm ? `/api/v1/mail/firms/${firmId}/inbox-account` : '/api/v1/mail/account'
  const [data, setData] = useState<AccountResponse | null>(null)
  const [form, setForm] = useState<MailAccount | null>(null)
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    const response = await fetch(base)
    if (!response.ok) throw new Error(await problemMessage(response))
    const value = await response.json() as AccountResponse
    setData(value)
    setForm(value.account)
  }, [base])

  useEffect(() => {
    void load().catch((loadError: unknown) => setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar a conta de email.'))
  }, [load])

  // O fornecedor devolve ao ecrã com `?email=ligado` ou `?email=erro&detalhe=…`.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const result = params.get('email')
    if (!result) return
    if (result === 'ligado') setMessage('Conta ligada. Já pode enviar um email de teste.')
    else setError(params.get('detalhe') ?? 'Não foi possível ligar a conta.')
    window.history.replaceState({}, '', window.location.pathname)
  }, [])

  if (!form || !data) {
    return <section className="panel"><div className="panel-heading"><div><h2><Mail size={17} /> {isFirm ? 'Caixa de correio do gabinete' : 'A minha conta de email'}</h2><p>{error || 'A carregar…'}</p></div></div></section>
  }

  const patch = (changes: Partial<MailAccount>) => setForm({ ...form, ...changes })
  const oauthReady = form.provider === 'Microsoft' ? data.oAuth.microsoft : form.provider === 'Google' ? data.oAuth.google : true

  async function call(label: string, action: () => Promise<string | void>) {
    setBusy(label); setError(''); setMessage('')
    try {
      const done = await action()
      if (done) setMessage(done)
    } catch (callError) {
      setError(callError instanceof Error ? callError.message : 'Não foi possível concluir a operação.')
      await load().catch(() => undefined) // o servidor regista o último erro na conta
    } finally {
      setBusy('')
    }
  }

  const save = (event?: FormEvent) => {
    event?.preventDefault()
    return call('save', async () => {
      const response = await fetch(base, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...form, password: password || null }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setPassword('')
      await load()
      return 'Conta guardada.'
    })
  }

  const connect = () => call('connect', async () => {
    // Grava primeiro o endereço (e o tipo), para o login do fornecedor já o sugerir.
    const saved = await fetch(base, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...form, password: null }) })
    if (!saved.ok) throw new Error(await problemMessage(saved))
    const params = new URLSearchParams({ provider: form.provider, address: form.address })
    if (firmId) params.set('firmId', firmId)
    const response = await fetch(`/api/v1/mail/oauth/start?${params}`)
    if (!response.ok) throw new Error(await problemMessage(response))
    window.location.assign((await response.json() as { url: string }).url)
  })

  const test = () => call('test', async () => {
    const response = await fetch(`${base}/test`, { method: 'POST' })
    if (!response.ok) throw new Error(await problemMessage(response))
    await load()
    return isFirm ? 'Ligação à caixa bem-sucedida.' : `Email de teste enviado para ${form.address}.`
  })

  const poll = () => call('poll', async () => {
    const response = await fetch(`${base}/poll`, { method: 'POST' })
    if (!response.ok) throw new Error(await problemMessage(response))
    const result = await response.json() as { read: number; archived: number; needsCompany: number; ignored: number }
    await load()
    return `${result.read} ${result.read === 1 ? 'email lido' : 'emails lidos'}: ${result.archived} arquivados, ${result.needsCompany} à espera de empresa, ${result.ignored} sem documentos.`
  })

  const remove = () => call('remove', async () => {
    const response = await fetch(base, { method: 'DELETE' })
    if (!response.ok && response.status !== 404) throw new Error(await problemMessage(response))
    setPassword('')
    await load()
    return 'Conta removida.'
  })

  const oauth = form.provider !== 'Password'
  return <section className="panel settings-panel" aria-label={isFirm ? 'Caixa de correio do gabinete' : 'A minha conta de email'}>
    <div className="panel-heading"><div>
      <h2><Mail size={17} /> {isFirm ? 'Caixa de correio do gabinete' : 'A minha conta de email'}</h2>
      <p>{isFirm
        ? 'A caixa onde clientes e fornecedores enviam faturas e recibos. A aplicação lê-a de 5 em 5 minutos e arquiva os anexos na empresa certa (pelo NIF no assunto, no texto ou no nome do ficheiro, ou pelo remetente); o que não souber atribuir espera aqui.'
        : 'A conta de onde a aplicação envia, em seu nome, os relatórios e os avisos. Com Microsoft 365 ou Google, o login e o MFA fazem-se na página do fornecedor; com servidor próprio e MFA, use uma senha de aplicação.'}</p>
    </div>
      <span className={`badge ${form.hasSecret ? 'success' : ''}`}>{form.hasSecret ? 'Ligada' : 'Não ligada'}</span>
    </div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}
    {form.lastError && !error && <p className="save-status error"><CircleAlert size={15} /> Último erro: {form.lastError}</p>}

    <form className="settings-grid" onSubmit={(event) => void save(event)}>
      <label><span>Tipo de conta</span><select value={form.provider} onChange={(event) => patch({ provider: event.target.value as Provider })}>
        {(Object.keys(providerLabels) as Provider[]).map((provider) => <option key={provider} value={provider}>{providerLabels[provider]}</option>)}</select></label>
      <label><span>Endereço de email</span><input type="email" value={form.address} onChange={(event) => patch({ address: event.target.value })} placeholder={isFirm ? 'faturas@gabinete.pt' : 'nome@empresa.pt'} /></label>
      {!isFirm && <label><span>Nome que aparece no envio</span><input value={form.displayName ?? ''} onChange={(event) => patch({ displayName: event.target.value })} /></label>}

      {!oauth && <>
        <div style={{ gridColumn: '1 / -1' }}><button className="button button-secondary" type="button" onClick={() => patch({ smtpHost: 'smtp.office365.com', smtpPort: 587, smtpSecurity: 'StartTls', imapHost: 'outlook.office365.com', imapPort: 993, imapSecurity: 'Ssl' })}>Preencher os servidores do Office 365</button> <small>Com a senha da conta — ou uma senha de aplicação, se tem MFA. O administrador do Office 365 tem de permitir o «SMTP autenticado» nesta caixa.</small></div>
        <label><span>Servidor de envio (SMTP)</span><input value={form.smtpHost ?? ''} onChange={(event) => patch({ smtpHost: event.target.value })} placeholder="smtp.exemplo.pt" /></label>
        <label><span>Porta SMTP</span><input type="number" min="1" max="65535" value={form.smtpPort ?? ''} onChange={(event) => patch({ smtpPort: event.target.value ? Number(event.target.value) : null })} /></label>
        <label><span>Segurança do envio</span><select value={form.smtpSecurity} onChange={(event) => patch({ smtpSecurity: event.target.value as Security })}>{(Object.keys(securityLabels) as Security[]).map((value) => <option key={value} value={value}>{securityLabels[value]}</option>)}</select></label>
        {isFirm && <>
          <label><span>Servidor de receção (IMAP)</span><input value={form.imapHost ?? ''} onChange={(event) => patch({ imapHost: event.target.value })} placeholder="imap.exemplo.pt" /></label>
          <label><span>Porta IMAP</span><input type="number" min="1" max="65535" value={form.imapPort ?? ''} onChange={(event) => patch({ imapPort: event.target.value ? Number(event.target.value) : null })} /></label>
          <label><span>Segurança da receção</span><select value={form.imapSecurity} onChange={(event) => patch({ imapSecurity: event.target.value as Security })}>{(Object.keys(securityLabels) as Security[]).map((value) => <option key={value} value={value}>{securityLabels[value]}</option>)}</select></label>
        </>}
        <label><span>Utilizador (se não for o endereço)</span><input value={form.username ?? ''} onChange={(event) => patch({ username: event.target.value })} autoComplete="off" /></label>
        <label><span>Senha{form.hasSecret ? ' (deixe em branco para manter)' : ''}</span><input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" placeholder={form.hasSecret ? '••••••••' : 'Senha de aplicação, se a conta tem MFA'} /></label>
      </>}

      {isFirm && <label><span>Pasta a ler</span><input value={form.imapFolder} onChange={(event) => patch({ imapFolder: event.target.value })} /></label>}
      {isFirm
        ? <label className="toggle-field"><input type="checkbox" checked={form.pollingEnabled} onChange={(event) => patch({ pollingEnabled: event.target.checked })} /><span><strong>Ler a caixa automaticamente</strong><small>De 5 em 5 minutos, com o servidor da aplicação ligado.</small></span></label>
        : <label className="toggle-field"><input type="checkbox" checked={form.dailyDigest} onChange={(event) => patch({ dailyDigest: event.target.checked })} /><span><strong>Receber o resumo diário</strong><small>Nos dias úteis, as obrigações em atraso ou a vencer das empresas de que é responsável e as suas tarefas.</small></span></label>}

      <footer className="settings-actions" style={{ gridColumn: '1 / -1' }}>
        {oauth && <button className="button button-primary" type="button" disabled={busy !== '' || !form.address || !oauthReady} onClick={() => void connect()} title={oauthReady ? undefined : 'O produtor da aplicação ainda não registou a aplicação OAuth deste fornecedor.'}>
          <RefreshCw size={16} /> {form.hasSecret ? 'Ligar de novo' : `Ligar com ${form.provider === 'Microsoft' ? 'a Microsoft' : 'a Google'}`}</button>}
        {oauth && !oauthReady && <small>O produtor da aplicação ainda não registou a aplicação OAuth {form.provider === 'Microsoft' ? 'da Microsoft' : 'da Google'}.</small>}
        {oauth && form.authorizedAccount && <small>Conta autorizada: {form.authorizedAccount}</small>}
        <button className="button button-secondary" type="submit" disabled={busy !== '' || !form.address}><Save size={16} /> {busy === 'save' ? 'A guardar…' : 'Guardar'}</button>
        <button className="button button-secondary" type="button" disabled={busy !== '' || !form.configured || !form.hasSecret} onClick={() => void test()}><Send size={16} /> {isFirm ? 'Testar a ligação' : 'Enviar email de teste'}</button>
        {isFirm && <button className="button button-secondary" type="button" disabled={busy !== '' || !form.hasSecret} onClick={() => void poll()}><RefreshCw size={16} /> Ler agora</button>}
        {form.configured && <button className="button button-secondary" type="button" disabled={busy !== ''} onClick={() => void remove()}><Trash2 size={16} /> Remover</button>}
        {form.lastUsedAtUtc && <small>Última utilização: {formatMovementDate(form.lastUsedAtUtc.slice(0, 10))}</small>}
      </footer>
    </form>
  </section>
}
