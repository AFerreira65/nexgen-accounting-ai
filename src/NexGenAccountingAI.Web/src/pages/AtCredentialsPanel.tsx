import { useEffect, useState, type FormEvent } from 'react'
import { formatMovementDate, type AtCredentials } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

/**
 * O subutilizador e a senha da AT de uma empresa, com a periodicidade da declaração de IVA e o
 * ambiente da AT. A senha segue para o servidor, que a guarda cifrada, e nunca volta.
 * Usa-se na configuração da empresa e no ecrã «Comunicação com a AT».
 */
export function AtCredentialsPanel({ companyId, companyName, taxRegistrationNumber }: { companyId: string; companyName: string; taxRegistrationNumber: string }) {
  const { atCredentials, saveAtCredentials, deleteAtCredentials } = useWorkContext()
  const [credentials, setCredentials] = useState<AtCredentials | null>(null)
  const [subUser, setSubUser] = useState('')
  const [password, setPassword] = useState('')
  const [periodicity, setPeriodicity] = useState<'Mensal' | 'Trimestral'>('Mensal')
  const [environment, setEnvironment] = useState<'Testes' | 'Producao'>('Testes')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    void atCredentials(companyId)
      .then((value) => {
        if (controller.signal.aborted) return
        setCredentials(value)
        setSubUser(value.subUser ?? '')
        setPeriodicity(value.periodicity)
        setEnvironment(value.environment)
      })
      .catch(() => { if (!controller.signal.aborted) setCredentials(null) })
    return () => controller.abort()
  }, [companyId, atCredentials, reload])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!subUser.trim() || !password) { setError('Indique o subutilizador e a senha.'); return }
    setBusy(true); setError(''); setMessage('')
    try {
      setCredentials(await saveAtCredentials(companyId, { subUser: subUser.trim(), password, periodicity, environment }))
      setPassword('')
      setMessage('Credenciais guardadas (a senha fica cifrada).')
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar as credenciais.')
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    setBusy(true); setError(''); setMessage('')
    try {
      await deleteAtCredentials(companyId)
      setCredentials({ configured: false, subUser: null, periodicity: 'Mensal', environment: 'Testes', updatedAtUtc: null })
      setSubUser(''); setPassword(''); setPeriodicity('Mensal'); setEnvironment('Testes')
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível remover as credenciais.')
    } finally {
      setBusy(false)
    }
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Configuração da AT de {companyName}</h2><p>O subutilizador e a senha do Portal das Finanças desta empresa, a periodicidade da declaração periódica e o ambiente da AT. A senha é guardada cifrada e nunca volta a ser mostrada.</p></div>
      <span className={`badge ${credentials?.configured ? 'success' : ''}`}>{credentials?.configured ? 'Configuradas' : 'Não configuradas'}</span>
    </div>
    {error && <p className="save-status error">{error}</p>}
    {message && !error && <p className="save-status success">{message}</p>}
    <form className="builder-row" onSubmit={(event) => void submit(event)}>
      <label>Subutilizador
        <input value={subUser} onChange={(event) => setSubUser(event.target.value)} placeholder={`${taxRegistrationNumber}/1`} autoComplete="off" />
      </label>
      <label>Senha
        <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder={credentials?.configured ? '•••••••• (guardada)' : ''} autoComplete="new-password" />
      </label>
      <label>Periodicidade
        <select value={periodicity} onChange={(event) => setPeriodicity(event.target.value as 'Mensal' | 'Trimestral')}>
          <option value="Mensal">Mensal</option>
          <option value="Trimestral">Trimestral</option>
        </select>
      </label>
      <label>Ambiente da AT
        <select value={environment} onChange={(event) => setEnvironment(event.target.value as 'Testes' | 'Producao')}>
          <option value="Testes">Testes</option>
          <option value="Producao">Produção</option>
        </select>
      </label>
      <button type="submit" className="button button-primary" disabled={busy}>{busy ? 'A guardar…' : 'Guardar'}</button>
      {credentials?.configured && <button type="button" className="button button-secondary" disabled={busy} onClick={() => void remove()}>Remover</button>}
    </form>
    {credentials?.configured && <p className="mr-hint">Regime {credentials.periodicity === 'Trimestral' ? 'trimestral' : 'mensal'}, ambiente {credentials.environment === 'Producao' ? 'de produção' : 'de testes'}.</p>}
    {credentials?.updatedAtUtc && <p className="mr-hint">Atualizadas em {formatDate(credentials.updatedAtUtc)}.</p>}
  </section>
}
