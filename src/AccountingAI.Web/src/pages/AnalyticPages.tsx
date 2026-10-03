import { ChevronDown, ChevronRight, Database, Download, FolderTree, Plus, X } from 'lucide-react'
import { Fragment, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type AnalyticReport, type CostCenterRow } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

function CostCenterEditor({ costCenter, onSaved, onClose }: { costCenter: CostCenterRow | null; onSaved: () => void; onClose: () => void }) {
  const { activeCompany, saveCostCenter } = useWorkContext()
  const isCreate = costCenter === null
  const [code, setCode] = useState('')
  const [name, setName] = useState(costCenter?.name ?? '')
  const [isActive, setIsActive] = useState(costCenter?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      await saveCostCenter(activeCompany.companyId, { code: isCreate ? code : costCenter.code, name, isActive }, isCreate)
      onSaved()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o centro de custo.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Novo centro de custo' : `Editar ${costCenter.code}`}</h2><p>{isCreate ? 'O código não se altera depois de criado.' : 'Um centro inativo deixa de poder ser escolhido, mas os lançamentos que o têm mantêm-no.'}</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} maxLength={20} placeholder="ex.: OBRA.01" autoFocus /></label>}
      <label><span>Nome</span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={120} /></label>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Centro ativo</strong><small>Desative para o retirar das novas seleções.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar centro de custo'}</button>
      </footer>
    </form>
  </section>
}

export function CostCentersPage() {
  const { activeCompany, source, costCenters } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const [rows, setRows] = useState<CostCenterRow[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; costCenter: CostCenterRow } | null>(null)
  usePublishRecord(editor?.mode === 'edit' ? 'centros-custo' : undefined, editor?.mode === 'edit' ? editor.costCenter.code : undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    setError('')
    void costCenters(companyId, includeInactive)
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os centros de custo.') })
    return () => controller.abort()
  }, [companyId, onDatabase, costCenters, includeInactive, reload])

  const saved = () => { setEditor(null); setReload((value) => value + 1) }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Centros de custo</h1><p>A dimensão analítica que se indica na linha do lançamento.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><FolderTree /></span><h2>Requer ligação ao SQL Server</h2><p>Os centros de custo são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Centros de custo</h1><p>Atividades, obras ou departamentos a que se atribuem gastos e rendimentos na linha do lançamento, sem desdobrar o plano de contas. O resultado por centro está no <Link to="/mapa-analitico">Mapa analítico</Link>.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} /> Incluir inativos</label>
      <span className="catalog-count">{rows.length} {rows.length === 1 ? 'centro de custo' : 'centros de custo'}</span>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo centro de custo</button>
    </div>

    {editor?.mode === 'create' && <CostCenterEditor costCenter={null} onSaved={saved} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <CostCenterEditor key={editor.costCenter.code} costCenter={editor.costCenter} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Código</th><th>Nome</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.code}>
          <td><span className="primary-cell">{row.code}</span></td>
          <td>{row.name}</td>
          <td><span className={`badge ${row.isActive ? 'success' : 'warning'}`}>{row.isActive ? 'Ativo' : 'Inativo'}</span></td>
          <td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', costCenter: row })}>Editar</button></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Ainda não há centros de custo. Depois de criar o primeiro, o editor de lançamentos ganha a coluna «C. custo».</p>}
    </section>
  </div>
}

export function AnalyticReportPage() {
  const { activeCompany, source, analyticReport } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, companyId)
  const [report, setReport] = useState<AnalyticReport | null>(null)
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void analyticReport(companyId, { from: fromDate, to: toDate })
      .then((value) => { if (!controller.signal.aborted) setReport(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o mapa analítico.') })
    return () => controller.abort()
  }, [analyticReport, companyId, onDatabase, fromDate, toDate])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa analítico</h1><p>Gastos, rendimentos e resultado por centro de custo.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><FolderTree /></span><h2>Requer ligação ao SQL Server</h2><p>O mapa analítico é calculado na base de dados da empresa.</p></section>
    </div>
  }

  const keyOf = (code: string | null) => code ?? '—'
  const toggle = (code: string | null) => setOpen((current) => {
    const next = new Set(current)
    if (next.has(keyOf(code))) next.delete(keyOf(code))
    else next.add(keyOf(code))
    return next
  })
  const centers = report?.centers ?? []
  const exportCsv = () => downloadCsv(
    csvFilename('mapa-analitico', activeCompany.legalName),
    toCsv([
      ['Centro de custo', 'Nome', 'Conta', 'Descrição', 'Débito', 'Crédito'],
      ...centers.flatMap((center) => center.accounts.map((account) => [center.code ?? '', center.name, account.accountCode, account.accountName, account.debit, account.credit])),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa analítico</h1><p>Os gastos (classe 6) e os rendimentos (classe 7) do período por centro de custo. As linhas sem centro aparecem no fim, para o total bater com a demonstração de resultados.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">De <input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label className="inline-check">Até <input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button className="button button-secondary" type="button" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={centers.length === 0}><Download size={16} /> Exportar CSV</button>
    </div>

    {report && <section className="metadata-strip">
      <span><small>Rendimentos</small><strong>{euro.format(report.totalIncome)}</strong></span>
      <span><small>Gastos</small><strong>{euro.format(report.totalExpenses)}</strong></span>
      <span><small>Resultado</small><strong>{euro.format(report.totalResult)}</strong></span>
      <span><small>Centros com movimento</small><strong>{centers.filter((center) => center.code !== null).length}</strong></span>
    </section>}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Centro de custo</th><th className="numeric">Rendimentos</th><th className="numeric">Gastos</th><th className="numeric">Resultado</th></tr></thead>
        <tbody>{centers.map((center) => {
          const expanded = open.has(keyOf(center.code))
          return <Fragment key={keyOf(center.code)}>
            <tr className={center.code === null ? 'muted-row' : undefined}>
              <td><button type="button" className="row-toggle" aria-expanded={expanded} onClick={() => toggle(center.code)}>{expanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}<span className="primary-cell">{center.code ?? '—'}</span> {center.name}</button></td>
              <td className="numeric">{euro.format(center.income)}</td>
              <td className="numeric">{euro.format(center.expenses)}</td>
              <td className="numeric strong">{euro.format(center.result)}</td>
            </tr>
            {expanded && center.accounts.map((account) => <tr key={`${keyOf(center.code)}-${account.accountCode}`} className="detail-row">
              <td><Link to={`/contas/${account.accountCode}`}>{account.accountCode}</Link> {account.accountName}</td>
              <td className="numeric">{account.accountCode.startsWith('7') ? euro.format(account.credit - account.debit) : '—'}</td>
              <td className="numeric">{account.accountCode.startsWith('6') ? euro.format(account.debit - account.credit) : '—'}</td>
              <td />
            </tr>)}
          </Fragment>
        })}</tbody>
      </table></div>
      {report && centers.length === 0 && <p className="empty-state">Sem gastos nem rendimentos no período.</p>}
    </section>
  </div>
}
