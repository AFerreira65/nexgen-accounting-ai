import { Database, ShieldCheck } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { formatMovementDate, type AtCertificateStatus, type AtCredentials } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

export function AtCommunicationPage() {
  const { activeCompany, source, atCertificateStatus, atCredentials, saveAtCredentials, deleteAtCredentials } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [status, setStatus] = useState<AtCertificateStatus | null>(null)
  const [error, setError] = useState('')

  const [credentials, setCredentials] = useState<AtCredentials | null>(null)
  const [subUser, setSubUser] = useState('')
  const [password, setPassword] = useState('')
  const [periodicity, setPeriodicity] = useState<'Mensal' | 'Trimestral'>('Mensal')
  const [environment, setEnvironment] = useState<'Testes' | 'Producao'>('Testes')
  const [credError, setCredError] = useState('')
  const [credStatus, setCredStatus] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    if (!onDatabase) { setStatus(null); return }
    const controller = new AbortController()
    setError('')
    void atCertificateStatus()
      .then((value) => { if (!controller.signal.aborted) setStatus(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível ler o estado do certificado.') })
    return () => controller.abort()
  }, [onDatabase, atCertificateStatus])

  useEffect(() => {
    if (!onDatabase) { setCredentials(null); return }
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
  }, [onDatabase, companyId, atCredentials, reload])

  const submitCredentials = async (event: FormEvent) => {
    event.preventDefault()
    if (!subUser.trim() || !password) { setCredError('Indique o subutilizador e a senha.'); return }
    setBusy(true); setCredError(''); setCredStatus('')
    try {
      const next = await saveAtCredentials(companyId, { subUser: subUser.trim(), password, periodicity, environment })
      setCredentials(next)
      setPassword('')
      setCredStatus('Credenciais guardadas (a senha fica cifrada).')
      setReload((value) => value + 1)
    } catch (problem) {
      setCredError(problem instanceof Error ? problem.message : 'Não foi possível guardar as credenciais.')
    } finally {
      setBusy(false)
    }
  }

  const removeCredentials = async () => {
    setBusy(true); setCredError(''); setCredStatus('')
    try {
      await deleteAtCredentials(companyId)
      setCredentials({ configured: false, subUser: null, periodicity: 'Mensal', environment: 'Testes', updatedAtUtc: null })
      setSubUser('')
      setPassword('')
      setPeriodicity('Mensal')
      setEnvironment('Testes')
      setReload((value) => value + 1)
    } catch (problem) {
      setCredError(problem instanceof Error ? problem.message : 'Não foi possível remover as credenciais.')
    } finally {
      setBusy(false)
    }
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Integração</p><h1>Comunicação com a AT</h1><p>O certificado de transporte e o estado da ligação à AT.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><ShieldCheck /></span><h2>Requer ligação à base de dados</h2><p>O estado do certificado é lido pela API local.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Integração</p><h1>Comunicação com a AT</h1><p>O certificado de transporte identifica o produtor na ligação SOAP à AT. Este ecrã mostra o estado do certificado configurado e as credenciais da empresa — a senha e a chave privada nunca saem do servidor.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    {status && <section className="panel">
      <div className="panel-heading"><div><h2>Certificado de transporte</h2><p>Ambiente: {status.environment === 'Producao' ? 'Produção' : 'Testes'}.</p></div>
        <span className={`badge ${status.configured && status.valid ? 'success' : status.configured ? 'warning' : ''}`}>
          {!status.configured ? 'Não configurado' : status.valid ? 'Válido' : 'Com problema'}
        </span>
      </div>
      {status.problem && <p className="mr-hint">{status.problem}</p>}
      {status.configured && <div className="table-wrap"><table><tbody>
        <tr><td>Assunto</td><td>{status.subject ?? '—'}</td></tr>
        <tr><td>Emissor</td><td>{status.issuer ?? '—'}</td></tr>
        <tr><td>Número de série</td><td>{status.serialNumber ?? '—'}</td></tr>
        <tr><td>Impressão digital</td><td>{status.thumbprint ?? '—'}</td></tr>
        <tr><td>Válido de</td><td>{formatDate(status.notBeforeUtc)} a {formatDate(status.notAfterUtc)}</td></tr>
        <tr><td>Tem chave privada</td><td>{status.hasPrivateKey ? 'Sim' : 'Não'}</td></tr>
      </tbody></table></div>}
    </section>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Configuração da AT de {activeCompany.legalName}</h2><p>O subutilizador e a senha do perfil e-Fatura, a periodicidade da declaração periódica e o ambiente da AT — tudo por empresa. A senha é guardada cifrada.</p></div>
        <span className={`badge ${credentials?.configured ? 'success' : ''}`}>{credentials?.configured ? 'Configuradas' : 'Não configuradas'}</span>
      </div>
      {credError && <p className="save-status error">{credError}</p>}
      {credStatus && !credError && <p className="save-status success">{credStatus}</p>}
      <form className="builder-row" onSubmit={(event) => void submitCredentials(event)}>
        <label>Subutilizador
          <input value={subUser} onChange={(event) => setSubUser(event.target.value)} placeholder={`${activeCompany.taxRegistrationNumber}/1`} />
        </label>
        <label>Senha
          <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder={credentials?.configured ? '•••••••• (guardada)' : ''} autoComplete="off" />
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
        {credentials?.configured && <button type="button" className="button button-secondary" disabled={busy} onClick={() => void removeCredentials()}>Remover</button>}
      </form>
      {credentials?.configured && <p className="mr-hint">Regime {credentials.periodicity === 'Trimestral' ? 'trimestral' : 'mensal'}, ambiente {credentials.environment === 'Producao' ? 'de produção' : 'de testes'}.</p>}
      {credentials?.updatedAtUtc && <p className="mr-hint">Atualizadas em {formatDate(credentials.updatedAtUtc)}.</p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Onde se configura o certificado</h2><p>No servidor, não pela interface.</p></div></div>
      <ul className="config-hints">
        <li>Local e nome do ficheiro: <code>At:Certificate:Directory</code> e <code>At:Certificate:FileName</code> (ou <code>At:Certificate:Path</code> absoluto).</li>
        <li>Senha do certificado: <code>At:Certificate:Password</code>, guardada <strong>cifrada</strong> (valor <code>enc:v1:…</code>), nunca em texto.</li>
        <li>Para cifrar um valor: <code>dotnet run --project src/NexGenAccountingAI.Api --no-launch-profile -- protect-at-secret "&lt;valor&gt;"</code> — cola o resultado na configuração. O local do ficheiro também pode ser cifrado da mesma forma.</li>
        <li>Ambiente: <code>At:Environment</code> = <code>Testes</code> ou <code>Producao</code>.</li>
        <li>Para os testes, aponta-se para o certificado do PHC.</li>
      </ul>
      <p className="mr-hint">A cifra usa a Data Protection do ASP.NET Core, com o chaveiro em <code>%LOCALAPPDATA%\AccountingAI\dp-keys</code> protegido pelo DPAPI do Windows — os valores cifrados só se decifram nesta máquina e conta. As chamadas SOAP à AT ficam a aguardar os elementos de teste e a aceitação da adesão.</p>
    </section>
  </div>
}
