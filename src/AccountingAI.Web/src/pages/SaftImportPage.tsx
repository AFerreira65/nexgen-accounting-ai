import { Link } from 'react-router-dom'
import { CircleAlert, CircleCheck, Database, Download, FileUp, Info, ShieldCheck } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ACCOUNTING_STANDARD_NAMES, accountingStandardName } from '../app/accountingStandards'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type SaftAccountsImport, type SaftAccountsPreview, type SaftCompanyCreation, type SaftEntriesAnalysis, type SaftEntriesImport, type SaftEntriesPreview, type SaftInspection, type SaftOpeningBalancesImport, type SaftValidation, type SaftValidationRunSummary } from '../app/demo-data'
import { issuesBySeverity, SAFT_SEVERITY_LABELS, saftScopeLabel, saftValidationBlocksImport } from '../app/saftValidation'
import { useWorkContext } from '../app/WorkContext'

const REGIONS: { value: 'Mainland' | 'Azores' | 'Madeira'; label: string }[] = [
  { value: 'Mainland', label: 'Continente' },
  { value: 'Azores', label: 'Açores' },
  { value: 'Madeira', label: 'Madeira' },
]

function CompanyProposal({ inspection, onCreated }: { inspection: SaftInspection; onCreated: (companyId: string) => void }) {
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
      const result = await createSaftCompany({
        taxRegistrationNumber: inspection.header.taxRegistrationNumber,
        legalName,
        taxRegion,
        accountingStandard,
        baseCurrency,
        fiscalYear: inspection.header.fiscalYear,
      })
      setCreated(result)
      setStatus('idle')
      onCreated(result.companyId)
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
        <div className="metadata-strip"><small>Exercício</small><strong>{inspection.header.fiscalYear}</strong></div>
        <div className="metadata-strip"><small>Versão do plano base</small><strong>{created.accountingSetupVersion ?? '—'}</strong></div>
        <div className="metadata-strip"><small>Contas provisionadas</small><strong>{created.accountCount}</strong></div>
        <div className="metadata-strip"><small>Diários</small><strong>{created.journalCount}</strong></div>
        <div className="metadata-strip"><small>Tipos de documento</small><strong>{created.documentTypeCount}</strong></div>
      </div>
      <p className="save-status"><Info size={15} /> Empresa no seletor e selecionada. A abrir o plano de contas e os movimentos do ficheiro para importar…</p>
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

function SaftValidationPanel({ file, companyId, companyName, validation, onResult }: {
  file: File | null
  companyId: string | null
  companyName: string
  validation: SaftValidation | null
  onResult: (validation: SaftValidation | null) => void
}) {
  const { validateSaft, saftValidationReports } = useWorkContext()
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  const [error, setError] = useState('')
  const [history, setHistory] = useState<SaftValidationRunSummary[]>([])

  const loadHistory = useCallback(() => {
    if (!companyId) { setHistory([]); return }
    void saftValidationReports(companyId).then(setHistory).catch(() => setHistory([]))
  }, [companyId, saftValidationReports])

  useEffect(() => { setState('idle'); setError(''); onResult(null) }, [file, onResult])
  useEffect(() => { loadHistory() }, [loadHistory])

  async function run() {
    if (!file) return
    setState('loading'); setError('')
    try {
      onResult(await validateSaft(file, companyId))
      setState('idle')
      loadHistory()
    } catch (runError) {
      setState('error')
      setError(runError instanceof Error ? runError.message : 'Não foi possível validar o ficheiro.')
    }
  }

  const groups = validation ? issuesBySeverity(validation) : []
  const blocks = saftValidationBlocksImport(validation)

  function exportCsv() {
    if (!validation) return
    const header = ['Severidade', 'Âmbito', 'Regra', 'Linha', 'Coluna', 'Mensagem']
    const rows = validation.issues.map((issue) => [
      SAFT_SEVERITY_LABELS[issue.severity], saftScopeLabel(issue.scope), issue.ruleId ?? 'XSD',
      issue.line ?? '', issue.position ?? '', issue.message,
    ])
    downloadCsv(csvFilename('validacao-saft', companyName), toCsv([header, ...rows]))
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Validação do ficheiro</h2><p>Conformidade com o esquema oficial da AT e verificações analíticas (contagens, totais, equilíbrio, contas, datas). É um apoio; não substitui a validação oficial.</p></div><span className={validation ? (blocks ? 'warning' : 'success') : ''}><ShieldCheck /></span></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={!file || state === 'loading'} onClick={() => void run()}>{state === 'loading' ? 'A validar…' : 'Validar ficheiro'}</button>
      {validation && validation.issues.length > 0 && <button className="button button-secondary" type="button" onClick={exportCsv}><Download size={15} /> Exportar relatório</button>}
    </div>
    {state === 'error' && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {validation && <>
      <div className="settings-grid">
        <div className="metadata-strip"><small>Versão SAF-T</small><strong>{validation.version ?? '—'}</strong></div>
        <div className="metadata-strip"><small>Esquema oficial</small><strong>{validation.schemaAvailable ? (validation.schemaValid ? 'Conforme' : 'Não conforme') : 'Não carregado'}</strong></div>
        <div className="metadata-strip"><small>Erros</small><strong>{validation.counts.error}</strong></div>
        <div className="metadata-strip"><small>Avisos</small><strong>{validation.counts.warning}</strong></div>
      </div>
      <p className={`save-status ${blocks ? 'error' : 'success'}`}>{blocks ? <CircleAlert size={15} /> : <CircleCheck size={15} />} {blocks
        ? `${validation.counts.error} ${validation.counts.error === 1 ? 'erro impede' : 'erros impedem'} a importação. Reveja o ficheiro ou importe mesmo assim (abaixo).`
        : validation.counts.warning > 0
          ? 'Sem erros. Há avisos a rever, mas a importação pode seguir.'
          : 'Sem erros nem avisos.'}</p>
      {validation.truncated && <p className="save-status"><Info size={15} /> Há mais ocorrências do que as listadas.</p>}
      {groups.map((group) => <div className="table-wrap" key={group.severity}>
        <table><thead><tr><th>{SAFT_SEVERITY_LABELS[group.severity]}</th><th>Âmbito</th><th>Regra</th><th>Local</th><th>Mensagem</th></tr></thead><tbody>
          {group.issues.map((issue, index) => <tr key={`${group.severity}-${index}`}>
            <td><span className={`badge ${group.severity === 'Error' ? 'warning' : group.severity === 'Warning' ? 'neutral' : 'success'}`}>{SAFT_SEVERITY_LABELS[group.severity]}</span></td>
            <td>{saftScopeLabel(issue.scope)}</td>
            <td>{issue.ruleId ?? 'XSD'}</td>
            <td><small>{issue.line ? `linha ${issue.line}${issue.position ? `, col. ${issue.position}` : ''}` : '—'}</small></td>
            <td>{issue.message}</td>
          </tr>)}
        </tbody></table>
      </div>)}
    </>}
    {history.length > 0 && <>
      <h3>Validações anteriores</h3>
      <div className="table-wrap"><table><thead><tr><th>Ficheiro</th><th>Data</th><th className="numeric">Erros</th><th className="numeric">Avisos</th><th>Esquema</th></tr></thead><tbody>
        {history.map((run) => <tr key={run.id}>
          <td className="primary-cell">{run.fileName}</td>
          <td><small>{new Date(run.createdAtUtc).toLocaleString('pt-PT')}</small></td>
          <td className="numeric strong">{run.counts.error}</td>
          <td className="numeric">{run.counts.warning}</td>
          <td><small>{run.schemaAvailable ? (run.schemaValid ? 'conforme' : 'não conforme') : 'não carregado'}</small></td>
        </tr>)}
      </tbody></table></div>
    </>}
  </section>
}

function AccountsPreviewPanel({ companyId, file, fiscalYear, importBlocked }: { companyId: string; file: File | null; fiscalYear: number; importBlocked: boolean }) {
  const { previewSaftAccounts, importSaftAccounts, importSaftOpeningBalances, source } = useWorkContext()
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [preview, setPreview] = useState<SaftAccountsPreview | null>(null)
  const [error, setError] = useState('')
  const [importState, setImportState] = useState<'idle' | 'importing' | 'done'>('idle')
  const [imported, setImported] = useState<SaftAccountsImport | null>(null)
  const [openingState, setOpeningState] = useState<'idle' | 'importing'>('idle')
  const [opening, setOpening] = useState<SaftOpeningBalancesImport | null>(null)

  useEffect(() => { setState('idle'); setPreview(null); setError(''); setImportState('idle'); setImported(null); setOpeningState('idle'); setOpening(null) }, [companyId, file])

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
      const result = await importSaftAccounts(companyId, file, fiscalYear)
      setImported(result)
      setImportState('done')
      setPreview(await previewSaftAccounts(companyId, file))
    } catch (importError) {
      setImportState('idle')
      setState('error')
      setError(importError instanceof Error ? importError.message : 'Não foi possível criar as contas.')
    }
  }

  async function importOpening() {
    if (!file) return
    setOpeningState('importing'); setError('')
    try {
      setOpening(await importSaftOpeningBalances(companyId, fiscalYear, file))
    } catch (openingError) {
      setState('error')
      setError(openingError instanceof Error ? openingError.message : 'Não foi possível importar os saldos iniciais.')
    } finally {
      setOpeningState('idle')
    }
  }

  return <section className="panel">
    <div className="panel-heading"><div><h2>Plano de contas do ficheiro</h2><p>Compara os códigos do <code>GeneralLedgerAccounts</code> com o plano da empresa. Os saldos de abertura das contas de movimento podem entrar como um lançamento de abertura ({fiscalYear}).</p></div></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={!file || state === 'loading'} onClick={() => void run()}>{state === 'loading' ? 'A ler…' : 'Pré-visualizar plano de contas'}</button>
      {preview && preview.newCount > 0 && source === 'database' && <button className="button button-primary" type="button" disabled={importState === 'importing' || importBlocked} onClick={() => void importNew()}>{importState === 'importing' ? 'A criar…' : `Criar as ${preview.newCount} contas novas`}</button>}
      {preview && preview.fileAccountCount > 0 && source === 'database' && <button className="button button-secondary" type="button" disabled={openingState === 'importing' || importBlocked} onClick={() => void importOpening()}>{openingState === 'importing' ? 'A importar…' : 'Importar saldos iniciais'}</button>}
    </div>
    {imported && <p className="save-status success"><CircleCheck size={15} /> {imported.createdCount} contas criadas ({imported.alreadyExistedCount} já existiam); exercício de {fiscalYear} definido como o seu exercício nesta empresa.</p>}
    {opening && <p className={`save-status ${opening.alreadyImported ? 'warning' : opening.lineCount > 0 ? 'success' : ''}`}>{opening.alreadyImported ? <Info size={15} /> : opening.lineCount > 0 ? <CircleCheck size={15} /> : <Info size={15} />} {opening.alreadyImported
      ? `Já existe abertura para ${fiscalYear}${opening.entryReference ? ` (${opening.entryReference})` : ''}; os saldos iniciais não foram reimportados.`
      : opening.lineCount > 0
        ? `Abertura ${opening.entryReference ?? ''} criada com ${opening.lineCount} linhas (${euro.format(opening.totalDebit)}).`
        : 'O ficheiro não tem saldos de abertura em contas de movimento da empresa.'}</p>}
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

function EntriesPreviewPanel({ companyId, file, fiscalYear, importBlocked }: { companyId: string; file: File | null; fiscalYear: number; importBlocked: boolean }) {
  const { previewSaftEntries, analyzeSaftEntries, importSaftEntries, source } = useWorkContext()
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [preview, setPreview] = useState<SaftEntriesPreview | null>(null)
  const [error, setError] = useState('')
  const [analyzeState, setAnalyzeState] = useState<'idle' | 'analyzing'>('idle')
  const [analysis, setAnalysis] = useState<SaftEntriesAnalysis | null>(null)
  const [importState, setImportState] = useState<'idle' | 'importing' | 'done'>('idle')
  const [imported, setImported] = useState<SaftEntriesImport | null>(null)

  useEffect(() => { setState('idle'); setPreview(null); setError(''); setAnalyzeState('idle'); setAnalysis(null); setImportState('idle'); setImported(null) }, [companyId, file])

  async function run() {
    if (!file) return
    setState('loading'); setError('')
    try {
      setPreview(await previewSaftEntries(companyId, file))
      setAnalysis(null); setImported(null)
      setState('done')
    } catch (previewError) {
      setState('error')
      setError(previewError instanceof Error ? previewError.message : 'Não foi possível ler os movimentos.')
    }
  }

  async function analyzeEntries() {
    if (!file) return
    setAnalyzeState('analyzing'); setError('')
    try {
      setAnalysis(await analyzeSaftEntries(companyId, file))
    } catch (analyzeError) {
      setState('error')
      setError(analyzeError instanceof Error ? analyzeError.message : 'Não foi possível analisar os movimentos.')
    } finally {
      setAnalyzeState('idle')
    }
  }

  async function importEntries() {
    if (!file) return
    setImportState('importing'); setError('')
    try {
      const result = await importSaftEntries(companyId, file, fiscalYear)
      setImported(result)
      setAnalysis(null)
      setImportState('done')
      setPreview(await previewSaftEntries(companyId, file))
    } catch (importError) {
      setImportState('idle')
      setState('error')
      setError(importError instanceof Error ? importError.message : 'Não foi possível importar os movimentos.')
    }
  }

  const skippedRows = imported?.skipped ?? analysis?.skipped ?? []
  // Um ficheiro grande pode ter dezenas de milhares de transações saltadas
  // (ex.: analisar antes de importar o plano de contas); acima de um limite,
  // agrupa-se por motivo com a contagem e alguns exemplos em vez de as
  // desenhar todas.
  const SKIPPED_DETAIL_LIMIT = 50
  const skippedByReason = useMemo(() => {
    if (skippedRows.length <= SKIPPED_DETAIL_LIMIT) return null
    const map = new Map<string, { count: number; sample: string[] }>()
    for (const entry of skippedRows) {
      const group = map.get(entry.reason) ?? { count: 0, sample: [] }
      group.count += 1
      if (group.sample.length < 5) group.sample.push(entry.transactionId)
      map.set(entry.reason, group)
    }
    return [...map.entries()].sort((a, b) => b[1].count - a[1].count)
  }, [skippedRows])

  return <section className="panel">
    <div className="panel-heading"><div><h2>Movimentos do ficheiro</h2><p>Resumo do bloco <code>GeneralLedgerEntries</code> — leitura em streaming. A importação usa o <code>TransactionID</code> do ficheiro como referência, cria os diários em falta e não duplica ao reimportar.</p></div></div>
    <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
      <button className="button button-secondary" type="button" disabled={!file || state === 'loading'} onClick={() => void run()}>{state === 'loading' ? 'A ler…' : 'Pré-visualizar movimentos'}</button>
      {preview && preview.hasEntries && preview.transactionCount > 0 && source === 'database' && <>
        <button className="button button-secondary" type="button" disabled={analyzeState === 'analyzing' || importState === 'importing'} onClick={() => void analyzeEntries()}>{analyzeState === 'analyzing' ? 'A analisar…' : 'Analisar importação'}</button>
        <button className="button button-primary" type="button" disabled={importState === 'importing' || importBlocked} onClick={() => void importEntries()}>{importState === 'importing' ? 'A importar…' : `Importar os ${preview.transactionCount} movimentos`}</button>
      </>}
    </div>
    {analysis && !imported && <p className={`save-status ${analysis.wouldSkipCount > 0 ? 'warning' : 'success'}`}>{analysis.wouldSkipCount > 0 ? <CircleAlert size={15} /> : <CircleCheck size={15} />} Seriam importados {analysis.wouldImportCount} {analysis.wouldImportCount === 1 ? 'movimento' : 'movimentos'}{analysis.journalsToCreate.length > 0 ? `, ${analysis.journalsToCreate.length} ${analysis.journalsToCreate.length === 1 ? 'diário a criar' : 'diários a criar'} (${analysis.journalsToCreate.join(', ')})` : ''}{analysis.wouldSkipCount > 0 ? `, ${analysis.wouldSkipCount} ${analysis.wouldSkipCount === 1 ? 'saltado' : 'saltados'}` : ''}.</p>}
    {analysis && !imported && analysis.wouldMatchDeclaredTotals !== null && <p className={`save-status ${analysis.wouldMatchDeclaredTotals ? 'success' : 'warning'}`}>{analysis.wouldMatchDeclaredTotals ? <CircleCheck size={15} /> : <CircleAlert size={15} />} {analysis.wouldMatchDeclaredTotals
      ? `Depois de importar, os totais da empresa no período do ficheiro (${euro.format(analysis.projectedPeriodDebit)}) coincidiriam com os declarados no SAF-T.`
      : `Depois de importar, os totais da empresa no período do ficheiro (débito ${euro.format(analysis.projectedPeriodDebit)}, crédito ${euro.format(analysis.projectedPeriodCredit)}) não coincidiriam com os declarados no SAF-T (débito ${euro.format(analysis.declaredTotalDebit ?? 0)}, crédito ${euro.format(analysis.declaredTotalCredit ?? 0)}).`}</p>}
    {imported && <p className={`save-status ${imported.skippedCount > 0 ? 'warning' : 'success'}`}>{imported.skippedCount > 0 ? <CircleAlert size={15} /> : <CircleCheck size={15} />} {imported.importedCount} {imported.importedCount === 1 ? 'movimento importado' : 'movimentos importados'}{imported.journalsCreated.length > 0 ? `, ${imported.journalsCreated.length} ${imported.journalsCreated.length === 1 ? 'diário criado' : 'diários criados'} (${imported.journalsCreated.join(', ')})` : ''}{imported.skippedCount > 0 ? `, ${imported.skippedCount} ${imported.skippedCount === 1 ? 'saltado' : 'saltados'}` : ''}; exercício de {fiscalYear} definido como o seu exercício nesta empresa.</p>}
    {imported && ((imported.linesWithParty ?? 0) > 0 || (imported.transactionsWithUnknownParty ?? 0) > 0) && <p className={`save-status ${(imported.transactionsWithUnknownParty ?? 0) > 0 ? 'warning' : 'success'}`}>{(imported.transactionsWithUnknownParty ?? 0) > 0 ? <CircleAlert size={15} /> : <CircleCheck size={15} />} {imported.linesWithParty ?? 0} {(imported.linesWithParty ?? 0) === 1 ? 'linha ficou' : 'linhas ficaram'} com terceiro.{(imported.transactionsWithUnknownParty ?? 0) > 0 && <> {imported.transactionsWithUnknownParty} {imported.transactionsWithUnknownParty === 1 ? 'transação refere' : 'transações referem'} um terceiro que ainda não existe na empresa: para os ligar, importe os terceiros em <Link to="/terceiros">Terceiros</Link> antes dos movimentos.</>}</p>}
    {imported && imported.matchesDeclaredTotals !== null && <p className={`save-status ${imported.matchesDeclaredTotals ? 'success' : 'warning'}`}>{imported.matchesDeclaredTotals ? <CircleCheck size={15} /> : <CircleAlert size={15} />} {imported.matchesDeclaredTotals
      ? `Os totais da empresa no período do ficheiro (${euro.format(imported.companyPeriodDebit)}) coincidem com os declarados no SAF-T.`
      : `Os totais da empresa no período do ficheiro (débito ${euro.format(imported.companyPeriodDebit)}, crédito ${euro.format(imported.companyPeriodCredit)}) não coincidem com os declarados no SAF-T (débito ${euro.format(imported.declaredTotalDebit ?? 0)}, crédito ${euro.format(imported.declaredTotalCredit ?? 0)}).`}</p>}
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
          {skippedRows.length > 0 && (skippedByReason
            ? <div className="table-wrap"><table><thead><tr><th>Motivo</th><th className="numeric">{imported ? 'Saltadas' : 'A saltar'}</th><th>Exemplos de transação</th></tr></thead><tbody>
                {skippedByReason.map(([reason, group]) => <tr key={reason}>
                  <td>{reason}</td>
                  <td className="numeric strong">{group.count}</td>
                  <td><small>{group.sample.join(', ')}{group.count > group.sample.length ? ' …' : ''}</small></td>
                </tr>)}
              </tbody></table></div>
            : <div className="table-wrap"><table><thead><tr><th>{imported ? 'Transação saltada' : 'Transação que seria saltada'}</th><th>Motivo</th></tr></thead><tbody>
                {skippedRows.map((entry) => <tr key={entry.transactionId}>
                  <td className="primary-cell">{entry.transactionId}</td>
                  <td>{entry.reason}</td>
                </tr>)}
              </tbody></table></div>)}
        </>
      : <p className="save-status"><Info size={15} /> O ficheiro não tem movimentos (bloco <code>GeneralLedgerEntries</code>).</p>)}
  </section>
}

export function SaftImportPage() {
  const { inspectSaft, refreshWorkContext, source } = useWorkContext()
  const inputRef = useRef<HTMLInputElement>(null)
  const [state, setState] = useState<'idle' | 'reading' | 'done' | 'error'>('idle')
  const [fileName, setFileName] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<SaftInspection | null>(null)
  const [error, setError] = useState('')
  const [validation, setValidation] = useState<SaftValidation | null>(null)
  const [override, setOverride] = useState(false)

  const validationBlocks = source === 'database' && saftValidationBlocksImport(validation)
  const importBlocked = validationBlocks && !override

  const onValidationResult = useCallback((next: SaftValidation | null) => {
    setValidation(next)
    setOverride(false)
  }, [])

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

  // Empresa criada a partir do ficheiro: traz o contexto de trabalho novo
  // (a empresa entra no seletor), seleciona-a e volta a ler o cabeçalho —
  // agora o NIF corresponde e o ecrã segue para importar contas e
  // movimentos, sem recarregar a página.
  async function onCompanyCreated(companyId: string) {
    await refreshWorkContext(companyId)
    if (file) await onFile(file)
  }

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Importação contabilística</p><h1>Importar SAF-T</h1><p>Leitura do cabeçalho, correspondência da empresa pelo NIF, criação e provisionamento quando não existe, e pré-visualização e importação do plano de contas e dos movimentos do ficheiro.</p></div>
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

      {source === 'database' && <>
        <SaftValidationPanel file={file} companyId={result.match.companyId} companyName={result.header.companyName} validation={validation} onResult={onValidationResult} />
        {validationBlocks && <section className="panel">
          <label className="save-status" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <input type="checkbox" checked={override} onChange={(event) => setOverride(event.target.checked)} />
            <span>Importar mesmo assim — assumo a responsabilidade de importar um ficheiro com erros de validação.</span>
          </label>
        </section>}
      </>}

      {result.match.outcome === 'ExistingCompany'
        ? <>
            <section className="panel">
              <div className="panel-heading"><div><h2>Correspondência de empresa</h2><p>Pesquisa pelo NIF do ficheiro entre as empresas a que tem acesso.</p></div><span className="success"><CircleCheck /></span></div>
              <p>Empresa encontrada: <strong>{result.match.legalName}</strong>{result.match.accountingStandard ? ` · ${accountingStandardName(result.match.accountingStandard)}` : ''}{result.match.accountingSetupVersion ? ` · ${result.match.accountingSetupVersion}` : ''}.</p>
              <p className="save-status"><Info size={15} /> Importe primeiro o plano de contas do ficheiro; só depois os movimentos, para que as contas referidas existam.</p>
            </section>
            {result.match.companyId && <AccountsPreviewPanel companyId={result.match.companyId} file={file} fiscalYear={result.header.fiscalYear} importBlocked={importBlocked} />}
            {result.match.companyId && source === 'database' && <EntriesPreviewPanel companyId={result.match.companyId} file={file} fiscalYear={result.header.fiscalYear} importBlocked={importBlocked} />}
          </>
        : <CompanyProposal inspection={result} onCreated={(companyId) => void onCompanyCreated(companyId)} />}
    </>}
  </div>
}
