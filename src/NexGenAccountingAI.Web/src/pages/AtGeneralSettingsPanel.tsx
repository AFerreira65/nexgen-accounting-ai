import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { formatMovementDate, type AtGeneralSettingsResponse } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

const sourceLabels: Record<string, string> = { geral: 'configuração geral', ficheiro: 'ficheiro de configuração do servidor', nenhuma: 'não configurado' }

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

/**
 * A configuração geral da AT, comum a todas as empresas: onde estão o certificado de transporte e a
 * chave pública da AT, e a senha do certificado (guardada cifrada, nunca devolvida). Substitui a
 * secção At do ficheiro de configuração, que fica como alternativa. Só o dono ou um administrador
 * de uma empresa a altera — o servidor é que o garante.
 */
export function AtGeneralSettingsPanel({ onChanged }: { onChanged?: () => void }) {
  const { atGeneralSettings, saveAtGeneralSettings } = useWorkContext()
  const [data, setData] = useState<AtGeneralSettingsResponse | null>(null)
  const [certificateDirectory, setCertificateDirectory] = useState('')
  const [certificateFileName, setCertificateFileName] = useState('')
  const [publicKeyDirectory, setPublicKeyDirectory] = useState('')
  const [publicKeyFileName, setPublicKeyFileName] = useState('')
  const [password, setPassword] = useState('')
  const [removePassword, setRemovePassword] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)

  const apply = useCallback((value: AtGeneralSettingsResponse) => {
    setData(value)
    setCertificateDirectory(value.settings.certificateDirectory ?? '')
    setCertificateFileName(value.settings.certificateFileName ?? '')
    setPublicKeyDirectory(value.settings.publicKeyDirectory ?? '')
    setPublicKeyFileName(value.settings.publicKeyFileName ?? '')
    setPassword('')
    setRemovePassword(false)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void atGeneralSettings()
      .then((value) => { if (!controller.signal.aborted) apply(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível ler a configuração geral da AT.') })
    return () => controller.abort()
  }, [atGeneralSettings, apply])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true); setError(''); setMessage('')
    try {
      apply(await saveAtGeneralSettings({ certificateDirectory, certificateFileName, publicKeyDirectory, publicKeyFileName, password, removePassword }))
      setMessage('Configuração guardada (a senha fica cifrada).')
      onChanged?.()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a configuração geral da AT.')
    } finally {
      setBusy(false)
    }
  }

  if (!data) return error ? <p className="save-status error">{error}</p> : null

  const { settings, certificate, publicKey } = data
  if (!settings.canEdit) {
    return <section className="panel">
      <div className="panel-heading"><div><h2>Certificados da AT (configuração geral)</h2><p>Vale para todas as empresas e só o dono ou um administrador de uma empresa a vê e altera.</p></div></div>
    </section>
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Certificados da AT (configuração geral)</h2><p>Onde estão o certificado de transporte e a chave pública da AT, e a senha do certificado. Vale para todas as empresas. Se ficar em branco, usa-se a secção <code>At</code> do ficheiro de configuração do servidor.</p></div></div>
    {error && <p className="save-status error">{error}</p>}
    {message && !error && <p className="save-status success">{message}</p>}
    <form className="builder-row" onSubmit={(event) => void submit(event)}>
      <label>Pasta do certificado (PFX/P12)
        <input value={certificateDirectory} onChange={(event) => setCertificateDirectory(event.target.value)} placeholder="C:\AT\certificados" autoComplete="off" />
      </label>
      <label>Nome do ficheiro do certificado
        <input value={certificateFileName} onChange={(event) => setCertificateFileName(event.target.value)} placeholder="TesteWebservices.pfx" autoComplete="off" />
      </label>
      <label>Senha do certificado
        <input type="password" value={password} onChange={(event) => { setPassword(event.target.value); setRemovePassword(false) }} placeholder={settings.passwordConfigured ? '•••••••• (guardada; em branco mantém)' : ''} autoComplete="new-password" />
      </label>
      <label>Pasta da chave pública da AT (CER)
        <input value={publicKeyDirectory} onChange={(event) => setPublicKeyDirectory(event.target.value)} placeholder="C:\AT\chaves" autoComplete="off" />
      </label>
      <label>Nome do ficheiro da chave pública
        <input value={publicKeyFileName} onChange={(event) => setPublicKeyFileName(event.target.value)} placeholder="ChaveCifraPublicaAT2027.cer" autoComplete="off" />
      </label>
      {settings.passwordConfigured && <label className="inline-check"><input type="checkbox" checked={removePassword} disabled={password !== ''} onChange={(event) => setRemovePassword(event.target.checked)} /> Retirar a senha guardada (certificado sem senha)</label>}
      <button type="submit" className="button button-primary" disabled={busy}>{busy ? 'A guardar…' : 'Guardar'}</button>
    </form>
    <p className="mr-hint">A senha é cifrada no servidor (Data Protection do ASP.NET Core) e nunca volta a ser mostrada. As pastas e os ficheiros têm de existir no computador onde corre a API. {settings.updatedAtUtc ? `Atualizada em ${formatDate(settings.updatedAtUtc)}.` : 'Ainda não há configuração geral guardada.'}</p>
    <div className="table-wrap"><table><tbody>
      <tr><td>Certificado de transporte</td><td>{certificate ? (certificate.configured ? (certificate.valid ? 'Válido' : 'Com problema') : 'Não configurado') : '—'} <small>({sourceLabels[certificate?.source ?? 'nenhuma']})</small>{certificate?.problem && <><br /><small>{certificate.problem}</small></>}</td></tr>
      <tr><td>Chave pública da AT</td><td>{publicKey ? (publicKey.configured ? (publicKey.valid ? 'Válida' : 'Com problema') : 'Não configurada') : '—'} <small>({sourceLabels[publicKey?.source ?? 'nenhuma']})</small>{publicKey?.notAfterUtc && <> <small>— válida até {formatDate(publicKey.notAfterUtc)}</small></>}{publicKey?.problem && <><br /><small>{publicKey.problem}</small></>}</td></tr>
    </tbody></table></div>
  </section>
}
