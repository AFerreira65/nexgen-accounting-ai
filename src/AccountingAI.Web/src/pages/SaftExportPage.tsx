import { CircleAlert, CircleCheck, Database, Download, FileDown, Info, Search } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, type SaftExportReport } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

export function SaftExportPage() {
  const { activeCompany, source, saftExportReport, downloadSaftExport } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const [year, setYear] = useState(activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear())
  const [report, setReport] = useState<SaftExportReport | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  usePublishRecord(undefined, undefined)

  const analyse = async () => {
    setBusy(true); setError(''); setStatus('')
    try {
      setReport(await saftExportReport(companyId, year))
    } catch (problem) {
      setReport(null)
      setError(problem instanceof Error ? problem.message : 'Não foi possível analisar o exercício.')
    } finally {
      setBusy(false)
    }
  }

  const download = async () => {
    setBusy(true); setError(''); setStatus('')
    try {
      const result = await downloadSaftExport(companyId, year)
      if ('report' in result) {
        // O exercício mudou desde a análise e passou a ter problemas bloqueantes.
        setReport(result.report)
        setError('O ficheiro não foi gerado: há problemas a resolver primeiro.')
        return
      }
      const url = URL.createObjectURL(result.file)
      const link = document.createElement('a')
      link.href = url
      link.download = `SAFT-PT-contabilidade-${activeCompany.taxRegistrationNumber}-${year}.xml`
      link.click()
      URL.revokeObjectURL(url)
      setStatus('Ficheiro gerado. Confira-o no validador da AT antes de o entregar.')
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o ficheiro.')
    } finally {
      setBusy(false)
    }
  }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Exportar SAF-T</h1><p>O ficheiro SAF-T (PT) de contabilidade de um exercício.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><FileDown /></span><h2>Requer ligação ao SQL Server</h2><p>O ficheiro é gerado a partir da base de dados da empresa.</p></section>
    </div>
  }

  const blocking = report?.issues.filter((issue) => issue.blocking) ?? []
  const warnings = report?.issues.filter((issue) => !issue.blocking) ?? []
  const canExport = report !== null && report.fiscalYear === year && blocking.length === 0

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Dados</p><h1>Exportar SAF-T</h1><p>Gera o ficheiro SAF-T (PT) de contabilidade de um exercício, na versão 1.04_01: plano de contas com saldos e taxonomias, terceiros e movimentos por diário.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    <p className="mr-hint"><Info size={13} /> A aplicação confere a estrutura do ficheiro, mas não substitui o validador oficial: valide-o no Portal das Finanças antes de o entregar a quem quer que seja.</p>

    {error && <p className="save-status error">{error}</p>}
    {status && !error && <p className="save-status success">{status}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min="2000" max="2100" value={year} onChange={(event) => { setYear(Number(event.target.value) || year); setStatus('') }} /></label>
      <button className="button button-secondary" type="button" disabled={busy} onClick={() => void analyse()}><Search size={16} /> {busy ? 'A analisar…' : 'Analisar o exercício'}</button>
      <button className="button button-primary" type="button" disabled={busy || !canExport} onClick={() => void download()}><Download size={16} /> Gerar o ficheiro</button>
    </div>

    {report && <>
      <section className="metadata-strip">
        <span><small>Contas</small><strong>{report.accountCount}</strong></span>
        <span><small>Terceiros</small><strong>{report.partyCount}</strong></span>
        <span><small>Lançamentos</small><strong>{report.transactionCount}</strong></span>
        <span><small>Débito = crédito</small><strong>{euro.format(report.totalDebit)}</strong></span>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>{blocking.length === 0 ? 'O exercício pode ser exportado' : 'Há problemas a resolver primeiro'}</h2><p>{blocking.length === 0 ? `Exercício de ${report.fiscalYear}.` : 'Com estes problemas o ficheiro ficaria inválido, por isso não é gerado.'}</p></div></div>
        {blocking.length > 0 && <ul className="issue-list">{blocking.map((issue) => <li key={issue.message} className="error"><CircleAlert size={15} /> {issue.message}{issue.count > 1 && <strong> ({issue.count})</strong>}</li>)}</ul>}
        {warnings.length > 0 && <ul className="issue-list">{warnings.map((issue) => <li key={issue.message}><Info size={15} /> {issue.message}{issue.count > 1 && <strong> ({issue.count})</strong>}</li>)}</ul>}
        {blocking.length === 0 && warnings.length === 0 && <p className="save-status success"><CircleCheck size={15} /> Sem observações.</p>}
        {blocking.some((issue) => issue.message.includes('taxonomia')) && <p className="mr-hint">Atribua as taxonomias em falta em <Link to="/taxonomias">Taxonomias</Link>.</p>}
      </section>
    </>}
  </div>
}
