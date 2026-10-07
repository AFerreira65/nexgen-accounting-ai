import { CircleAlert, CircleCheck, CloudDownload, Database, FileUp, Receipt } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { euro, formatMovementDate, type EFaturaAtImportOutcome, type EFaturaImportResult, type EFaturaPurchaseRow } from '../app/demo-data'
import { Pager } from '../app/Pager'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'

type Pending = {
  file: File
  preview: EFaturaImportResult | null
  imported: EFaturaImportResult | null
  error: string | null
  busy: boolean
}

/** O mês anterior ao de hoje, que é o que normalmente falta importar. */
function lastMonth() {
  const now = new Date()
  const first = new Date(Date.UTC(now.getFullYear(), now.getMonth() - 1, 1))
  const last = new Date(Date.UTC(now.getFullYear(), now.getMonth(), 0))
  return { from: first.toISOString().slice(0, 10), to: last.toISOString().slice(0, 10) }
}

export function EFaturaPurchasesPage() {
  const { activeCompany, eFaturaPurchases, importEFaturaCsv, importEFaturaFromAt, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const pageSize = activeCompany.settings.pageSize || 50
  const inputRef = useRef<HTMLInputElement>(null)
  const [pending, setPending] = useState<Pending[]>([])
  const [reload, setReload] = useState(0)
  const [atFrom, setAtFrom] = useState(() => lastMonth().from)
  const [atTo, setAtTo] = useState(() => lastMonth().to)
  const [atBusy, setAtBusy] = useState(false)
  const [atError, setAtError] = useState('')
  const [atPreview, setAtPreview] = useState<EFaturaAtImportOutcome | null>(null)
  const [atDone, setAtDone] = useState<EFaturaAtImportOutcome | null>(null)

  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, companyId)
  const [page, setPage] = useState(1)
  const [list, setList] = useState<{ items: EFaturaPurchaseRow[]; totalItems: number; totalPages: number; currentPage: number } | null>(null)
  const [listError, setListError] = useState('')

  useEffect(() => setPage(1), [fromDate, toDate])

  useEffect(() => {
    if (source !== 'database') { setList(null); return }
    const controller = new AbortController()
    void eFaturaPurchases(companyId, { from: fromDate || undefined, to: toDate || undefined, page, pageSize })
      .then((body) => { setList({ items: body.items, totalItems: body.totalItems, totalPages: body.totalPages, currentPage: body.page }); setListError('') })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setListError(error instanceof Error ? error.message : 'Não foi possível carregar as compras.')
      })
    return () => controller.abort()
  }, [companyId, eFaturaPurchases, fromDate, toDate, page, pageSize, source, reload])

  async function onFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    const items: Pending[] = Array.from(files).map((file) => ({ file, preview: null, imported: null, error: null, busy: true }))
    setPending(items)
    await Promise.all(items.map(async (item, index) => {
      try {
        const preview = await importEFaturaCsv(companyId, item.file, true)
        setPending((current) => current.map((entry, position) => position === index ? { ...entry, preview, busy: false } : entry))
      } catch (error) {
        setPending((current) => current.map((entry, position) => position === index
          ? { ...entry, error: error instanceof Error ? error.message : 'CSV ilegível.', busy: false }
          : entry))
      }
    }))
  }

  async function importOne(index: number) {
    setPending((current) => current.map((entry, position) => position === index ? { ...entry, busy: true } : entry))
    try {
      const imported = await importEFaturaCsv(companyId, pending[index].file, false)
      setPending((current) => current.map((entry, position) => position === index ? { ...entry, imported, busy: false } : entry))
      setReload((value) => value + 1)
    } catch (error) {
      setPending((current) => current.map((entry, position) => position === index
        ? { ...entry, error: error instanceof Error ? error.message : 'Não foi possível importar.', busy: false }
        : entry))
    }
  }

  async function importAll() {
    for (let index = 0; index < pending.length; index++) {
      if (!pending[index].imported && !pending[index].error && pending[index].preview) {
        await importOne(index)
      }
    }
  }

  async function queryAt(dryRun: boolean) {
    setAtBusy(true)
    setAtError('')
    if (dryRun) { setAtPreview(null); setAtDone(null) }
    try {
      const outcome = await importEFaturaFromAt(companyId, { from: atFrom, to: atTo, dryRun })
      if (outcome.problem) { setAtError(outcome.problem); setAtPreview(null); return }
      if (dryRun) setAtPreview(outcome)
      else { setAtDone(outcome); setAtPreview(null); setReload((value) => value + 1) }
    } catch (error) {
      setAtError(error instanceof Error ? error.message : 'Não foi possível consultar a AT.')
    } finally {
      setAtBusy(false)
    }
  }

  const atDatesValid = Boolean(atFrom) && Boolean(atTo) && atFrom <= atTo
  const atResult = atDone ?? atPreview
  const canImportAny = pending.some((entry) => entry.preview && !entry.imported && !entry.error)

  if (source !== 'database') {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Importação contabilística</p><h1>Compras e-Fatura</h1><p>Importação e consulta das faturas de compra conhecidas pelo Portal e-Fatura.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Receipt /></span><h2>Requer ligação à base de dados</h2><p>As compras do e-Fatura são lidas e guardadas na base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Importação contabilística</p><h1>Compras e-Fatura</h1><p>Importe as faturas de compra diretamente da AT, ou um ou mais CSV obtidos no Portal e-Fatura (adquirente). Importar outra vez o mesmo período não duplica.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    <section className="panel">
      <div className="panel-heading"><div><h2>Importar da AT</h2><p>Consulta o webservice do e-Fatura com o subutilizador da empresa (permissão de consulta de faturas), configurado em «Comunicação com a AT». A AT responde a um mês de cada vez; períodos longos demoram.</p></div></div>
      <div className="filterbar">
        <label><span className="sr-only">Início da consulta</span><input type="date" aria-label="Início da consulta" value={atFrom} onChange={(event) => setAtFrom(event.target.value)} /></label>
        <label><span className="sr-only">Fim da consulta</span><input type="date" aria-label="Fim da consulta" value={atTo} onChange={(event) => setAtTo(event.target.value)} /></label>
        <button className="button button-primary" type="button" disabled={atBusy || !atDatesValid} onClick={() => void queryAt(true)}><CloudDownload size={16} /> {atBusy && !atPreview ? 'A consultar a AT…' : 'Consultar a AT'}</button>
        {atPreview?.import && atPreview.import.imported > 0 && <button className="button button-secondary" type="button" disabled={atBusy} onClick={() => void queryAt(false)}>Importar {atPreview.import.imported} {atPreview.import.imported === 1 ? 'fatura' : 'faturas'}</button>}
      </div>
      {atError && <p className="save-status error" role="alert"><CircleAlert size={15} /> {atError}</p>}
      {atResult?.import && <p className="save-status" role="status">
        {atDone ? <CircleCheck size={15} /> : null}{' '}
        {atDone ? 'Importado' : 'Pré-visualização'} ({atResult.environment === 'Producao' ? 'AT produção' : 'ambiente de testes da AT'}): a AT devolveu {atResult.fetched} {atResult.fetched === 1 ? 'documento' : 'documentos'}; {atResult.import.imported} {atDone ? 'novos' : 'por importar'} e {atResult.import.alreadyExisted} já existiam.
        {atResult.import.warnings.length > 0 && <small> {atResult.import.warnings.join(' ')}</small>}
        {atResult.import.imported > 0 && <small> A AT não indica o nome do emitente: fica o do cadastro de terceiros ou, na falta dele, o NIF.</small>}
      </p>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Importar CSV</h2><p>Cada ficheiro é pré-visualizado antes de gravar. Lotes de 300 registos são assinalados.</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <input ref={inputRef} type="file" accept=".csv,text/csv" multiple className="sr-only" aria-label="Escolher CSV do e-Fatura" onChange={(event) => void onFiles(event.target.files)} />
        <button className="button button-primary" type="button" onClick={() => inputRef.current?.click()}><FileUp size={16} /> Escolher ficheiros</button>
        {canImportAny && <button className="button button-secondary" type="button" onClick={() => void importAll()}>Importar todos</button>}
      </div>
      {pending.length > 0 && <div className="table-wrap"><table><thead><tr><th>Ficheiro</th><th className="numeric">A importar</th><th className="numeric">Já existiam</th><th className="numeric">Inválidas</th><th>Avisos</th><th><span className="sr-only">Ação</span></th></tr></thead><tbody>
        {pending.map((entry, index) => <tr key={entry.file.name + index}>
          <td className="primary-cell">{entry.file.name}</td>
          <td className="numeric strong">{entry.imported ? entry.imported.imported : entry.preview ? entry.preview.imported : entry.busy ? '…' : '—'}</td>
          <td className="numeric">{(entry.imported ?? entry.preview)?.alreadyExisted ?? '—'}</td>
          <td className="numeric">{(entry.imported ?? entry.preview)?.invalid ?? '—'}</td>
          <td><small>{entry.error ? entry.error : ((entry.imported ?? entry.preview)?.warnings.join(' ') || '—')}</small></td>
          <td>{entry.imported
            ? <span className="badge success"><CircleCheck size={13} /> Importado</span>
            : entry.error
              ? <span className="badge warning"><CircleAlert size={13} /> Erro</span>
              : entry.preview
                ? <button className="button button-secondary" type="button" disabled={entry.busy || entry.preview.imported === 0} onClick={() => void importOne(index)}>Importar</button>
                : '…'}</td>
        </tr>)}
      </tbody></table></div>}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Compras importadas</h2><p>{list ? `${list.totalItems} ${list.totalItems === 1 ? 'fatura' : 'faturas'}` : '—'}</p></div></div>
      <div className="filterbar">
        <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
        <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
        <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
        {(fromDate || toDate) && <button className="button button-secondary" type="button" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>}
      </div>
      {listError && <p className="save-status error"><CircleAlert size={15} /> {listError}</p>}
      <div className="table-wrap"><table><thead><tr><th>Data</th><th>Fornecedor</th><th>Fatura</th><th>Tipo</th><th className="numeric">Base</th><th className="numeric">IVA</th><th className="numeric">Total</th></tr></thead><tbody>
        {(list?.items ?? []).map((row) => <tr key={row.id}>
          <td>{formatMovementDate(row.invoiceDate)}</td>
          <td>{row.supplierName}<small>{row.supplierTaxId}</small></td>
          <td>{row.invoiceNo}{row.atcud ? <small>ATCUD {row.atcud}</small> : null}</td>
          <td>{row.invoiceType}</td>
          <td className="numeric">{euro.format(row.taxBase)}</td>
          <td className="numeric">{euro.format(row.taxAmount)}</td>
          <td className="numeric strong">{euro.format(row.grossTotal)}</td>
        </tr>)}
      </tbody></table></div>
      {list && <Pager currentPage={list.currentPage} totalPages={list.totalPages} onPage={setPage} />}
      {list && list.totalItems === 0 && <p className="empty-state">Sem compras do e-Fatura no período.</p>}
    </section>
  </div>
}
