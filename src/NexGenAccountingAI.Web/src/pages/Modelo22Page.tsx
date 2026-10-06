import { Calculator, CircleAlert, CircleCheck, Database, FileDown, FileText, Printer, Table2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type Modelo22Field, type Modelo22Return } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { problemMessage, useWorkContext } from '../app/WorkContext'
import { Modelo22Form } from './Modelo22Form'
import { Modelo22FilingPanel } from './VatDeclarationFilingPanel'

const sections = ['Quadro 07', 'Quadro 09', 'Tributações autónomas', 'Quadro 10'] as const
const sectionTitles: Record<string, string> = {
  'Quadro 07': 'Quadro 07 — Apuramento do lucro tributável',
  'Quadro 09': 'Quadro 09 — Apuramento da matéria coletável (regime geral)',
  'Tributações autónomas': 'Tributações autónomas — encargos por tipo',
  'Quadro 10': 'Quadro 10 — Cálculo do imposto',
}
const parse = (value: string) => Number(value.replace(/\s/g, '').replace(',', '.'))

export function Modelo22Page() {
  const { activeCompany, source, user, modelo22, saveModelo22 } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [result, setResult] = useState<Modelo22Return | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [sme, setSme] = useState(false)
  const [rate, setRate] = useState('')
  const [added, setAdded] = useState<Set<string>>(new Set())
  const [showAll, setShowAll] = useState(false)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [view, setView] = useState<'impresso' | 'tabela'>('impresso')
  const [error, setError] = useState('')
  const [xmlBusy, setXmlBusy] = useState(false)
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  function load(value: Modelo22Return) {
    setResult(value)
    setDraft(Object.fromEntries(value.fields.filter((field) => !field.computed && field.amount !== 0).map((field) => [field.code, String(field.amount)])))
    setSme(value.smallOrMediumEnterprise)
    setRate(value.municipalSurtaxRate === 0 ? '' : String(value.municipalSurtaxRate))
  }

  useEffect(() => {
    if (!onDatabase) { setResult(null); return }
    const controller = new AbortController()
    setError('')
    setStatus('idle')
    setAdded(new Set())
    void modelo22(companyId, year)
      .then((value) => { if (!controller.signal.aborted) load(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setResult(null); setError(problem instanceof Error ? problem.message : 'Não foi possível preparar o Modelo 22.') } })
    return () => controller.abort()
  }, [modelo22, companyId, onDatabase, year])

  /** O ficheiro XML no formato da AT, para entregar no Portal das Finanças. */
  async function downloadXml() {
    setXmlBusy(true); setError('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${companyId}/modelo-22/xml?year=${year}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      const name = /filename="?([^";]+)/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? `modelo22-${year}.xml`
      const url = URL.createObjectURL(await response.blob())
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = name
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o ficheiro.')
    } finally {
      setXmlBusy(false)
    }
  }

  const fields = useMemo(() => result?.fields ?? [], [result])
  const hidden = useMemo(
    () => fields.filter((field) => field.section === 'Quadro 07' && !field.computed && !(draft[field.code] ?? '') && field.suggested === null && !added.has(field.code)),
    [fields, draft, added],
  )

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Modelo 22</h1><p>Do resultado líquido ao imposto a pagar: quadros 07, 09 e 10 da declaração de rendimentos de IRC.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Calculator /></span><h2>Requer ligação à base de dados</h2><p>O apuramento parte do resultado líquido da contabilidade da empresa.</p></section>
    </div>
  }

  const value = (code: string) => fields.find((field) => field.code === code)?.amount ?? 0
  const set = (code: string, text: string) => { setDraft((current) => ({ ...current, [code]: text })); setStatus('idle') }

  async function save() {
    setError('')
    const values = Object.entries(draft).filter(([, text]) => text.trim() !== '').map(([field, text]) => ({ field, amount: parse(text) }))
    const municipal = rate.trim() === '' ? 0 : parse(rate)
    if (values.some((item) => !Number.isFinite(item.amount) || item.amount < 0) || !Number.isFinite(municipal) || municipal < 0) {
      setError('Os valores têm de ser números positivos.')
      return
    }

    setStatus('saving')
    try {
      load(await saveModelo22(companyId, year, [
        ...values.filter((item) => item.amount !== 0),
        ...(sme ? [{ field: 'pme', amount: 1 }] : []),
        ...(municipal !== 0 ? [{ field: 'derrama-municipal-taxa', amount: municipal }] : []),
      ]))
      setStatus('saved')
    } catch (problem) {
      setStatus('idle')
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar o Modelo 22.')
    }
  }

  const exportCsv = () => downloadCsv(
    csvFilename(`modelo-22-${year}`, activeCompany.legalName),
    toCsv([['Quadro', 'Campo', 'Descrição', 'Valor', 'Origem'], ...fields.filter((field) => field.computed || field.amount !== 0).map((field) => [field.section, field.code, field.label, field.amount, field.computed ? 'calculado' : 'indicado'])]),
  )

  const row = (field: Modelo22Field) => <tr key={field.code} className={field.computed ? 'subtotal-row' : undefined}>
    <td className="primary-cell">{field.code}</td>
    <td>{field.label}{field.suggested !== null && !(draft[field.code] ?? '') && <> <button type="button" className="period-chip" onClick={() => set(field.code, String(field.suggested))}>Usar {euro.format(field.suggested)} ({field.suggestionSource})</button></>}</td>
    <td className="numeric">{field.computed
      ? euro.format(field.amount)
      : <input className="amount-input" inputMode="decimal" aria-label={`Campo ${field.code}`} value={draft[field.code] ?? ''} onChange={(event) => set(field.code, event.target.value)} placeholder="0,00" />}</td>
  </tr>

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Modelo 22</h1><p>O impresso da AT do resultado líquido ao imposto a pagar: rosto e quadros 07 a 11. O resultado líquido, as somas, a coleta e o quadro 11 vêm da contabilidade; os acréscimos, as deduções e os pagamentos escrevem-se no impresso e ficam guardados por exercício.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      {view === 'tabela' && <>
        <label className="inline-check"><input type="checkbox" checked={sme} onChange={(event) => { setSme(event.target.checked); setStatus('idle') }} /> PME (taxa reduzida na primeira parcela)</label>
        <label className="inline-check">Derrama municipal (%) <input inputMode="decimal" aria-label="Taxa da derrama municipal" value={rate} onChange={(event) => { setRate(event.target.value); setStatus('idle') }} placeholder="ex.: 1,5" /></label>
      </>}
      <button className="button button-primary" type="button" onClick={() => void save()} disabled={!result || status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar e recalcular'}</button>
      {status === 'saved' && <span className="save-status success"><CircleCheck size={15} /> Guardado.</span>}
      <span className="view-toggle" role="group" aria-label="Vista">
        <button type="button" className={`button button-secondary ${view === 'impresso' ? 'is-active' : ''}`} aria-pressed={view === 'impresso'} onClick={() => setView('impresso')}><FileText size={15} /> Impresso</button>
        <button type="button" className={`button button-secondary ${view === 'tabela' ? 'is-active' : ''}`} aria-pressed={view === 'tabela'} onClick={() => setView('tabela')}><Table2 size={15} /> Tabela</button>
      </span>
      <ExportButtons run={exportCsv} disabled={!result} />
      <button className="button button-secondary" type="button" onClick={() => void downloadXml()} disabled={!result || xmlBusy}><FileDown size={15} /> {xmlBusy ? 'A gerar…' : 'Ficheiro XML (AT)'}</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!result}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {result && <>
      <section className="metadata-strip">
        <span><small>Resultado líquido (701)</small><strong>{euro.format(value('701'))}</strong></span>
        <span><small>{value('777') > 0 ? 'Prejuízo fiscal (777)' : 'Lucro tributável (778)'}</small><strong>{euro.format(value('777') > 0 ? value('777') : value('778'))}</strong></span>
        <span><small>Matéria coletável (311)</small><strong>{euro.format(value('311'))}</strong></span>
        <span><small>{value('368') > 0 ? 'Total a recuperar (368)' : 'Total a pagar (367)'}</small><strong>{euro.format(value('368') > 0 ? value('368') : value('367'))}</strong></span>
      </section>

      {result.warnings.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>A conferir</h2></div></div>
        <ul className="warning-list">{result.warnings.map((warning) => <li key={warning}><CircleAlert size={15} /> {warning}</li>)}</ul>
      </section>}

      {view === 'impresso' && <section className="panel atf-panel">
        <Modelo22Form result={result} draft={draft} onDraft={set} companyName={activeCompany.legalName} taxNumber={activeCompany.taxRegistrationNumber} sme={sme} onSme={(value) => { setSme(value); setStatus('idle') }} municipalRate={rate} onMunicipalRate={(value) => { setRate(value); setStatus('idle') }} />
      </section>}

      {view === 'tabela' && sections.map((section) => <section key={section} className="panel">
        <div className="panel-heading"><div><h2>{sectionTitles[section]}</h2>{section === 'Tributações autónomas' && <p>Indique os encargos do exercício sujeitos a tributação autónoma; o total calculado passa a ser o campo 365. Sem encargos indicados, o campo 365 fica como o preencher.</p>}{section === 'Quadro 07' && <p>Só aparecem os campos com valor. Para preencher outro, escolha-o na lista ou mostre todos. Os valores são os que guardou; as somas atualizam ao guardar.</p>}</div></div>
        {section === 'Quadro 07' && <div className="filterbar">
          <label className="inline-check">Adicionar campo <select aria-label="Adicionar campo do quadro 07" value="" onChange={(event) => { const code = event.target.value; if (code) setAdded((current) => new Set(current).add(code)) }}>
            <option value="">— escolher —</option>
            {hidden.map((field) => <option key={field.code} value={field.code}>{field.code} — {field.label.length > 110 ? `${field.label.slice(0, 110)}…` : field.label}</option>)}
          </select></label>
          <label className="inline-check"><input type="checkbox" checked={showAll} onChange={(event) => setShowAll(event.target.checked)} /> Mostrar todos os campos</label>
        </div>}
        <div className="table-wrap"><table>
          <thead><tr><th>Campo</th><th>Descrição</th><th className="numeric">Valor</th></tr></thead>
          <tbody>{fields
            .filter((field) => field.section === section && (section !== 'Quadro 07' || showAll || !hidden.includes(field)))
            .map(row)}</tbody>
        </table></div>
      </section>)}

      <p className="statement-note">{result.disclaimer} As taxas vêm das <Link to="/tabelas-fiscais">tabelas fiscais</Link> de {result.year}.</p>
      <p className="statement-note">Fonte dos campos e das fórmulas: {result.source}</p>
      <Modelo22FilingPanel companyId={companyId} year={year} />
    </>}
  </div>
}
