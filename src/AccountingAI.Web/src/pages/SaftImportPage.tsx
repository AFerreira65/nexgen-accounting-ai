import { CircleAlert, CircleCheck, Database, FileUp, Info } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { SaftCompanyCreation, SaftInspection } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

const STANDARD_NAMES: Record<string, string> = {
  SncGeneral: 'SNC — Regime geral',
  SncSmallEntities: 'SNC — Pequenas entidades',
  SncMicroEntities: 'SNC — Microentidades',
  SncNonProfit: 'SNC — Entidades do setor não lucrativo',
  IfrsEu: 'Normas internacionais adotadas pela UE',
}

const REGIONS: { value: 'Mainland' | 'Azores' | 'Madeira'; label: string }[] = [
  { value: 'Mainland', label: 'Continente' },
  { value: 'Azores', label: 'Açores' },
  { value: 'Madeira', label: 'Madeira' },
]

function CompanyProposal({ inspection }: { inspection: SaftInspection }) {
  const { createSaftCompany, source } = useWorkContext()
  const [legalName, setLegalName] = useState(inspection.header.companyName)
  const [taxRegion, setTaxRegion] = useState<'Mainland' | 'Azores' | 'Madeira'>('Mainland')
  const [accountingStandard, setAccountingStandard] = useState('SncGeneral')
  const [baseCurrency, setBaseCurrency] = useState(inspection.header.currencyCode)
  const [status, setStatus] = useState<'idle' | 'creating' | 'error'>('idle')
  const [error, setError] = useState('')
  const [created, setCreated] = useState<SaftCompanyCreation | null>(null)

  useEffect(() => {
    setLegalName(inspection.header.companyName)
    setBaseCurrency(inspection.header.currencyCode)
    setCreated(null)
    setStatus('idle')
    setError('')
  }, [inspection])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('creating')
    setError('')
    try {
      setCreated(await createSaftCompany({
        taxRegistrationNumber: inspection.header.taxRegistrationNumber,
        legalName,
        taxRegion,
        accountingStandard,
        baseCurrency,
      }))
      setStatus('idle')
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível criar a empresa.')
    }
  }

  if (created) {
    return <section className="panel">
      <div className="panel-heading"><div><h2>Empresa criada</h2><p>{created.legalName} · {STANDARD_NAMES[created.accountingStandard] ?? created.accountingStandard}</p></div><span className="success"><CircleCheck /></span></div>
      <div className="settings-grid">
        <div className="metadata-strip"><small>NIF</small><strong>{created.taxRegistrationNumber}</strong></div>
        <div className="metadata-strip"><small>Versão do plano base</small><strong>{created.accountingSetupVersion ?? '—'}</strong></div>
        <div className="metadata-strip"><small>Contas provisionadas</small><strong>{created.accountCount}</strong></div>
        <div className="metadata-strip"><small>Diários</small><strong>{created.journalCount}</strong></div>
        <div className="metadata-strip"><small>Tipos de documento</small><strong>{created.documentTypeCount}</strong></div>
      </div>
      <p className="save-status"><Info size={15} /> A empresa e os cadastros ficam disponíveis no seletor após recarregar. A leitura e a importação dos movimentos do ficheiro ainda não estão disponíveis.</p>
    </section>
  }

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>Propor a criação da empresa</h2><p>Nenhuma empresa acessível tem o NIF <strong>{inspection.header.taxRegistrationNumber}</strong>. Confirme os dados antes de provisionar os cadastros.</p></div><span className="warning"><CircleAlert /></span></div>
    <form className="settings-grid" onSubmit={submit}>
      <label><span>Designação</span><input value={legalName} onChange={(event) => setLegalName(event.target.value)} /></label>
      <label><span>Região fiscal</span><select value={taxRegion} onChange={(event) => setTaxRegion(event.target.value as typeof taxRegion)}>{REGIONS.map((region) => <option key={region.value} value={region.value}>{region.label}</option>)}</select></label>
      <label><span>Norma contabilística</span><select value={accountingStandard} onChange={(event) => setAccountingStandard(event.target.value)}>{Object.entries(STANDARD_NAMES).map(([value, name]) => <option key={value} value={value}>{name}</option>)}</select></label>
      <label><span>Moeda base</span><input value={baseCurrency} onChange={(event) => setBaseCurrency(event.target.value)} maxLength={3} /></label>
      <footer className="settings-actions">
        {source !== 'database' && <span className="save-status">A criação requer ligação ao SQL Server.</span>}
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-primary" type="submit" disabled={source !== 'database' || status === 'creating'}>{status === 'creating' ? 'A criar…' : 'Criar empresa e provisionar cadastros'}</button>
      </footer>
    </form>
  </section>
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
      <div><p className="eyebrow">Importação contabilística</p><h1>Importar SAF-T</h1><p>Leitura do cabeçalho, correspondência da empresa pelo NIF e, quando não existe, criação e provisionamento dos cadastros. A importação dos movimentos ainda não está disponível.</p></div>
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

      {result.match.outcome === 'ExistingCompany'
        ? <section className="panel">
            <div className="panel-heading"><div><h2>Correspondência de empresa</h2><p>Pesquisa pelo NIF do ficheiro entre as empresas a que tem acesso.</p></div><span className="success"><CircleCheck /></span></div>
            <p>Empresa encontrada: <strong>{result.match.legalName}</strong>{result.match.accountingStandard ? ` · ${STANDARD_NAMES[result.match.accountingStandard] ?? result.match.accountingStandard}` : ''}{result.match.accountingSetupVersion ? ` · ${result.match.accountingSetupVersion}` : ''}.</p>
            <p className="save-status"><Info size={15} /> Os próximos passos — confirmar norma e referencial, pré-visualizar e importar os movimentos — ainda não estão disponíveis.</p>
          </section>
        : <CompanyProposal inspection={result} />}
    </>}
  </div>
}
