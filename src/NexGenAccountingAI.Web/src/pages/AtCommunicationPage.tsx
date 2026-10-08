import { Database, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { formatMovementDate, type AtCertificateStatus } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'
import { AtCredentialsPanel } from './AtCredentialsPanel'

function formatDate(value: string | null) {
  return value ? formatMovementDate(value.slice(0, 10)) : '—'
}

export function AtCommunicationPage() {
  const { activeCompany, can, source, atCertificateStatus } = useWorkContext()
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
      <section className="panel empty-state"><span className="empty-icon"><ShieldCheck /></span><h2>Requer ligação à base de dados</h2><p>O estado do certificado é lido pela API local.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Integração</p><h1>Comunicação com a AT</h1><p>O certificado de transporte identifica o produtor na ligação SOAP à AT e é comum a todas as empresas e a todos os postos; quem o configura é o produtor do software. O subutilizador e a senha do Portal das Finanças são de cada empresa. As senhas e a chave privada nunca saem do servidor.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    {status && <section className="panel">
      <div className="panel-heading"><div><h2>Certificado de transporte</h2><p>Ambiente do servidor: {status.environment === 'Producao' ? 'Produção' : 'Testes'}. Origem: {status.source === 'geral' ? 'configuração geral' : status.source === 'ficheiro' ? 'ficheiro de configuração do servidor' : 'nenhuma'}.</p></div>
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

    {can('at.credentials') && <AtCredentialsPanel companyId={activeCompany.companyId} companyName={activeCompany.legalName} taxRegistrationNumber={activeCompany.taxRegistrationNumber} />}

    {can('at.credentials') && <p className="mr-hint">As senhas ficam cifradas na base de dados, com uma cifra que qualquer posto desta instalação decifra, e nunca voltam a ser mostradas. O ambiente do servidor (<code>At:Environment</code>) e a versão do Modelo 22 continuam no ficheiro de configuração.</p>}
  </div>
}
