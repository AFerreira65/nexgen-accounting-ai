import { Check, CircleAlert, KeyRound, Save, Sparkles, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { problemMessage } from '../app/WorkContext'
import { formatMovementDate } from '../app/demo-data'

type Status = { key: { configured: boolean; hint: string | null; updatedAtUtc: string | null }; source: 'geral' | 'ficheiro' | 'nenhuma' }

const root = '/api/v1/producer/ai-settings'
const sourceLabels = { geral: 'a configuração geral, guardada na base de dados', ficheiro: 'o ficheiro de configuração ou a variável de ambiente do servidor', nenhuma: 'nenhuma' } as const

/**
 * A chave da API da Anthropic, só do produtor: fica na configuração geral, cifrada, e vale para todas as empresas e para todos os
 * postos. Cada empresa continua a decidir, nas suas definições, se a IA está ligada. A chave nunca volta ao ecrã.
 */
export function AiKeyPanel() {
  const [status, setStatus] = useState<Status | null>(null)
  const [apiKey, setApiKey] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const load = useCallback(async () => {
    const response = await fetch(root)
    if (!response.ok) throw new Error(await problemMessage(response))
    const value = await response.json() as Status
    if (!value.key) throw new Error('Resposta inesperada do servidor.')
    setStatus(value)
  }, [])

  useEffect(() => { void load().catch((loadError: unknown) => setError(loadError instanceof Error ? loadError.message : 'Não foi possível carregar.')) }, [load])

  async function run(label: string, action: () => Promise<string | void>) {
    setBusy(label); setError(''); setMessage('')
    try {
      const done = await action()
      await load()
      if (done) setMessage(done)
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : 'Não foi possível concluir a operação.')
    } finally {
      setBusy('')
    }
  }

  const save = (event: FormEvent) => {
    event.preventDefault()
    return run('save', async () => {
      const response = await fetch(root, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ apiKey }) })
      if (!response.ok) throw new Error(await problemMessage(response))
      setApiKey('')
      return 'Chave guardada, cifrada. Vale já para todas as empresas.'
    })
  }

  const test = () => run('test', async () => {
    const response = await fetch(`${root}/test`, { method: 'POST' })
    if (!response.ok) throw new Error(await problemMessage(response))
    const result = await response.json() as { ok: boolean; message: string | null }
    if (!result.ok) throw new Error(`A Anthropic não aceitou a chave: ${result.message ?? 'sem detalhe'}`)
    return 'A Anthropic aceitou a chave.'
  })

  const remove = () => run('remove', async () => {
    const response = await fetch(root, { method: 'DELETE' })
    if (!response.ok && response.status !== 404) throw new Error(await problemMessage(response))
    return 'Chave retirada da configuração geral.'
  })

  return <section className="panel" aria-label="Chave da IA">
    <div className="panel-heading"><div>
      <h2><Sparkles size={17} /> IA — chave da Anthropic</h2>
      <p>A chave de acesso à IA (Claude), comum a todas as empresas. Fica cifrada na base de dados e nunca volta a ser mostrada. Cada empresa decide, nas suas definições, se a IA está ligada.</p>
    </div>
      <span className={`badge ${status?.key.configured ? 'success' : ''}`}>{status?.key.configured ? 'Configurada' : 'Não configurada'}</span>
    </div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {message && !error && <p className="save-status success"><Check size={15} /> {message}</p>}
    {status && <p className="mr-hint">
      {status.key.configured ? <>Chave guardada, termina em <code>…{status.key.hint}</code>{status.key.updatedAtUtc ? ` (${formatMovementDate(status.key.updatedAtUtc.slice(0, 10))})` : ''}. </> : null}
      Em uso: {sourceLabels[status.source]}.
    </p>}
    <form className="builder-row" onSubmit={(event) => void save(event)}>
      <label>Chave da API (sk-ant-…)
        <input type="password" value={apiKey} onChange={(event) => setApiKey(event.target.value)} autoComplete="off" placeholder={status?.key.configured ? '••••••••  (escreva para substituir)' : 'sk-ant-…'} style={{ minWidth: 340 }} />
      </label>
      <button type="submit" className="button button-primary" disabled={busy !== '' || !apiKey.trim()}><Save size={15} /> {busy === 'save' ? 'A guardar…' : 'Guardar a chave'}</button>
      <button type="button" className="button button-secondary" disabled={busy !== '' || status?.source === 'nenhuma'} onClick={() => void test()}><KeyRound size={15} /> {busy === 'test' ? 'A testar…' : 'Testar a chave'}</button>
      {status?.key.configured && <button type="button" className="button" disabled={busy !== ''} onClick={() => void remove()}><Trash2 size={15} /> Retirar a chave</button>}
    </form>
  </section>
}
