import { CircleAlert, CircleCheck, Database, FileUp, Receipt } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { euro, formatMovementDate, type EFaturaImportResult, type EFaturaPurchaseRow } from '../app/demo-data'
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

export function EFaturaPurchasesPage() {
  const { activeCompany, eFaturaPurchases, importEFaturaCsv, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const pageSize = activeCompany.settings.pageSize || 50
  const inputRef = useRef<HTMLInputElement>(null)
  const [pending, setPending] = useState<Pending[]>([])
  const [reload, setReload] = useState(0)

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

  const canImportAny = pending.some((entry) => entry.preview && !entry.imported && !entry.error)

  if (source !== 'database') {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Importação contabilística</p><h1>Compras e-Fatura</h1><p>Importação e consulta das faturas de compra conhecidas pelo Portal e-Fatura.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Receipt /></span><h2>Requer ligação à base de dados</h2><p>As compras do e-Fatura são lidas e guardadas na base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Importação contabilística</p><h1>Compras e-Fatura</h1><p>Importe um ou mais CSV obtidos no Portal e-Fatura (adquirente). Reimportar o mesmo ficheiro não duplica.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

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
