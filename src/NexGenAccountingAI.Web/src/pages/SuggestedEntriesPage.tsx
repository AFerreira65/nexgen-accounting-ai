import { CircleAlert, CircleCheck, Database, Download, Trash2, WandSparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type EntryTemplateRow, type SuggestedEntriesReport, type SuggestedEntry, type SuggestedEntryOutcome, type SupplierEntryRuleRow } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

function SuggestionLines({ item }: { item: SuggestedEntry }) {
  return <table className="nested-lines">
    <tbody>{item.lines.map((line, index) => <tr key={`${line.accountCode}-${index}`}>
      <td><Link to={`/contas/${line.accountCode}`}>{line.accountCode}</Link> {line.accountName}{line.vatCode ? <small> · {line.vatCode}</small> : null}{line.partyTaxNumber ? <small> · NIF {line.partyTaxNumber}</small> : null}</td>
      <td className="numeric">{line.debit ? euro.format(line.debit) : ''}</td>
      <td className="numeric">{line.credit ? euro.format(line.credit) : ''}</td>
    </tr>)}</tbody>
  </table>
}

export function SuggestedEntriesPage() {
  const { activeCompany, source, suggestedEntries, finalizeSuggestedEntries, supplierEntryRules, saveSupplierEntryRule, deleteSupplierEntryRule, entryTemplates } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const [report, setReport] = useState<SuggestedEntriesReport | null>(null)
  const [rules, setRules] = useState<SupplierEntryRuleRow[]>([])
  const [templates, setTemplates] = useState<EntryTemplateRow[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [ruleTemplate, setRuleTemplate] = useState<Record<string, string>>({})
  const [outcomes, setOutcomes] = useState<SuggestedEntryOutcome[]>([])
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase || !fromDate || !toDate) { setReport(null); return }
    const controller = new AbortController()
    setError('')
    void Promise.all([suggestedEntries(companyId, { from: fromDate, to: toDate }), supplierEntryRules(companyId), entryTemplates(companyId, false)])
      .then(([value, ruleRows, templateRows]) => {
        if (controller.signal.aborted) return
        setReport(value)
        setRules(ruleRows)
        setTemplates(templateRows)
        setSelected(new Set(value.items.filter((item) => item.ready).map((item) => item.purchaseId)))
      })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setReport(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as sugestões.') } })
    return () => controller.abort()
  }, [companyId, onDatabase, fromDate, toDate, reload, suggestedEntries, supplierEntryRules, entryTemplates])

  const items = useMemo(() => report?.items ?? [], [report])
  const withoutRule = useMemo(() => {
    const seen = new Map<string, SuggestedEntry>()
    for (const item of items) if (item.entryTemplateCode === null && !seen.has(item.supplierTaxId)) seen.set(item.supplierTaxId, item)
    return [...seen.values()]
  }, [items])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Lançamentos sugeridos</h1><p>As compras do e-Fatura ainda por lançar, cada uma com a proposta da regra do fornecedor.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><WandSparkles /></span><h2>Requer ligação à base de dados</h2><p>As sugestões partem das faturas do e-Fatura importadas e das regras guardadas por empresa.</p></section>
    </div>
  }

  function toggle(purchaseId: string) {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(purchaseId)) next.delete(purchaseId)
      else next.add(purchaseId)
      return next
    })
  }

  async function finalize() {
    setBusy(true)
    setError('')
    try {
      const result = await finalizeSuggestedEntries(companyId, [...selected])
      setOutcomes(result)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível criar os lançamentos.')
    } finally {
      setBusy(false)
    }
  }

  async function createRule(supplierTaxId: string) {
    const code = ruleTemplate[supplierTaxId] ?? ''
    if (!code) { setError('Escolha o lançamento-tipo da regra.'); return }
    setBusy(true)
    setError('')
    try {
      await saveSupplierEntryRule(companyId, supplierTaxId, { entryTemplateCode: code, description: null, isActive: true })
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a regra.')
    } finally {
      setBusy(false)
    }
  }

  async function removeRule(supplierTaxId: string) {
    setBusy(true)
    setError('')
    try {
      await deleteSupplierEntryRule(companyId, supplierTaxId)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível apagar a regra.')
    } finally {
      setBusy(false)
    }
  }

  const exportCsv = () => downloadCsv(
    csvFilename(`lancamentos-sugeridos-${fromDate}-${toDate}`, activeCompany.legalName),
    toCsv([
      ['Data', 'NIF', 'Fornecedor', 'Tipo', 'Documento', 'Base', 'IVA', 'Total', 'Lançamento-tipo', 'Situação'],
      ...items.map((item) => [item.invoiceDate, item.supplierTaxId, item.supplierName, item.invoiceType, item.invoiceNo, item.taxBase, item.taxAmount, item.grossTotal, item.entryTemplateCode ?? '', item.problem ?? item.warning ?? 'Pronto']),
    ]),
  )

  const created = outcomes.filter((outcome) => outcome.reference)
  const failed = outcomes.filter((outcome) => outcome.error)
  const ready = items.filter((item) => item.ready)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Lançamentos sugeridos</h1><p>As compras do <Link to="/compras-efatura">e-Fatura</Link> que ainda não estão na contabilidade, cada uma com o lançamento proposto pela regra do fornecedor — um <Link to="/lancamentos-tipo">lançamento-tipo</Link>. Finalizar cria o lançamento por conferir, com a base e o IVA da fatura.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {created.length > 0 && <p className="save-status"><CircleCheck size={15} /> {created.length === 1 ? 'Criado 1 lançamento por conferir' : `Criados ${created.length} lançamentos por conferir`}: {created.map((outcome, index) => <span key={outcome.reference}>{index > 0 ? ', ' : ''}<Link to={`/movimentos/${outcome.reference}`}>{outcome.reference}</Link></span>)}.</p>}
    {failed.length > 0 && <p className="save-status error"><CircleAlert size={15} /> {failed.length === 1 ? '1 sugestão não foi lançada' : `${failed.length} sugestões não foram lançadas`}: {failed.map((outcome) => outcome.error).filter((value, index, all) => all.indexOf(value) === index).join(' ')}</p>}

    <div className="filterbar">
      <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={items.length === 0}><Download size={16} /> Exportar CSV</button>
      <button className="button button-primary" type="button" onClick={() => void finalize()} disabled={busy || selected.size === 0}><WandSparkles size={16} /> {busy ? 'A lançar…' : `Finalizar ${selected.size} ${selected.size === 1 ? 'selecionado' : 'selecionados'}`}</button>
    </div>

    {report && <section className="metadata-strip">
      <span><small>Faturas no período</small><strong>{report.purchaseCount}</strong></span>
      <span><small>Já lançadas</small><strong>{report.bookedCount}</strong></span>
      <span><small>Sugestões prontas</small><strong>{report.readyCount}</strong></span>
      <span><small>Fornecedores sem regra</small><strong>{withoutRule.length}</strong><small>{report.withoutRuleCount} faturas</small></span>
      {report.cancelledCount > 0 && <span><small>Anuladas no Portal</small><strong>{report.cancelledCount}</strong></span>}
    </section>}

    {report && report.purchaseCount === 0 && <section className="panel empty-state"><h2>Sem faturas do e-Fatura no período</h2><p>Importe o CSV das compras em <Link to="/compras-efatura">Compras e-Fatura</Link>.</p></section>}
    {report && report.purchaseCount > 0 && items.length === 0 && <section className="panel empty-state"><span className="empty-icon"><CircleCheck /></span><h2>Tudo lançado</h2><p>Todas as faturas do período têm lançamento na contabilidade.</p></section>}

    {withoutRule.length > 0 && <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Fornecedores sem regra</h2><p>Escolha o lançamento-tipo com que as faturas de cada fornecedor passam a ser sugeridas. A contrapartida usa a conta própria do fornecedor quando a ficha de terceiro a tiver.</p></div></div>
      {templates.length === 0
        ? <p className="save-status"><CircleAlert size={15} /> Ainda não há lançamentos-tipo ativos. Crie um em <Link to="/lancamentos-tipo">Lançamentos-tipo</Link> (por exemplo, gasto + IVA dedutível + fornecedor).</p>
        : <div className="table-wrap"><table>
          <thead><tr><th>NIF</th><th>Fornecedor</th><th>Lançamento-tipo</th><th /></tr></thead>
          <tbody>{withoutRule.map((item) => <tr key={item.supplierTaxId}>
            <td>{item.supplierTaxId}</td>
            <td>{item.supplierName}</td>
            <td><select aria-label={`Lançamento-tipo para ${item.supplierTaxId}`} value={ruleTemplate[item.supplierTaxId] ?? ''} onChange={(event) => setRuleTemplate((current) => ({ ...current, [item.supplierTaxId]: event.target.value }))}><option value="">Escolher…</option>{templates.map((template) => <option key={template.code} value={template.code}>{template.code} — {template.name}</option>)}</select></td>
            <td><button className="button button-secondary" type="button" disabled={busy || !ruleTemplate[item.supplierTaxId]} onClick={() => void createRule(item.supplierTaxId)}>Criar regra</button></td>
          </tr>)}</tbody>
        </table></div>}
    </section>}

    {items.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Faturas por lançar</h2><p>{ready.length === items.length ? 'Todas com sugestão pronta.' : `${ready.length} de ${items.length} com sugestão pronta.`} As notas de crédito invertem os lados do lançamento-tipo.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th><span className="sr-only">Selecionar</span></th><th>Data</th><th>Fornecedor</th><th>Documento</th><th className="numeric">Total</th><th>Lançamento proposto</th></tr></thead>
        <tbody>{items.map((item) => <tr key={item.purchaseId}>
          <td><input type="checkbox" aria-label={`Selecionar ${item.invoiceNo}`} disabled={!item.ready} checked={selected.has(item.purchaseId)} onChange={() => toggle(item.purchaseId)} /></td>
          <td>{formatMovementDate(item.invoiceDate)}</td>
          <td>{item.supplierName}<br /><small>NIF {item.supplierTaxId}</small></td>
          <td>{item.invoiceNo}<br /><small>{item.invoiceType}{item.isCreditNote ? <span className="badge warning">nota de crédito</span> : null}</small></td>
          <td className="numeric strong">{euro.format(item.grossTotal)}<br /><small>base {euro.format(item.taxBase)} · IVA {euro.format(item.taxAmount)}</small></td>
          <td>{item.ready
            ? <><strong>{item.entryTemplateCode}</strong> <small>{item.journalCode}{item.documentTypeCode ? ` · ${item.documentTypeCode}` : ''} · {item.description}</small><SuggestionLines item={item} />{item.warning && <span className="suggestion-note"><CircleAlert size={13} /> {item.warning}</span>}</>
            : <span className="suggestion-problem">{item.problem}</span>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {rules.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Regras por fornecedor</h2><p>Para mudar a regra de um fornecedor, apague-a e crie-a de novo a partir de uma fatura dele.</p></div></div>
      <div className="table-wrap"><table>
        <thead><tr><th>NIF</th><th>Fornecedor</th><th>Lançamento-tipo</th><th>Estado</th><th /></tr></thead>
        <tbody>{rules.map((rule) => <tr key={rule.supplierTaxId}>
          <td>{rule.supplierTaxId}</td>
          <td>{rule.supplierName ?? '—'}</td>
          <td>{rule.entryTemplateCode} — {rule.entryTemplateName}</td>
          <td>{rule.isActive ? 'Ativa' : 'Inativa'}</td>
          <td><button className="icon-button" type="button" aria-label={`Apagar a regra de ${rule.supplierTaxId}`} disabled={busy} onClick={() => void removeRule(rule.supplierTaxId)}><Trash2 size={16} /></button></td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    <p className="statement-note">Uma fatura conta como lançada quando tem um lançamento criado daqui que não foi anulado, ou quando o <Link to="/cruzamento">cruzamento</Link> lhe encontra um lançamento de compra pelo número do documento. As faturas anuladas no Portal não são sugeridas. Os lançamentos ficam por conferir, com a origem «Sugerido do e-Fatura».</p>
  </div>
}
