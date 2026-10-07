import { KeyRound, ShieldCheck, Trash2, Upload } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { problemMessage } from '../app/WorkContext'
import { formatMovementDate, type AtCertificateStatus, type AtPublicKeyStatus } from '../app/demo-data'

export type ProducerAtSettings = {
  settings: {
    certificateFileName: string | null
    publicKeyFileName: string | null
    passwordConfigured: boolean
    updatedAtUtc: string | null
  }
  certificate: AtCertificateStatus
  publicKey: AtPublicKeyStatus
}

const root = '/api/v1/producer/at-settings'
const sourceLabels: Record<string, string> = { geral: 'configuração geral, na base de dados', ficheiro: 'ficheiro de configuração do servidor', nenhuma: 'não configurado' }

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

/** O conteúdo do ficheiro em base64, sem o prefixo do «data URL». */
function readBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result).replace(/^data:[^,]*,/, ''))
    reader.onerror = () => reject(new Error('Não foi possível ler o ficheiro escolhido.'))
    reader.readAsDataURL(file)
  })
}

/**
 * A configuração geral da aplicação, só do produtor (ADR-0017): o certificado de transporte, a
 * senha dele e a chave pública da AT. Os ficheiros ficam na base de dados — qualquer posto os usa
 * sem instalar nada — e a senha fica cifrada. Nada disto volta do servidor: só o estado.
 */
export function ProducerSettingsPage() {
  const [data, setData] = useState<ProducerAtSettings | null>(null)
  const [certificateFile, setCertificateFile] = useState<File | null>(null)
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [publicKeyFile, setPublicKeyFile] = useState<File | null>(null)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    void fetch(root, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(await problemMessage(response))
        if (!controller.signal.aborted) setData(await response.json() as ProducerAtSettings)
      })
      .catch((problem: unknown) => {
        if (controller.signal.aborted) return
        setError(problem instanceof Error ? problem.message : 'Não foi possível ler a configuração geral.')
      })
    return () => controller.abort()
  }, [])

  const change = useCallback(async (method: 'PUT' | 'DELETE', path: string, body: unknown, done: string) => {
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await fetch(`${root}${path}`, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setData(await response.json() as ProducerAtSettings)
      setMessage(done)
      return true
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a alteração.')
      return false
    } finally {
      setBusy(false)
    }
  }, [])

  const installCertificate = async (event: FormEvent) => {
    event.preventDefault()
    if (!certificateFile) { setError('Escolha o ficheiro do certificado (PFX/P12).'); return }
    let contentBase64: string
    try { contentBase64 = await readBase64(certificateFile) } catch (problem) { setError(problem instanceof Error ? problem.message : 'Não foi possível ler o ficheiro.'); return }
    if (await change('PUT', '/certificate', { fileName: certificateFile.name, contentBase64, password }, 'Certificado guardado na base de dados; a senha ficou cifrada.')) {
      setCertificateFile(null); setPassword('')
    }
  }

  const changePassword = async (event: FormEvent) => {
    event.preventDefault()
    if (await change('PUT', '/password', { password: newPassword }, 'Senha do certificado atualizada (cifrada).')) setNewPassword('')
  }

  const installPublicKey = async (event: FormEvent) => {
    event.preventDefault()
    if (!publicKeyFile) { setError('Escolha o ficheiro da chave pública (CER).'); return }
    let contentBase64: string
    try { contentBase64 = await readBase64(publicKeyFile) } catch (problem) { setError(problem instanceof Error ? problem.message : 'Não foi possível ler o ficheiro.'); return }
    if (await change('PUT', '/public-key', { fileName: publicKeyFile.name, contentBase64 }, 'Chave pública guardada na base de dados.')) setPublicKeyFile(null)
  }

  const { settings, certificate, publicKey } = data ?? {}

  return <div className="page-stack">
    <header className="page-heading"><div>
      <p className="eyebrow">Produtor</p>
      <h1>Configuração geral da aplicação</h1>
      <p>O certificado de transporte e a chave pública da AT valem para todas as empresas e para todos os postos. Ficam na base de dados — nenhum posto precisa de os ter instalados — e a senha fica cifrada. Esta área só existe para a conta do produtor.</p>
    </div></header>

    {error && <p className="save-status error" role="alert">{error}</p>}
    {message && !error && <p className="save-status success">{message}</p>}
    {!data && !error && <p className="mr-hint" aria-busy="true">A ler a configuração…</p>}

    {data && settings && certificate && publicKey && <>
      <section className="panel">
        <div className="panel-heading"><div><h2><ShieldCheck size={16} /> Certificado de transporte</h2>
          <p>O ficheiro PFX/P12 que a AT enviou e a senha dele. Origem em uso: {sourceLabels[certificate.source]}.</p></div>
          <span className={`badge ${certificate.configured && certificate.valid ? 'success' : certificate.configured ? 'warning' : ''}`}>
            {!certificate.configured ? 'Não configurado' : certificate.valid ? 'Válido' : 'Com problema'}
          </span>
        </div>
        {certificate.problem && <p className="mr-hint">{certificate.problem}</p>}
        {certificate.configured && <div className="table-wrap"><table><tbody>
          {settings.certificateFileName && <tr><td>Ficheiro</td><td>{settings.certificateFileName}</td></tr>}
          <tr><td>Assunto</td><td>{certificate.subject ?? '—'}</td></tr>
          <tr><td>Válido de</td><td>{formatDate(certificate.notBeforeUtc)} a {formatDate(certificate.notAfterUtc)}</td></tr>
          <tr><td>Senha</td><td>{settings.passwordConfigured ? 'Guardada, cifrada (nunca é mostrada)' : certificate.source === 'ficheiro' ? 'A do ficheiro de configuração do servidor' : 'Sem senha guardada'}</td></tr>
        </tbody></table></div>}

        <form className="builder-row" onSubmit={(event) => void installCertificate(event)}>
          <label>Ficheiro do certificado (PFX/P12)
            <input type="file" accept=".pfx,.p12" onChange={(event) => setCertificateFile(event.target.files?.[0] ?? null)} />
          </label>
          <label>Senha do certificado
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" />
          </label>
          <button type="submit" className="button button-primary" disabled={busy}><Upload size={15} />{busy ? 'A guardar…' : 'Guardar o certificado'}</button>
        </form>

        {settings.certificateFileName && <form className="builder-row" onSubmit={(event) => void changePassword(event)}>
          <label>Nova senha do certificado guardado
            <input type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} autoComplete="new-password" />
          </label>
          <button type="submit" className="button" disabled={busy}><KeyRound size={15} />Trocar a senha</button>
          <button type="button" className="button" disabled={busy} onClick={() => void change('DELETE', '/certificate', undefined, 'Certificado retirado, com a senha.')}><Trash2 size={15} />Retirar o certificado</button>
        </form>}
        <p className="mr-hint">A senha tem de abrir o ficheiro: se não abrir, nada é guardado. {settings.updatedAtUtc ? `Última alteração em ${formatDate(settings.updatedAtUtc)}.` : 'Ainda não há configuração geral guardada.'}</p>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2><KeyRound size={16} /> Chave pública da AT</h2>
          <p>O ficheiro CER com que se cifra a senha enviada à AT. Origem em uso: {sourceLabels[publicKey.source]}.</p></div>
          <span className={`badge ${publicKey.configured && publicKey.valid ? 'success' : publicKey.configured ? 'warning' : ''}`}>
            {!publicKey.configured ? 'Não configurada' : publicKey.valid ? 'Válida' : 'Com problema'}
          </span>
        </div>
        {publicKey.problem && <p className="mr-hint">{publicKey.problem}</p>}
        {publicKey.configured && <div className="table-wrap"><table><tbody>
          <tr><td>Ficheiro</td><td>{publicKey.fileName ?? '—'}</td></tr>
          <tr><td>Assunto</td><td>{publicKey.subject ?? '—'}</td></tr>
          <tr><td>Válida até</td><td>{formatDate(publicKey.notAfterUtc)}</td></tr>
        </tbody></table></div>}
        <form className="builder-row" onSubmit={(event) => void installPublicKey(event)}>
          <label>Ficheiro da chave pública (CER)
            <input type="file" accept=".cer,.crt,.pem" onChange={(event) => setPublicKeyFile(event.target.files?.[0] ?? null)} />
          </label>
          <button type="submit" className="button button-primary" disabled={busy}><Upload size={15} />Guardar a chave pública</button>
          {settings.publicKeyFileName && <button type="button" className="button" disabled={busy} onClick={() => void change('DELETE', '/public-key', undefined, 'Chave pública retirada.')}><Trash2 size={15} />Retirar a chave pública</button>}
        </form>
      </section>

      <p className="mr-hint">A cifra da senha funciona em qualquer posto que tenha o programa e esta base de dados, sem instalar chaves. Protege a base de dados e as cópias de segurança contra leitura casual; quem tiver a base de dados e o programa pode decifrá-la, por isso o acesso a esta área é só da conta do produtor e a API nunca devolve a senha, o valor cifrado nem os ficheiros. Uma alteração vale já neste posto e nos outros ao fim de uns segundos.</p>
    </>}
  </div>
}
