import { Database, Percent, Plus, Sparkles, X } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { type VatCodeRow } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { VAT_NATURES, VAT_RATES, VAT_REGIONS, vatNatureLabel, vatRateLabel, vatRegionLabel } from '../app/vatClassification'
import { useWorkContext } from '../app/WorkContext'

const percent = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 0, maximumFractionDigits: 2 })

function VatCodeEditor({ vatCode, onSaved, onClose }: { vatCode: VatCodeRow | null; onSaved: () => void; onClose: () => void }) {
  const { activeCompany, saveVatCode } = useWorkContext()
  const isCreate = vatCode === null
  const [code, setCode] = useState('')
  const [name, setName] = useState(vatCode?.name ?? '')
  const [rate, setRate] = useState(vatCode?.rate ?? 'Normal')
  const [percentage, setPercentage] = useState(vatCode ? String(vatCode.percentage) : '')
  const [nature, setNature] = useState(vatCode?.nature ?? 'TransmissoesInternas')
  const [region, setRegion] = useState(vatCode?.region ?? 'Continente')
  const [isActive, setIsActive] = useState(vatCode?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      await saveVatCode(activeCompany.companyId, {
        code: isCreate ? code : vatCode.code,
        name,
        rate,
        percentage: rate === 'Isento' ? 0 : Number(percentage.replace(',', '.')) || 0,
        nature,
        region,
        isActive,
      }, isCreate)
      onSaved()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o código de IVA.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Novo código de IVA' : `Editar ${vatCode.code}`}</h2><p>A percentagem fica gravada no código: se a taxa legal mudar, corrija-a aqui.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} maxLength={12} placeholder="ex.: V-NOR" autoFocus /></label>}
      <label><span>Descrição</span><input value={name} onChange={(event) => setName(event.target.value)} maxLength={120} /></label>
      <label><span>Taxa</span><select value={rate} onChange={(event) => setRate(event.target.value)}>{VAT_RATES.filter((item) => item.value !== 'Nenhuma').map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
      <label><span>Percentagem</span><input inputMode="decimal" value={rate === 'Isento' ? '0' : percentage} disabled={rate === 'Isento'} onChange={(event) => setPercentage(event.target.value)} placeholder="ex.: 23" /></label>
      <label><span>Natureza da operação</span><select value={nature} onChange={(event) => setNature(event.target.value)}>{VAT_NATURES.filter((item) => item.value !== 'Nenhuma').map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
      <label><span>Região</span><select value={region} onChange={(event) => setRegion(event.target.value)}>{VAT_REGIONS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Código ativo</strong><small>Desative para o retirar das novas seleções.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar código'}</button>
      </footer>
    </form>
  </section>
}

export function VatCodesPage() {
  const { activeCompany, source, vatCodes, createDefaultVatCodes } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'

  const [rows, setRows] = useState<VatCodeRow[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; vatCode: VatCodeRow } | null>(null)
  usePublishRecord(editor?.mode === 'edit' ? 'codigos-iva' : undefined, editor?.mode === 'edit' ? editor.vatCode.code : undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    setError('')
    void vatCodes(companyId, includeInactive)
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os códigos de IVA.') })
    return () => controller.abort()
  }, [companyId, onDatabase, vatCodes, includeInactive, reload])

  const createDefaults = async () => {
    setBusy(true); setError(''); setStatus('')
    try {
      const created = await createDefaultVatCodes(companyId)
      setStatus(created === 0 ? 'Os códigos por omissão já existem.' : `${created} ${created === 1 ? 'código criado' : 'códigos criados'}.`)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível criar os códigos por omissão.')
    } finally {
      setBusy(false)
    }
  }

  const saved = () => { setEditor(null); setReload((value) => value + 1) }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Códigos de IVA</h1><p>Taxa, percentagem, natureza e região num código para indicar na linha do lançamento.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Percent /></span><h2>Requer ligação ao SQL Server</h2><p>Os códigos de IVA são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Códigos de IVA</h1><p>Cada código junta a taxa, a percentagem, a natureza da operação e a região. Indica-se na linha do lançamento e é o que permite desagregar o IVA por documento.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}
    {status && !error && <p className="save-status success">{status}</p>}

    <div className="filterbar">
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} /> Incluir inativos</label>
      <span className="catalog-count">{rows.length} {rows.length === 1 ? 'código' : 'códigos'}</span>
      <button className="button button-secondary" type="button" disabled={busy} onClick={() => void createDefaults()}><Sparkles size={16} /> {busy ? 'A criar…' : 'Criar códigos por omissão'}</button>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo código</button>
    </div>

    {editor?.mode === 'create' && <VatCodeEditor vatCode={null} onSaved={saved} onClose={() => setEditor(null)} />}
    {editor?.mode === 'edit' && <VatCodeEditor key={editor.vatCode.code} vatCode={editor.vatCode} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Código</th><th>Descrição</th><th>Taxa</th><th className="numeric">%</th><th>Natureza</th><th>Região</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.code}>
          <td><span className="primary-cell">{row.code}</span></td>
          <td>{row.name}</td>
          <td>{vatRateLabel(row.rate)}</td>
          <td className="numeric">{percent.format(row.percentage)}</td>
          <td>{vatNatureLabel(row.nature)}</td>
          <td>{vatRegionLabel(row.region)}</td>
          <td><span className={`badge ${row.isActive ? 'success' : 'warning'}`}>{row.isActive ? 'Ativo' : 'Inativo'}</span></td>
          <td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', vatCode: row })}>Editar</button></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Ainda não há códigos de IVA. «Criar códigos por omissão» propõe os mais comuns para a região da empresa, com as percentagens em vigor.</p>}
    </section>
  </div>
}
