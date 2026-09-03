import { CircleAlert, CircleCheck, Database, FileUp, Info } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ACCOUNTING_STANDARD_NAMES, accountingStandardName } from '../app/accountingStandards'
import { euro, type SaftAccountsImport, type SaftAccountsPreview, type SaftCompanyCreation, type SaftEntriesPreview, type SaftInspection } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

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
      <div className="panel-heading"><div><h2>Empresa criada</h2><p>{created.legalName} · {accountingStandardName(created.accountingStandard)}</p></div><span className="success"><CircleCheck /></span></div>
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
      <label><span>Norma contabilística</span><select value={accountingStandard} onChange={(event) => setAccountingStandard(event.target.value)}>{Object.entries(ACCOUNTING_STANDARD_NAMES).map(([value, name]) => <option key={value} value={value}>{name}</option>)}</select></label>
      <label><span>Moeda base</span><input value={baseCurrency} onChange={(event) => setBaseCurrency(event.target.value)} maxLength={3} /></label>
      <footer className="settings-actions">
        {source !== 'database' && <span className="save-status">A criação requer ligação ao SQL Server.</span>}
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-primary" type="submit" disabled={source !== 'database' || status === 'creating'}>{status === 'creating' ? 'A criar…' : 'Criar empresa e provisionar cadastros'}</button>
      </footer>
    </form>
  </section>
}

function AccountsPreviewPanel({ companyId, file }: { companyId: string; file: File | null }) {
  const { previewSaftAccounts, importSaftAccounts, source } = useWorkContext()
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [preview, setPreview] = useState<SaftAccountsPreview | null>(null)
  const [error, setError] = useState('')
  const [importState, setImportState] = useState<'idle' | 'importing' | 'done'>('idle')
  const [imported, setImported] = useState<SaftAccountsImport | null>(null)

  useEffect(() => { setState('idle'); setPreview(null); setError(''); setImportState('idle'); setImported(null) }, [companyId, file])

  async function run() {
    if (!file) return
    setState('loading'); setError('')
    try {
      setPreview(await previewSaftAccounts(companyId, file))
      setState('done')
    } catch (previewError) {
      setState('error')
      setError(previewError instanceof Error ? previewError.message : 'Não foi possível ler o plano de contas.')
    }
  }

  async function importNew() {
    if (!file) return
    setImportState('importing'); setError('')
    try {
      const result = await importSaftAccounts(companyId, file)
      setImported(result)
      setImportState('done')
      setPreview(await previewSaftAccounts(companyId, file))
    } catch (importError) {
      setImportState('idle')
      setState('error')
      setError(importError instanceof Error ? importError.message : 'Não foi possível criar as contas.')
    }
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Plano de contas do ficheiro</h2><p>Compara os códigos do <code>GeneralLedgerAccounts</code> com o plano da empresa.</p></div></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={!file || state === 'loading'} onClick={() => void run()}>{state === 'loading' ? 'A ler…' : 'Pré-visualizar plano de contas'}</button>
      {preview && preview.newCount > 0 && source === 'database' && <button className="button button-primary" type="button" disabled={importState === 'importing'} onClick={() => void importNew()}>{importState === 'importing' ? 'A criar…' : `Criar as ${preview.newCount} contas novas`}</button>}
    </div>
    {imported && <p className="save-status success"><CircleCheck size={15} /> {imported.createdCount} contas criadas ({imported.alreadyExistedCount} já existiam).</p>}
    {state === 'error' && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {preview && <>
      <div className="settings-grid">
        <div className="metadata-strip"><small>Contas no ficheiro</small><strong>{preview.fileAccountCount}</strong></div>
        <div className="metadata-strip"><small>Já existentes</small><strong>{preview.existingCount}</strong></div>
        <div className="metadata-strip"><small>Novas</small><strong>{preview.newCount}</strong></div>
        <div className="metadata-strip"><small>Contas de movimento da empresa</small><strong>{preview.companyPostingAccountCount}</strong></div>
      </div>
      {preview.fileAccountCount === 0
        ? <p className="save-status"><Info size={15} /> O ficheiro não tem plano geral de contas.</p>
        : <div className="table-wrap"><table><thead><tr><th>Código</th><th>Descrição</th><th>Situação</th></tr></thead><tbody>
            {preview.accounts.map((account) => <tr key={account.code}>
              <td className="primary-cell">{account.code}</td>
              <td>{account.description || '—'}</td>
              <td><span className={`badge ${account.state === 'Existing' ? 'success' : 'warning'}`}>{account.state === 'Existing' ? 'Já existe' : 'Nova'}</span></td>
            </tr>)}
          </tbody></table></div>}
    </>}
  </section>
}

function EntriesPreviewPanel({ companyId, file }: { companyId: string; file: File | null }) {
  const { previewSaftEntries } = useWorkContext()
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [preview, setPreview] = useState<SaftEntriesPreview | null>(null)
  const [error, setError] = useState('')

  useEffect(() => { setState('idle'); setPreview(null); setError('') }, [companyId, file])

  async function run() {
    if (!file) return
    setState('loading'); setError('')
    try {
      setPreview(await previewSaftEntries(companyId, file))
      setState('done')
    } catch (previewError) {
      setState('error')
      setError(previewError instanceof Error ? previewError.message : 'Não foi possível ler os movimentos.')
    }
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Movimentos do ficheiro</h2><p>Resumo do bloco <code>GeneralLedgerEntries</code> — leitura em streaming, sem carregar as linhas. A importação dos movimentos ainda não está disponível.</p></div></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={!file || state === 'loading'} onClick={() => void run()}>{state === 'loading' ? 'A ler…' : 'Pré-visualizar movimentos'}</button>
    </div>
    {state === 'error' && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {preview && (preview.hasEntries
      ? <>
          <div className="settings-grid">
            <div className="metadata-strip"><small>Transações</small><strong>{preview.transactionCount}{preview.declaredEntryCount !== null && preview.declaredEntryCount !== preview.transactionCount ? ` (declaradas ${preview.declaredEntryCount})` : ''}</strong></div>
            <div className="metadata-strip"><small>Total de débito</small><strong>{preview.totalDebit !== null ? euro.format(preview.totalDebit) : '—'}</strong></div>
            <div className="metadata-strip"><small>Total de crédito</small><strong>{preview.totalCredit !== null ? euro.format(preview.totalCredit) : '—'}</strong></div>
            <div className="metadata-strip"><small>Período</small><strong>{preview.fromDate ?? '—'} a {preview.toDate ?? '—'}</strong></div>
          </div>
          <p className={`save-status ${preview.balanced ? 'success' : 'error'}`}>{preview.balanced ? <CircleCheck size={15} /> : <CircleAlert size={15} />} {preview.balanced ? 'Débito e crédito declarados equilibrados.' : 'Débito e crédito declarados diferentes.'}</p>
          <div className="table-wrap"><table><thead><tr><th>Diário</th><th>Descrição</th><th className="numeric">Transações</th><th>Situação</th></tr></thead><tbody>
            {preview.journals.map((journal) => <tr key={journal.code}>
              <td className="primary-cell">{journal.code}</td>
              <td>{journal.description || '—'}</td>
              <td className="numeric">{journal.transactionCount}</td>
              <td><span className={`badge ${journal.exists ? 'success' : 'warning'}`}>{journal.exists ? 'Já existe' : 'Novo'}</span></td>
            </tr>)}
          </tbody></table></div>
        </>
      : <p className="save-status"><Info size={15} /> O ficheiro não tem movimentos (bloco <code>GeneralLedgerEntries</code>).</p>)}
  </section>
}

export function SaftImportPage() {
  const { inspectSaft, source } = useWorkContext()
  const inputRef = useRef<HTMLInputElement>(null)
  const [state, setState] = useState<'idle' | 'reading' | 'done' | 'error'>('idle')
  const [fileName, setFileName] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<SaftInspection | null>(null)
  const [error, setError] = useState('')

  async function onFile(picked: File | undefined) {
    if (!picked) return
    setFileName(picked.name)
    setFile(picked)
    setState('reading')
    setError('')
    setResult(null)
    try {
      setResult(await inspectSaft(picked))
      setState('done')
    } catch (readError) {
      setState('error')
      setError(readError instanceof Error ? readError.message : 'Não foi possível ler o ficheiro SAF-T.')
    }
  }

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Importação contabilística</p><h1>Importar SAF-T</h1><p>Leitura do cabeçalho, correspondência da empresa pelo NIF, criação e provisionamento quando não existe, e pré-visualização do plano de contas do ficheiro. A importação dos movimentos ainda não está disponível.</p></div>
      <span className={`connection-badge ${source === 'database' ? 'database' : 'demonstration'}`}><Database size={15} />{source === 'database' ? 'Ligado ao SQL Server' : 'Modo de demonstração'}</span>
    </header>

    <section className="panel">
      <div className="panel-heading"><div><h2>Ficheiro SAF-T (PT)</h2><p>Aceita ficheiros de faturação, contabilidade ou integrados. São lidos o cabeçalho e o plano de contas.</p></div></div>
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
        ? <>
            <section className="panel">
              <div className="panel-heading"><div><h2>Correspondência de empresa</h2><p>Pesquisa pelo NIF do ficheiro entre as empresas a que tem acesso.</p></div><span className="success"><CircleCheck /></span></div>
              <p>Empresa encontrada: <strong>{result.match.legalName}</strong>{result.match.accountingStandard ? ` · ${accountingStandardName(result.match.accountingStandard)}` : ''}{result.match.accountingSetupVersion ? ` · ${result.match.accountingSetupVersion}` : ''}.</p>
              <p className="save-status"><Info size={15} /> A importação dos movimentos do ficheiro ainda não está disponível.</p>
            </section>
            {result.match.companyId && <AccountsPreviewPanel companyId={result.match.companyId} file={file} />}
            {result.match.companyId && source === 'database' && <EntriesPreviewPanel companyId={result.match.companyId} file={file} />}
          </>
        : <CompanyProposal inspection={result} />}
    </>}
  </div>
}
