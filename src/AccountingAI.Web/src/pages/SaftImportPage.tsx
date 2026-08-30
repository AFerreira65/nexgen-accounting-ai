import { CircleAlert, CircleCheck, Database, FileUp, Info } from 'lucide-react'
import { useRef, useState } from 'react'
import type { SaftInspection } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

const STANDARD_NAMES: Record<string, string> = {
  SncGeneral: 'SNC — Regime geral',
  SncSmallEntities: 'SNC — Pequenas entidades',
  SncMicroEntities: 'SNC — Microentidades',
  SncNonProfit: 'SNC — Entidades do setor não lucrativo',
  IfrsEu: 'Normas internacionais adotadas pela UE',
}

export function SaftImportPage() {
  const { inspectSaft, source } = useWorkContext()
  const inputRef = useRef<HTMLInputElement>(null)
  const [state, setState] = useState<'idle' | 'reading' | 'done' | 'error'>('idle')
  const [fileName, setFileName] = useState('')
  const [result, setResult] = useState<SaftInspection | null>(null)
  const [error, setError] = useState('')

  async function onFile(file: File | undefined) {
    if (!file) return
    setFileName(file.name)
    setState('reading')
    setError('')
    setResult(null)
    try {
      setResult(await inspectSaft(file))
      setState('done')
    } catch (readError) {
      setState('error')
      setError(readError instanceof Error ? readError.message : 'Não foi possível ler o ficheiro SAF-T.')
    }
  }

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Importação contabilística</p><h1>Importar SAF-T</h1><p>Leitura do cabeçalho e correspondência da empresa pelo NIF. A criação e a importação ainda não estão disponíveis.</p></div>
      <span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Ligado ao SQL Server' : 'Modo de demonstração'}</span>
    </header>

    <section className="panel">
      <div className="panel-heading"><div><h2>Ficheiro SAF-T (PT)</h2><p>Aceita ficheiros de faturação, contabilidade ou integrados. Só o cabeçalho é lido.</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <input ref={inputRef} type="file" accept=".xml,text/xml,application/xml" className="sr-only" aria-label="Escolher ficheiro SAF-T" onChange={(event) => void onFile(event.target.files?.[0])} />
        <button className="button button-primary" type="button" onClick={() => inputRef.current?.click()}><FileUp size={16} /> Escolher ficheiro</button>
        <span className="catalog-count">{fileName || 'Nenhum ficheiro escolhido'}</span>
      </div>
      {state === 'reading' && <p className="save-status">A ler o cabeçalho…</p>}
      {state === 'error' && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    </section>

    {result && <>
      <section className="panel">
        <div className="panel-heading"><div><h2>Cabeçalho do ficheiro</h2><p>{result.header.companyName}{result.header.businessName ? ` · ${result.header.businessName}` : ''}</p></div></div>
        <div className="settings-grid">
          <div className="metadata-strip"><small>NIF</small><strong>{result.header.taxRegistrationNumber}</strong></div>
          <div className="metadata-strip"><small>Versão SAF-T</small><strong>{result.header.auditFileVersion}</strong></div>
          <div className="metadata-strip"><small>Tipo de ficheiro</small><strong>{result.header.taxAccountingBasis}{result.header.includesAccounting ? ' · com contabilidade' : ' · sem contabilidade'}</strong></div>
          <div className="metadata-strip"><small>Exercício</small><strong>{result.header.fiscalYear}</strong></div>
          <div className="metadata-strip"><small>Período</small><strong>{result.header.startDate} a {result.header.endDate}</strong></div>
          <div className="metadata-strip"><small>Moeda</small><strong>{result.header.currencyCode}</strong></div>
        </div>
      </section>

      <section className="panel">
        <div className="panel-heading">
          <div><h2>Correspondência de empresa</h2><p>Pesquisa pelo NIF do ficheiro entre as empresas a que tem acesso.</p></div>
          <span className={result.match.outcome === 'ExistingCompany' ? 'success' : 'warning'}>{result.match.outcome === 'ExistingCompany' ? <CircleCheck /> : <CircleAlert />}</span>
        </div>
        {result.match.outcome === 'ExistingCompany'
          ? <p>Empresa encontrada: <strong>{result.match.legalName}</strong>{result.match.accountingStandard ? ` · ${STANDARD_NAMES[result.match.accountingStandard] ?? result.match.accountingStandard}` : ''}{result.match.accountingSetupVersion ? ` · ${result.match.accountingSetupVersion}` : ''}.</p>
          : <p>Nenhuma empresa acessível tem o NIF <strong>{result.header.taxRegistrationNumber}</strong>. O passo seguinte proporá a criação da empresa.</p>}
        <p className="save-status"><Info size={15} /> Os próximos passos — confirmar norma e referencial, provisionar os cadastros, pré-visualizar e importar — ainda não estão disponíveis. Um SAF-T real nunca cria uma empresa em silêncio.</p>
      </section>
    </>}
  </div>
}
