import { Database, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { formatMovementDate, type AtCertificateStatus } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

export function AtCommunicationPage() {
  const { source, atCertificateStatus } = useWorkContext()
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [status, setStatus] = useState<AtCertificateStatus | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!onDatabase) { setStatus(null); return }
    const controller = new AbortController()
    setError('')
    void atCertificateStatus()
      .then((value) => { if (!controller.signal.aborted) setStatus(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível ler o estado do certificado.') })
    return () => controller.abort()
  }, [onDatabase, atCertificateStatus])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Integração</p><h1>Comunicação com a AT</h1><p>O certificado de transporte e o estado da ligação à AT.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><ShieldCheck /></span><h2>Requer ligação ao SQL Server</h2><p>O estado do certificado é lido pela API local.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Integração</p><h1>Comunicação com a AT</h1><p>O certificado de transporte identifica o produtor na ligação SOAP à AT. Este ecrã mostra o estado do certificado configurado — a senha e a chave privada nunca saem do servidor.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

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
      <div className="panel-heading"><div><h2>Onde se configura</h2><p>No servidor, não pela interface.</p></div></div>
      <ul className="config-hints">
        <li>Local e nome do ficheiro: <code>At:Certificate:Directory</code> e <code>At:Certificate:FileName</code> (ou <code>At:Certificate:Path</code> absoluto) no <code>appsettings</code>.</li>
        <li>Senha do certificado: só em <em>user-secrets</em> (<code>At:Certificate:Password</code>) ou na variável de ambiente <code>AT_CERTIFICATE_PASSWORD</code> — nunca em <code>appsettings</code>.</li>
        <li>Ambiente: <code>At:Environment</code> = <code>Testes</code> ou <code>Producao</code>.</li>
        <li>Para os testes, aponta-se para o certificado do PHC.</li>
      </ul>
      <p className="mr-hint">As chamadas SOAP à AT (consulta e-Fatura, submissão de declarações) ficam a aguardar os elementos de teste e a aceitação da adesão pela AT.</p>
    </section>
  </div>
}
