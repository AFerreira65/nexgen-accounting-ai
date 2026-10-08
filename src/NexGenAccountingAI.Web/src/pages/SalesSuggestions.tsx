import { CircleAlert, CircleCheck, FileUp, Trash2, WandSparkles } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, formatMovementDate, type EntryTemplateRow, type SalesDocumentsImportResult, type SalesEntryRuleRow, type SalesSuggestedEntriesReport, type SuggestedEntryOutcome } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'
import { percentLabel, SuggestionLines, SuggestionOutcomes } from './suggestionParts'

const typeLabels: Record<string, string> = { FT: 'Fatura', FS: 'Fatura simplificada', FR: 'Fatura-recibo', NC: 'Nota de crédito', ND: 'Nota de débito' }

/** O separador «Vendas» dos lançamentos sugeridos: documentos de um SAF-T de faturação, com regra por taxa de IVA. */
export function SalesSuggestions({ fromDate, toDate }: { fromDate: string; toDate: string }) {
  const { activeCompany, can, importSalesDocuments, salesSuggestedEntries, finalizeSalesSuggestedEntries, salesEntryRules, saveSalesEntryRule, deleteSalesEntryRule, entryTemplates } = useWorkContext()
  const companyId = activeCompany.companyId
  const inputRef = useRef<HTMLInputElement>(null)
  const [report, setReport] = useState<SalesSuggestedEntriesReport | null>(null)
  const [rules, setRules] = useState<SalesEntryRuleRow[]>([])
  const [templates, setTemplates] = useState<EntryTemplateRow[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [ruleTemplate, setRuleTemplate] = useState<Record<string, string>>({})
  const [pendingFile, setPendingFile] = useState<{ file: File; preview: SalesDocumentsImportResult } | null>(null)
  const [imported, setImported] = useState<SalesDocumentsImportResult | null>(null)
  const [outcomes, setOutcomes] = useState<SuggestedEntryOutcome[]>([])
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!fromDate || !toDate) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void Promise.all([salesSuggestedEntries(companyId, { from: fromDate, to: toDate }), salesEntryRules(companyId), entryTemplates(companyId, false)])
      .then(([value, ruleRows, templateRows]) => {
        if (controller.signal.aborted) return
        setReport(value)
        setRules(ruleRows)
        setTemplates(templateRows)
        setSelected(new Set(value.items.filter((item) => item.ready).map((item) => item.documentId)))
      })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as sugestões de vendas.') } })
    return () => controller.abort()
  }, [companyId, fromDate, toDate, reload, salesSuggestedEntries, salesEntryRules, entryTemplates])

  async function run(action: () => Promise<void>, fallback: string) {
    setBusy(true)
    setError('')
    try {
      await action()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : fallback)
    } finally {
      setBusy(false)
    }
  }

  const choose = (files: FileList | null) => {
    const file = files?.[0]
    if (inputRef.current) inputRef.current.value = ''
    if (!file) return
    setImported(null)
    void run(async () => setPendingFile({ file, preview: await importSalesDocuments(companyId, file, true) }), 'Não foi possível ler o ficheiro.')
  }

  const confirmImport = () => void run(async () => {
    if (!pendingFile) return
    setImported(await importSalesDocuments(companyId, pendingFile.file, false))
    setPendingFile(null)
    setReload((value) => value + 1)
  }, 'Não foi possível importar o ficheiro.')

  const finalize = () => void run(async () => {
    setOutcomes(await finalizeSalesSuggestedEntries(companyId, [...selected]))
    setReload((value) => value + 1)
  }, 'Não foi possível criar os lançamentos.')

  const createRule = (percentage: number) => void run(async () => {
    const code = ruleTemplate[String(percentage)] ?? ''
    if (!code) throw new Error('Escolha o lançamento-tipo da regra.')
    await saveSalesEntryRule(companyId, percentage, { entryTemplateCode: code, isActive: true })
    setReload((value) => value + 1)
  }, 'Não foi possível guardar a regra.')

  const removeRule = (percentage: number) => void run(async () => {
    await deleteSalesEntryRule(companyId, percentage)
    setReload((value) => value + 1)
  }, 'Não foi possível apagar a regra.')

  const toggle = (documentId: string) => setSelected((current) => {
    const next = new Set(current)
    if (next.has(documentId)) next.delete(documentId)
    else next.add(documentId)
    return next
  })

  const items = report?.items ?? []
  const exportCsv = () => downloadCsv(
    csvFilename(`vendas-sugeridas-${fromDate}-${toDate}`, activeCompany.legalName),
    toCsv([
      ['Data', 'Documento', 'Tipo', 'NIF', 'Cliente', 'Base', 'IVA', 'Total', 'Taxas', 'Situação'],
      ...items.map((item) => [item.invoiceDate, item.invoiceNo, item.invoiceType, item.customerTaxId, item.customerName, item.netTotal, item.taxPayable, item.grossTotal, item.percentages.join(' / '), item.problem ?? item.warning ?? 'Pronto']),
    ]),
  )

  return <>
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    <SuggestionOutcomes outcomes={outcomes} />

    {can('saftimport.import') && <section className="panel">
      <div className="panel-heading"><div><h2>Importar o SAF-T de faturação</h2><p>Os documentos de venda do ficheiro (faturas, faturas simplificadas e recibo, notas de crédito e de débito) ficam guardados com a base e o IVA por taxa. Reimportar não duplica; um ficheiro de outra empresa é recusado.</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <input ref={inputRef} type="file" accept=".xml,application/xml,text/xml" className="sr-only" aria-label="Escolher SAF-T de faturação" onChange={(event) => choose(event.target.files)} />
        <button className="button button-secondary" type="button" disabled={busy} onClick={() => inputRef.current?.click()}><FileUp size={16} /> Escolher ficheiro</button>
        {pendingFile && <>
          <span className="inline-check">{pendingFile.file.name}: {pendingFile.preview.imported} novos, {pendingFile.preview.alreadyExisted} já importados{pendingFile.preview.voided > 0 ? `, ${pendingFile.preview.voided} anulados` : ''}</span>
          <button className="button button-primary" type="button" disabled={busy || pendingFile.preview.imported === 0} onClick={confirmImport}>Importar</button>
        </>}
      </div>
      {pendingFile?.preview.warnings.map((warning) => <p key={warning} className="save-status"><CircleAlert size={15} /> {warning}</p>)}
      {imported && <p className="save-status"><CircleCheck size={15} /> Importados {imported.imported} documentos{imported.from ? ` de ${formatMovementDate(imported.from)} a ${formatMovementDate(imported.to ?? imported.from)}` : ''}.</p>}
    </section>}

    {report && <section className="metadata-strip">
      <span><small>Documentos no período</small><strong>{report.documentCount}</strong></span>
      <span><small>Já lançados</small><strong>{report.bookedCount}</strong></span>
      <span><small>Sugestões prontas</small><strong>{report.readyCount}</strong></span>
      {report.voidedCount > 0 && <span><small>Anulados</small><strong>{report.voidedCount}</strong></span>}
    </section>}

    {report && (report.percentagesWithoutRule.length > 0 || rules.length > 0) && <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Regras por taxa de IVA</h2><p>A parte de cada documento a uma taxa é lançada com o lançamento-tipo da regra dessa taxa — rendimento, IVA liquidado e cliente. As linhas iguais juntam-se num só lançamento por documento; a do cliente usa a conta própria quando a ficha de terceiro a tiver.</p></div></div>
      {templates.length === 0 && <p className="save-status"><CircleAlert size={15} /> Ainda não há lançamentos-tipo ativos. Crie um por taxa em <Link to="/lancamentos-tipo">Lançamentos-tipo</Link> (por exemplo, rendimento + IVA liquidado + cliente).</p>}
      <div className="table-wrap"><table>
        <thead><tr><th>Taxa</th><th>Lançamento-tipo</th><th /></tr></thead>
        <tbody>
          {rules.map((rule) => <tr key={rule.taxPercentage}>
            <td>{percentLabel(rule.taxPercentage)}</td>
            <td>{rule.entryTemplateCode} — {rule.entryTemplateName}{rule.isActive ? '' : ' (inativa)'}</td>
            <td>{can('suggested.post') && <button className="icon-button" type="button" aria-label={`Apagar a regra de ${percentLabel(rule.taxPercentage)}`} disabled={busy} onClick={() => removeRule(rule.taxPercentage)}><Trash2 size={16} /></button>}</td>
          </tr>)}
          {can('suggested.post') && templates.length > 0 && report.percentagesWithoutRule.map((percentage) => <tr key={`novo-${percentage}`}>
            <td>{percentLabel(percentage)}</td>
            <td><select aria-label={`Lançamento-tipo para ${percentLabel(percentage)}`} value={ruleTemplate[String(percentage)] ?? ''} onChange={(event) => setRuleTemplate((current) => ({ ...current, [String(percentage)]: event.target.value }))}><option value="">Escolher…</option>{templates.map((template) => <option key={template.code} value={template.code}>{template.code} — {template.name}</option>)}</select></td>
            <td><button className="button button-secondary" type="button" disabled={busy || !ruleTemplate[String(percentage)]} onClick={() => createRule(percentage)}>Criar regra</button></td>
          </tr>)}
        </tbody>
      </table></div>
    </section>}

    {report && report.documentCount === 0 && <section className="panel empty-state"><h2>Sem documentos de venda no período</h2><p>Importe o SAF-T de faturação da empresa acima.</p></section>}
    {report && report.documentCount > 0 && items.length === 0 && <section className="panel empty-state"><span className="empty-icon"><CircleCheck /></span><h2>Tudo lançado</h2><p>Todos os documentos de venda do período têm lançamento na contabilidade.</p></section>}

    {items.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Documentos por lançar</h2><p>{report?.readyCount === items.length ? 'Todos com sugestão pronta.' : `${report?.readyCount ?? 0} de ${items.length} com sugestão pronta.`} As notas de crédito invertem os lados.</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        {can('suggested.post') && <button className="button button-primary" type="button" onClick={finalize} disabled={busy || selected.size === 0}><WandSparkles size={16} /> {busy ? 'A lançar…' : `Finalizar ${selected.size} ${selected.size === 1 ? 'selecionado' : 'selecionados'}`}</button>}
        <ExportButtons run={exportCsv} />
      </div>
      <div className="table-wrap"><table>
        <thead><tr><th><span className="sr-only">Selecionar</span></th><th>Data</th><th>Documento</th><th>Cliente</th><th className="numeric">Total</th><th>Lançamento proposto</th></tr></thead>
        <tbody>{items.map((item) => <tr key={item.documentId}>
          <td>{can('suggested.post') && <input type="checkbox" aria-label={`Selecionar ${item.invoiceNo}`} disabled={!item.ready} checked={selected.has(item.documentId)} onChange={() => toggle(item.documentId)} />}</td>
          <td>{formatMovementDate(item.invoiceDate)}</td>
          <td>{item.invoiceNo}<br /><small>{typeLabels[item.invoiceType] ?? item.invoiceType} · {item.percentages.map(percentLabel).join(' / ')}</small></td>
          <td>{item.customerName || '—'}<br /><small>NIF {item.customerTaxId || '—'}</small></td>
          <td className="numeric strong">{euro.format(item.grossTotal)}<br /><small>base {euro.format(item.netTotal)} · IVA {euro.format(item.taxPayable)}</small></td>
          <td>{item.ready
            ? <><small>{item.journalCode}{item.documentTypeCode ? ` · ${item.documentTypeCode}` : ''} · {item.description}</small><SuggestionLines lines={item.lines} />{item.warning && <span className="suggestion-note"><CircleAlert size={13} /> {item.warning}</span>}</>
            : <span className="suggestion-problem">{item.problem}</span>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    <p className="statement-note">Um documento conta como lançado quando tem um lançamento criado daqui que não foi anulado, ou quando há um lançamento de vendas (contas 71, 72 ou 2433) com o mesmo número de documento. Se as vendas forem lançadas por resumo mensal, os documentos aparecem aqui como por lançar — não os finalize. Os anulados no SAF-T não são sugeridos; os lançamentos ficam por conferir, com a origem «Sugerido do SAF-T de faturação».</p>
  </>
}
