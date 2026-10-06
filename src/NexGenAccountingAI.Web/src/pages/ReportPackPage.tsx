import { ArrowDown, ArrowUp, Database, Eye, FileDown, Plus, Sparkles, Trash2 } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { signalLoadFailure } from '../app/loadFailure'
import { MONTH_NAMES } from '../app/mapLabels'
import { useWorkContext } from '../app/WorkContext'

type SectionKind = 'texto' | 'mapa' | 'balancete' | 'racios'
type Section = { kind: SectionKind; title: string; text: string; mapId: string | null; templateKey: string | null; depth: number | null; columnSet: string | null; enabled: boolean }
type PackRequest = { title: string; subtitle: string | null; year: number; fromMonth: number; toMonth: number; includeClosing: boolean; sections: Section[]; preparer: string | null }
type Preset = { key: string; label: string; description: string; request: PackRequest }
type Catalog = { maps: { id: string; name: string }[]; templates: { key: string; name: string }[] }
type Preview = { title: string; period: string; sections: { title: string; paragraphs: string[]; table: { header: string[]; rows: { cells: string[]; level: number; bold: boolean; isTitle: boolean }[]; numeric: boolean[] } | null; notes: string[] | null }[] }

const KIND_LABELS: Record<SectionKind, string> = { texto: 'Texto', mapa: 'Mapa', balancete: 'Balancete', racios: 'Rácios' }

function newSection(kind: SectionKind): Section {
  return { kind, title: KIND_LABELS[kind], text: '', mapId: null, templateKey: kind === 'mapa' ? 'demonstracao-resultados' : null, depth: kind === 'balancete' ? 2 : null, columnSet: null, enabled: true }
}

async function readRefusal(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback
}

/**
 * O relatório para a banca (F4B-04) e qualquer relato composto: capa, secções de texto
 * (redigidas à mão ou pela IA, sempre revistas antes), mapas, rácios e balancete, num
 * PDF gerado pelo servidor.
 */
export function ReportPackPage() {
  const { activeCompany, source, user } = useWorkContext()
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}`
  const [presets, setPresets] = useState<Preset[]>([])
  const [catalog, setCatalog] = useState<Catalog>({ maps: [], templates: [] })
  const [request, setRequest] = useState<PackRequest | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [busy, setBusy] = useState<'preview' | 'pdf' | number | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [instructions, setInstructions] = useState('')

  useEffect(() => {
    if (source !== 'database') return
    let cancelled = false
    void Promise.all([
      fetch(`${root}/reports/pack/presets?year=${fiscalYear}`).then(async (response) => response.ok ? await response.json() as Preset[] : Promise.reject(new Error())),
      fetch(`${root}/maps`).then(async (response) => response.ok ? await response.json() as Catalog : Promise.reject(new Error())),
    ]).then(([found, maps]) => {
      if (cancelled) return
      setPresets(found)
      setCatalog({ maps: maps.maps, templates: maps.templates })
      setRequest((current) => current ?? found[0]?.request ?? null)
    }).catch(() => { if (!cancelled) { signalLoadFailure(); setError('Não foi possível preparar o relatório.') } })
    return () => { cancelled = true }
  }, [fiscalYear, root, source])

  function update(patch: Partial<PackRequest>) {
    setRequest((current) => current ? { ...current, ...patch } : current)
    setPreview(null)
  }
  function updateSection(index: number, patch: Partial<Section>) {
    setRequest((current) => current ? { ...current, sections: current.sections.map((section, position) => position === index ? { ...section, ...patch } : section) } : current)
    setPreview(null)
  }
  function moveSection(index: number, delta: number) {
    setRequest((current) => {
      if (!current) return current
      const target = index + delta
      if (target < 0 || target >= current.sections.length) return current
      const sections = [...current.sections]
      const [item] = sections.splice(index, 1)
      sections.splice(target, 0, item)
      return { ...current, sections }
    })
  }

  async function narrate(index: number) {
    if (!request) return
    setBusy(index)
    setError('')
    try {
      const response = await fetch(`${root}/reports/pack/narrate`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sectionTitle: request.sections[index].title, instructions, year: request.year, fromMonth: request.fromMonth, toMonth: request.toMonth }),
      })
      if (!response.ok) throw new Error(await readRefusal(response, 'Não foi possível pedir o texto à IA.'))
      const narration = await response.json() as { paragraphs: string[]; unavailable: string | null; model: string }
      if (narration.unavailable) { setError(narration.unavailable); return }
      updateSection(index, { text: `${narration.paragraphs.join('\n\n')}\n\n[IA] Texto redigido por ${narration.model}; reveja antes de entregar.` })
      setNotice('Texto redigido pela IA. Reveja-o e corrija o que entender antes de gerar o PDF.')
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível pedir o texto à IA.')
    } finally {
      setBusy(null)
    }
  }

  async function generate(format: 'json' | 'pdf') {
    if (!request) return
    setBusy(format === 'json' ? 'preview' : 'pdf')
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${root}/reports/pack${format === 'json' ? '?format=json' : ''}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request),
      })
      if (!response.ok) throw new Error(await readRefusal(response, 'Não foi possível gerar o relatório.'))
      if (format === 'json') {
        setPreview(await response.json() as Preview)
        return
      }
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = response.headers.get('Content-Disposition')?.match(/filename="?([^";]+)"?/)?.[1] ?? 'relatorio.pdf'
      anchor.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
      setNotice('PDF gerado.')
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o relatório.')
    } finally {
      setBusy(null)
    }
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Relatório para a banca</h1><p>O relatório em PDF precisa da base de dados; em demonstração não está disponível.</p></div></header></div>
  }

  return <div className="page-stack report-pack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gestão</p><h1>Relatório para a banca</h1><p>Um PDF com capa, apresentação, balanço, demonstração de resultados, exploração mensal, rácios e balancete — o dossier que um banco pede. Acrescente, tire ou reordene secções; o texto pode ser redigido pela IA e é sempre revisto por si.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert">{error}</p>}
    {notice && <p className="save-status success">{notice}</p>}
    {request && <>
      <section className="panel">
        <div className="panel-heading"><h2>Capa e período</h2>
          {presets.length > 1 && <select aria-label="Modelo de relatório" onChange={(event) => { const preset = presets.find((item) => item.key === event.target.value); if (preset) { setRequest(preset.request); setPreview(null) } }}>{presets.map((preset) => <option key={preset.key} value={preset.key}>{preset.label}</option>)}</select>}
        </div>
        <div className="builder-grid">
          <label className="field-block"><span>Título</span><input value={request.title} onChange={(event) => update({ title: event.target.value })} /></label>
          <label className="field-block"><span>Subtítulo</span><input value={request.subtitle ?? ''} onChange={(event) => update({ subtitle: event.target.value || null })} /></label>
          <label className="field-block"><span>Preparado por</span><input value={request.preparer ?? ''} onChange={(event) => update({ preparer: event.target.value || null })} placeholder="Nome do gabinete ou do contabilista" /></label>
        </div>
        <div className="filterbar balancete-filters">
          <label className="balancete-field"><span>Exercício</span><input type="number" min={1990} max={2100} value={request.year} onChange={(event) => { const next = Number(event.target.value); if (next >= 1990 && next <= 2100) update({ year: next }) }} /></label>
          <label className="balancete-field"><span>De</span><select value={request.fromMonth} onChange={(event) => { const next = Number(event.target.value); update({ fromMonth: next, toMonth: Math.max(next, request.toMonth) }) }}>{MONTH_NAMES.map((month, index) => <option key={month} value={index + 1}>{month}</option>)}</select></label>
          <label className="balancete-field"><span>A</span><select value={request.toMonth} onChange={(event) => { const next = Number(event.target.value); update({ toMonth: next, fromMonth: Math.min(next, request.fromMonth) }) }}>{MONTH_NAMES.map((month, index) => <option key={month} value={index + 1}>{month}</option>)}</select></label>
          <label className="balancete-field"><span>Apuramento</span><select value={request.includeClosing ? 'com' : 'sem'} onChange={(event) => update({ includeClosing: event.target.value === 'com' })}><option value="sem">Antes do apuramento</option><option value="com">Com o apuramento de resultados</option></select></label>
        </div>
      </section>

      <section className="panel">
        <div className="panel-heading"><h2>Secções</h2></div>
        <ol className="pack-sections">
          {request.sections.map((section, index) => <li key={index} className={`pack-section ${section.enabled ? '' : 'is-off'}`}>
            <div className="pack-section-head">
              <label className="toggle-inline"><input type="checkbox" checked={section.enabled} onChange={(event) => updateSection(index, { enabled: event.target.checked })} aria-label={`Incluir a secção ${section.title}`} /> {index + 1}.</label>
              <span className="pack-kind">{KIND_LABELS[section.kind]}</span>
              <input className="pack-title" aria-label={`Título da secção ${index + 1}`} value={section.title} onChange={(event) => updateSection(index, { title: event.target.value })} />
              {section.kind === 'mapa' && <select aria-label={`Mapa da secção ${index + 1}`} value={section.mapId ? `mapa:${section.mapId}` : `modelo:${section.templateKey ?? ''}`} onChange={(event) => { const [kind, key] = event.target.value.split(':'); updateSection(index, kind === 'mapa' ? { mapId: key, templateKey: null } : { mapId: null, templateKey: key }) }}>
                <optgroup label="Modelos">{catalog.templates.map((template) => <option key={template.key} value={`modelo:${template.key}`}>{template.name}</option>)}</optgroup>
                {catalog.maps.length > 0 && <optgroup label="Mapas da empresa">{catalog.maps.map((map) => <option key={map.id} value={`mapa:${map.id}`}>{map.name}</option>)}</optgroup>}
              </select>}
              {section.kind === 'balancete' && <select aria-label={`Nível do balancete da secção ${index + 1}`} value={section.depth ?? 2} onChange={(event) => updateSection(index, { depth: Number(event.target.value) })}>{[1, 2, 3, 4].map((depth) => <option key={depth} value={depth}>{depth === 1 ? 'Classes' : `Até ao ${depth}.º nível`}</option>)}</select>}
              <span className="map-row-actions">
                <button type="button" className="icon-button" aria-label={`Subir a secção ${index + 1}`} onClick={() => moveSection(index, -1)}><ArrowUp size={14} /></button>
                <button type="button" className="icon-button" aria-label={`Descer a secção ${index + 1}`} onClick={() => moveSection(index, 1)}><ArrowDown size={14} /></button>
                <button type="button" className="icon-button" aria-label={`Apagar a secção ${index + 1}`} onClick={() => update({ sections: request.sections.filter((_, position) => position !== index) })}><Trash2 size={14} /></button>
              </span>
            </div>
            {section.kind === 'texto' && <div className="pack-text">
              <textarea aria-label={`Texto da secção ${index + 1}`} rows={6} value={section.text} onChange={(event) => updateSection(index, { text: event.target.value })} placeholder="Escreva o texto em parágrafos separados por uma linha em branco, ou peça-o à IA." />
              <div className="pack-ai">
                <input aria-label="Indicações para a IA" value={instructions} onChange={(event) => setInstructions(event.target.value)} placeholder="Indicações para a IA (opcional): o que realçar, o fim do crédito…" />
                <button type="button" className="button button-secondary" disabled={busy !== null} onClick={() => void narrate(index)}><Sparkles size={15} /> {busy === index ? 'A redigir…' : 'Redigir com IA'}</button>
              </div>
            </div>}
          </li>)}
        </ol>
        <div className="pack-add">
          <span>Acrescentar:</span>
          {(Object.keys(KIND_LABELS) as SectionKind[]).map((kind) => <button key={kind} type="button" className="button button-secondary" onClick={() => update({ sections: [...request.sections, newSection(kind)] })}><Plus size={14} /> {KIND_LABELS[kind]}</button>)}
        </div>
      </section>

      <div className="settings-actions">
        <button type="button" className="button button-secondary" disabled={busy !== null} onClick={() => void generate('json')}><Eye size={15} /> {busy === 'preview' ? 'A calcular…' : 'Pré-visualizar'}</button>
        <button type="button" className="button button-primary" disabled={busy !== null} onClick={() => void generate('pdf')}><FileDown size={15} /> {busy === 'pdf' ? 'A gerar…' : 'Gerar PDF'}</button>
      </div>

      {preview && <section className="panel pack-preview">
        <div className="panel-heading"><h2>{preview.title} · {preview.period}</h2></div>
        {preview.sections.map((section, index) => <article key={index} className="pack-preview-section">
          <h3>{index + 1}. {section.title}</h3>
          {section.paragraphs.map((paragraph, position) => <p key={position}>{paragraph}</p>)}
          {section.table && <div className="table-wrap"><table className="map-table">
            <thead><tr>{section.table.header.map((cell, position) => <th key={position} className={section.table!.numeric[position] ? 'numeric' : ''}>{cell}</th>)}</tr></thead>
            <tbody>{section.table.rows.map((row, position) => <tr key={position} className={`${row.bold ? 'map-row-bold' : ''} ${row.isTitle ? 'map-row-title' : ''}`}>
              {row.cells.map((cell, column) => <Fragment key={column}><td className={section.table!.numeric[column] ? 'numeric' : ''} style={column === 0 ? { paddingLeft: 12 + row.level * 16 } : undefined}>{cell}</td></Fragment>)}
            </tr>)}</tbody>
          </table></div>}
          {section.notes?.map((note) => <p key={note} className="pack-note">{note}</p>)}
        </article>)}
      </section>}
    </>}
  </div>
}
