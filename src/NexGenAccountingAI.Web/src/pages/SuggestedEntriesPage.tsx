import { Ban, CircleAlert, CircleCheck, Database, FileQuestion, History, RotateCcw, Sparkles, Trash2, WandSparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, formatMovementDate, type EntryTemplateRow, type SuggestedEntriesReport, type SuggestedEntry, type SuggestedEntryOutcome, type SupplierClassificationProposal, type SupplierEntryRuleRow } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'
import { SalesSuggestions } from './SalesSuggestions'
import { SuggestionLines, SuggestionOutcomes } from './suggestionParts'

type Tab = 'compras' | 'vendas'

export function SuggestedEntriesPage() {
  const { activeCompany, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const [tab, setTab] = useState<Tab>('compras')
  usePublishRecord(undefined, undefined)

  if (source !== 'database') {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Lançamentos sugeridos</h1><p>As compras do e-Fatura e as vendas do SAF-T de faturação ainda por lançar, cada uma com o lançamento proposto pelas regras da empresa.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><WandSparkles /></span><h2>Requer ligação à base de dados</h2><p>As sugestões partem das faturas do e-Fatura e dos documentos de venda importados e das regras guardadas por empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Lançamentos sugeridos</h1><p>Os documentos que ainda não estão na contabilidade, cada um com o lançamento proposto por um <Link to="/lancamentos-tipo">lançamento-tipo</Link>: as compras do <Link to="/compras-efatura">e-Fatura</Link>, pela regra do fornecedor, e as vendas do SAF-T de faturação, pela regra de cada taxa de IVA. Finalizar cria o lançamento por conferir, com os valores do documento.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    <div className="filterbar">
      <div className="period-shortcuts" role="tablist" aria-label="Origem dos documentos">
        {([['compras', 'Compras (e-Fatura)'], ['vendas', 'Vendas (SAF-T de faturação)']] as const).map(([key, label]) => <button key={key} type="button" role="tab" aria-selected={tab === key} className={`period-chip${tab === key ? ' active' : ''}`} onClick={() => setTab(key)}>{label}</button>)}
      </div>
      <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={fiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
    </div>

    {tab === 'compras'
      ? <PurchaseSuggestions fromDate={fromDate} toDate={toDate} />
      : <SalesSuggestions fromDate={fromDate} toDate={toDate} />}
  </div>
}

/** O separador «Compras»: faturas do e-Fatura, com regra por fornecedor. */
function PurchaseSuggestions({ fromDate, toDate }: { fromDate: string; toDate: string }) {
  const { activeCompany, can, suggestedEntries, finalizeSuggestedEntries, dismissPurchases, restorePurchases, classificationFromHistory, classificationAiStatus, classificationFromAi, acceptClassification, supplierEntryRules, saveSupplierEntryRule, deleteSupplierEntryRule, entryTemplates } = useWorkContext()
  const companyId = activeCompany.companyId
  const [report, setReport] = useState<SuggestedEntriesReport | null>(null)
  const [rules, setRules] = useState<SupplierEntryRuleRow[]>([])
  const [templates, setTemplates] = useState<EntryTemplateRow[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [ruleTemplate, setRuleTemplate] = useState<Record<string, string>>({})
  const [outcomes, setOutcomes] = useState<SuggestedEntryOutcome[]>([])
  const [dismissReason, setDismissReason] = useState('')
  const [proposals, setProposals] = useState<Record<string, SupplierClassificationProposal>>({})
  const [accountEdits, setAccountEdits] = useState<Record<string, string>>({})
  const [aiConfigured, setAiConfigured] = useState(false)
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!fromDate || !toDate) { setReport(null); return }
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
  }, [companyId, fromDate, toDate, reload, suggestedEntries, supplierEntryRules, entryTemplates])

  const items = useMemo(() => report?.items ?? [], [report])
  const withoutRule = useMemo(() => {
    const seen = new Map<string, SuggestedEntry>()
    for (const item of items) if (item.entryTemplateCode === null && !seen.has(item.supplierTaxId)) seen.set(item.supplierTaxId, item)
    return [...seen.values()]
  }, [items])

  // As propostas pelo histórico dos fornecedores sem regra, e se a IA está disponível para os outros.
  useEffect(() => {
    if (withoutRule.length === 0) { setProposals({}); return }
    const controller = new AbortController()
    void Promise.all([classificationFromHistory(companyId, withoutRule.map((item) => item.supplierTaxId)), classificationAiStatus(companyId)])
      .then(([rows, configured]) => {
        if (controller.signal.aborted) return
        setProposals(Object.fromEntries(rows.map((row) => [row.supplierTaxId, row])))
        setAiConfigured(configured)
      })
      .catch(() => { if (!controller.signal.aborted) setProposals({}) })
    return () => controller.abort()
  }, [classificationAiStatus, classificationFromHistory, companyId, withoutRule])

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

  const readyIds = new Set(items.filter((item) => item.ready).map((item) => item.purchaseId))
  const selectedReady = [...selected].filter((id) => readyIds.has(id))

  const finalize = () => void run(async () => {
    setOutcomes(await finalizeSuggestedEntries(companyId, selectedReady))
    setReload((value) => value + 1)
  }, 'Não foi possível criar os lançamentos.')

  const dismiss = () => void run(async () => {
    if (dismissReason.trim() === '') throw new Error('Indique porque é que as faturas não se lançam.')
    await dismissPurchases(companyId, [...selected], dismissReason.trim())
    setDismissReason('')
    setReload((value) => value + 1)
  }, 'Não foi possível dispensar as faturas.')

  const restore = (purchaseId: string) => void run(async () => {
    await restorePurchases(companyId, [purchaseId])
    setReload((value) => value + 1)
  }, 'Não foi possível repor a fatura.')

  // O mapa de documentos em falta: as faturas por lançar agrupadas por fornecedor, para pedir ao cliente.
  const exportMissing = () => {
    const bySupplier = new Map<string, SuggestedEntry[]>()
    for (const item of items) bySupplier.set(item.supplierTaxId, [...(bySupplier.get(item.supplierTaxId) ?? []), item])
    const total = (group: SuggestedEntry[]) => Math.round(group.reduce((sum, item) => sum + item.grossTotal, 0) * 100) / 100
    downloadCsv(
      csvFilename(`documentos-em-falta-${fromDate}-${toDate}`, activeCompany.legalName),
      toCsv([
        ['NIF', 'Fornecedor', 'Faturas', 'Total', 'Documentos'],
        ...[...bySupplier.values()]
          .sort((left, right) => total(right) - total(left))
          .map((group) => [group[0].supplierTaxId, group[0].supplierName, group.length, total(group), group.map((item) => `${item.invoiceNo} (${item.invoiceDate})`).join('; ')]),
      ]),
    )
  }

  const createRule = (supplierTaxId: string) => void run(async () => {
    const code = ruleTemplate[supplierTaxId] ?? ''
    if (!code) throw new Error('Escolha o lançamento-tipo da regra.')
    await saveSupplierEntryRule(companyId, supplierTaxId, { entryTemplateCode: code, description: null, isActive: true })
    setReload((value) => value + 1)
  }, 'Não foi possível guardar a regra.')

  const askAi = (supplierTaxId: string) => void run(async () => {
    const proposal = await classificationFromAi(companyId, supplierTaxId)
    setProposals((current) => ({ ...current, [supplierTaxId]: proposal }))
  }, 'Não foi possível obter a proposta da IA.')

  const accept = (proposal: SupplierClassificationProposal) => void run(async () => {
    if (!proposal.supplierAccount) throw new Error('A proposta não tem conta de fornecedor: crie o lançamento-tipo à mão.')
    await acceptClassification(companyId, {
      supplierTaxId: proposal.supplierTaxId,
      accountCode: (accountEdits[proposal.supplierTaxId] ?? proposal.account.code).trim(),
      vatCode: proposal.vatCode?.code ?? null,
      vatAccountCode: proposal.vatAccount?.code ?? null,
      supplierAccountCode: proposal.supplierAccount.code,
      journalCode: proposal.journalCode,
    })
    setReload((value) => value + 1)
  }, 'Não foi possível aceitar a proposta.')

  const removeRule = (supplierTaxId: string) => void run(async () => {
    await deleteSupplierEntryRule(companyId, supplierTaxId)
    setReload((value) => value + 1)
  }, 'Não foi possível apagar a regra.')

  const toggle = (purchaseId: string) => setSelected((current) => {
    const next = new Set(current)
    if (next.has(purchaseId)) next.delete(purchaseId)
    else next.add(purchaseId)
    return next
  })

  const exportCsv = () => downloadCsv(
    csvFilename(`compras-sugeridas-${fromDate}-${toDate}`, activeCompany.legalName),
    toCsv([
      ['Data', 'NIF', 'Fornecedor', 'Tipo', 'Documento', 'Base', 'IVA', 'Total', 'Lançamento-tipo', 'Situação'],
      ...items.map((item) => [item.invoiceDate, item.supplierTaxId, item.supplierName, item.invoiceType, item.invoiceNo, item.taxBase, item.taxAmount, item.grossTotal, item.entryTemplateCode ?? '', item.problem ?? item.warning ?? 'Pronto']),
    ]),
  )

  const ready = items.filter((item) => item.ready)

  return <>
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    <SuggestionOutcomes outcomes={outcomes} />

    {report && <section className="metadata-strip">
      <span><small>Faturas no período</small><strong>{report.purchaseCount}</strong></span>
      <span><small>Já lançadas</small><strong>{report.bookedCount}</strong></span>
      <span><small>Sugestões prontas</small><strong>{report.readyCount}</strong></span>
      <span><small>Fornecedores sem regra</small><strong>{withoutRule.length}</strong><small>{report.withoutRuleCount} faturas</small></span>
      {report.cancelledCount > 0 && <span><small>Anuladas no Portal</small><strong>{report.cancelledCount}</strong></span>}
    </section>}

    {report && report.purchaseCount === 0 && <section className="panel empty-state"><h2>Sem faturas do e-Fatura no período</h2><p>Importe o CSV das compras em <Link to="/compras-efatura">Compras e-Fatura</Link>.</p></section>}
    {report && report.purchaseCount > 0 && items.length === 0 && <section className="panel empty-state"><span className="empty-icon"><CircleCheck /></span><h2>Tudo lançado</h2><p>Todas as faturas do período têm lançamento na contabilidade.</p></section>}

    {can('suggested.post') && withoutRule.length > 0 && <section className="panel settings-panel">
      <div className="panel-heading"><div><h2>Fornecedores sem regra</h2><p>Escolha o lançamento-tipo com que as faturas de cada fornecedor passam a ser sugeridas — ou aceite a proposta tirada das compras já lançadas dele{aiConfigured ? ', ou peça uma à IA quando não as há' : ''}. Aceitar cria o lançamento-tipo «H-NIF» e a regra; pode corrigir a conta antes. A contrapartida usa a conta própria do fornecedor quando a ficha de terceiro a tiver.</p></div></div>
      {withoutRule.some((item) => proposals[item.supplierTaxId]) || aiConfigured ? <div className="table-wrap"><table>
        <thead><tr><th>Fornecedor</th><th>Proposta</th><th /></tr></thead>
        <tbody>{withoutRule.map((item) => {
          const proposal = proposals[item.supplierTaxId]
          return <tr key={`proposta-${item.supplierTaxId}`}>
            <td>{item.supplierName}<small>NIF {item.supplierTaxId}</small></td>
            <td>{proposal
              ? <>
                <span className="inline-check">{proposal.source === 'ia' ? <Sparkles size={14} /> : <History size={14} />} Conta <input aria-label={`Conta proposta para ${item.supplierTaxId}`} value={accountEdits[item.supplierTaxId] ?? proposal.account.code} onChange={(event) => setAccountEdits((current) => ({ ...current, [item.supplierTaxId]: event.target.value }))} style={{ width: '8em' }} />{proposal.account.name ? ` ${proposal.account.name}` : ''}</span>
                <small>{proposal.vatCode ? `IVA ${proposal.vatCode.code} na ${proposal.vatAccount?.code ?? '—'} · ` : 'sem IVA dedutível · '}fornecedor {proposal.supplierAccount?.code ?? '—'} · diário {proposal.journalCode}{proposal.source === 'historico' ? ` · ${Math.round(proposal.confidence * 100)} % das compras` : ''}</small>
                <small>{proposal.explanation}</small>
              </>
              : <small>Sem compras lançadas deste fornecedor.</small>}</td>
            <td>{proposal
              ? <button className="button button-secondary" type="button" disabled={busy || !proposal.supplierAccount} onClick={() => accept(proposal)}>Aceitar proposta</button>
              : aiConfigured && <button className="button button-secondary" type="button" disabled={busy} onClick={() => askAi(item.supplierTaxId)}><Sparkles size={15} /> Pedir à IA</button>}</td>
          </tr>
        })}</tbody>
      </table></div> : null}
      {templates.length === 0
        ? <p className="save-status"><CircleAlert size={15} /> Ainda não há lançamentos-tipo ativos. Crie um em <Link to="/lancamentos-tipo">Lançamentos-tipo</Link> (por exemplo, gasto + IVA dedutível + fornecedor).</p>
        : <div className="table-wrap"><table>
          <thead><tr><th>NIF</th><th>Fornecedor</th><th>Lançamento-tipo</th><th /></tr></thead>
          <tbody>{withoutRule.map((item) => <tr key={item.supplierTaxId}>
            <td>{item.supplierTaxId}</td>
            <td>{item.supplierName}</td>
            <td><select aria-label={`Lançamento-tipo para ${item.supplierTaxId}`} value={ruleTemplate[item.supplierTaxId] ?? ''} onChange={(event) => setRuleTemplate((current) => ({ ...current, [item.supplierTaxId]: event.target.value }))}><option value="">Escolher…</option>{templates.map((template) => <option key={template.code} value={template.code}>{template.code} — {template.name}</option>)}</select></td>
            <td><button className="button button-secondary" type="button" disabled={busy || !ruleTemplate[item.supplierTaxId]} onClick={() => createRule(item.supplierTaxId)}>Criar regra</button></td>
          </tr>)}</tbody>
        </table></div>}
    </section>}

    {items.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Faturas por lançar</h2><p>{ready.length === items.length ? 'Todas com sugestão pronta.' : `${ready.length} de ${items.length} com sugestão pronta.`} As notas de crédito invertem os lados do lançamento-tipo.</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        {can('suggested.post') && <button className="button button-primary" type="button" onClick={finalize} disabled={busy || selectedReady.length === 0}><WandSparkles size={16} /> {busy ? 'A lançar…' : `Finalizar ${selectedReady.length} ${selectedReady.length === 1 ? 'selecionado' : 'selecionados'}`}</button>}
        <ExportButtons run={exportCsv} />
        <button className="button button-secondary" type="button" onClick={exportMissing} title="As faturas por lançar agrupadas por fornecedor, para pedir os documentos"><FileQuestion size={16} /> Documentos em falta</button>
      </div>
      {can('suggested.post') && <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <label className="inline-check">Motivo <input aria-label="Motivo para não lançar" list="motivos-nao-lancar" value={dismissReason} onChange={(event) => setDismissReason(event.target.value)} placeholder="ex.: despesa particular" /></label>
        <datalist id="motivos-nao-lancar"><option value="Despesa particular" /><option value="Documento em duplicado" /><option value="Lançado noutro documento" /><option value="Não é desta empresa" /></datalist>
        <button className="button button-secondary" type="button" onClick={dismiss} disabled={busy || selected.size === 0 || dismissReason.trim() === ''}><Ban size={16} /> Não lançar os selecionados</button>
      </div>}
      <div className="table-wrap"><table>
        <thead><tr><th><span className="sr-only">Selecionar</span></th><th>Data</th><th>Fornecedor</th><th>Documento</th><th className="numeric">Total</th><th>Lançamento proposto</th></tr></thead>
        <tbody>{items.map((item) => <tr key={item.purchaseId}>
          <td>{can('suggested.post') && <input type="checkbox" aria-label={`Selecionar ${item.invoiceNo}`} checked={selected.has(item.purchaseId)} onChange={() => toggle(item.purchaseId)} />}</td>
          <td>{formatMovementDate(item.invoiceDate)}</td>
          <td>{item.supplierName}<br /><small>NIF {item.supplierTaxId}</small></td>
          <td>{item.invoiceNo}<br /><small>{item.invoiceType}{item.isCreditNote ? <span className="badge warning">nota de crédito</span> : null}</small></td>
          <td className="numeric strong">{euro.format(item.grossTotal)}<br /><small>base {euro.format(item.taxBase)} · IVA {euro.format(item.taxAmount)}</small></td>
          <td>{item.ready
            ? <><strong>{item.entryTemplateCode}</strong> <small>{item.journalCode}{item.documentTypeCode ? ` · ${item.documentTypeCode}` : ''} · {item.description}</small><SuggestionLines lines={item.lines} />{item.warning && <span className="suggestion-note"><CircleAlert size={13} /> {item.warning}</span>}</>
            : <span className="suggestion-problem">{item.problem}</span>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    {report && report.dismissed.length > 0 && <section className="panel">
      <div className="panel-heading"><div><h2>Não lançar</h2><p>Faturas do período que não entram na contabilidade, com o motivo. Não contam nas sugestões nem no cruzamento.</p></div><span className="attention-count">{report.dismissed.length}</span></div>
      <div className="table-wrap"><table>
        <thead><tr><th>Data</th><th>Fornecedor</th><th>Documento</th><th className="numeric">Total</th><th>Motivo</th><th /></tr></thead>
        <tbody>{report.dismissed.map((item) => <tr key={item.purchaseId}>
          <td>{formatMovementDate(item.invoiceDate)}</td>
          <td>{item.supplierName}<br /><small>NIF {item.supplierTaxId}</small></td>
          <td>{item.invoiceNo}</td>
          <td className="numeric">{euro.format(item.grossTotal)}</td>
          <td>{item.reason}</td>
          <td>{can('suggested.post') && <button className="icon-button" type="button" aria-label={`Repor ${item.invoiceNo}`} title="Repor nas sugestões" disabled={busy} onClick={() => restore(item.purchaseId)}><RotateCcw size={16} /></button>}</td>
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
          <td>{can('suggested.post') && <button className="icon-button" type="button" aria-label={`Apagar a regra de ${rule.supplierTaxId}`} disabled={busy} onClick={() => removeRule(rule.supplierTaxId)}><Trash2 size={16} /></button>}</td>
        </tr>)}</tbody>
      </table></div>
    </section>}

    <p className="statement-note">Uma fatura conta como lançada quando tem um lançamento criado daqui que não foi anulado, ou quando o <Link to="/cruzamento">cruzamento</Link> lhe encontra um lançamento de compra pelo número do documento. As faturas anuladas no Portal não são sugeridas, e as marcadas «não lançar» saem também do cruzamento. «Documentos em falta» exporta as faturas por lançar agrupadas por fornecedor, para pedir os documentos. Os lançamentos ficam por conferir, com a origem «Sugerido do e-Fatura».</p>
  </>
}
