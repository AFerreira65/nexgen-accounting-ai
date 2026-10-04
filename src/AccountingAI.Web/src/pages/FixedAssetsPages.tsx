import { CircleAlert, CircleCheck, Database, Download, Factory, Plus, Printer, X } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type DepreciationSchedule, type FixedAssetRow } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const shortDate = (value: string | null) => (value ? value.split('-').reverse().join('/') : '—')
const percent = new Intl.NumberFormat('pt-PT', { maximumFractionDigits: 4 })
const today = () => new Date().toISOString().slice(0, 10)

function FixedAssetEditor({ asset, onSaved, onClose }: { asset: FixedAssetRow | null; onSaved: () => void; onClose: () => void }) {
  const { activeCompany, saveFixedAsset } = useWorkContext()
  const isCreate = asset === null
  const [code, setCode] = useState('')
  const [description, setDescription] = useState(asset?.description ?? '')
  const [acquisitionDate, setAcquisitionDate] = useState(asset?.acquisitionDate ?? today())
  const [startOfUseDate, setStartOfUseDate] = useState(asset?.startOfUseDate ?? today())
  const [acquisitionValue, setAcquisitionValue] = useState(asset ? String(asset.acquisitionValue) : '')
  const [residualValue, setResidualValue] = useState(asset ? String(asset.residualValue) : '0')
  const [annualRate, setAnnualRate] = useState(asset ? String(asset.annualRate) : '')
  const [byMonths, setByMonths] = useState(asset?.byMonths ?? false)
  const [assetAccountCode, setAssetAccountCode] = useState(asset?.assetAccountCode ?? '')
  const [accumulatedAccountCode, setAccumulatedAccountCode] = useState(asset?.accumulatedAccountCode ?? '')
  const [expenseAccountCode, setExpenseAccountCode] = useState(asset?.expenseAccountCode ?? '')
  const [disposalDate, setDisposalDate] = useState(asset?.disposalDate ?? '')
  const [isActive, setIsActive] = useState(asset?.isActive ?? true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'error'>('idle')
  const [error, setError] = useState('')
  const number = (value: string) => Number(value.replace(',', '.'))

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    setError('')
    try {
      await saveFixedAsset(activeCompany.companyId, {
        code: isCreate ? code : asset.code,
        description,
        acquisitionDate,
        startOfUseDate,
        acquisitionValue: number(acquisitionValue),
        residualValue: number(residualValue || '0'),
        annualRate: number(annualRate),
        byMonths,
        assetAccountCode: assetAccountCode.trim() || null,
        accumulatedAccountCode: accumulatedAccountCode.trim() || null,
        expenseAccountCode: expenseAccountCode.trim() || null,
        disposalDate: disposalDate || null,
        isActive,
      }, isCreate)
      onSaved()
    } catch (submitError) {
      setStatus('error')
      setError(submitError instanceof Error ? submitError.message : 'Não foi possível guardar o ativo.')
    }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading">
      <div><h2>{isCreate ? 'Novo ativo fixo' : `Editar ${asset.code}`}</h2><p>A taxa anual é a que resulta das tabelas do Decreto Regulamentar n.º 25/2009 ou da vida útil que a empresa definiu.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={18} /></button>
    </div>
    <form className="settings-grid" onSubmit={submit}>
      {isCreate && <label><span>Código</span><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} maxLength={20} placeholder="ex.: EQ-001" autoFocus /></label>}
      <label><span>Descrição</span><input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={200} /></label>
      <label><span>Data de aquisição</span><input type="date" value={acquisitionDate} onChange={(event) => setAcquisitionDate(event.target.value)} /></label>
      <label><span>Entrada em funcionamento</span><input type="date" value={startOfUseDate} onChange={(event) => setStartOfUseDate(event.target.value)} /></label>
      <label><span>Valor de aquisição</span><input inputMode="decimal" value={acquisitionValue} onChange={(event) => setAcquisitionValue(event.target.value)} /></label>
      <label><span>Valor residual</span><input inputMode="decimal" value={residualValue} onChange={(event) => setResidualValue(event.target.value)} /></label>
      <label><span>Taxa anual (%)</span><input inputMode="decimal" value={annualRate} onChange={(event) => setAnnualRate(event.target.value)} placeholder="ex.: 25" /></label>
      <label className="toggle-field"><input type="checkbox" checked={byMonths} onChange={(event) => setByMonths(event.target.checked)} /><span><strong>Por duodécimos</strong><small>No ano de início conta a partir do mês de entrada em funcionamento; sem esta opção leva a quota anual inteira.</small></span></label>
      <label><span>Conta do ativo</span><input value={assetAccountCode} onChange={(event) => setAssetAccountCode(event.target.value)} placeholder="Opcional — ex.: 433" /></label>
      <label><span>Conta das depreciações acumuladas</span><input value={accumulatedAccountCode} onChange={(event) => setAccumulatedAccountCode(event.target.value)} placeholder="Opcional — ex.: 4383" /></label>
      <label><span>Conta do gasto de depreciação</span><input value={expenseAccountCode} onChange={(event) => setExpenseAccountCode(event.target.value)} placeholder="Opcional — ex.: 6423" /></label>
      <label><span>Data de alienação ou abate</span><input type="date" value={disposalDate} onChange={(event) => setDisposalDate(event.target.value)} /></label>
      {!isCreate && <label className="toggle-field"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /><span><strong>Ativo no cadastro</strong><small>Um ativo inativo deixa de aparecer no mapa de depreciações.</small></span></label>}
      <footer className="settings-actions">
        {status === 'error' && <span className="save-status error">{error}</span>}
        <button className="button button-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="button button-primary" type="submit" disabled={status === 'saving'}>{status === 'saving' ? 'A guardar…' : 'Guardar ativo'}</button>
      </footer>
    </form>
  </section>
}

export function FixedAssetsPage() {
  const { activeCompany, source, fixedAssets } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const [rows, setRows] = useState<FixedAssetRow[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ mode: 'create' } | { mode: 'edit'; asset: FixedAssetRow } | null>(null)
  usePublishRecord(editor?.mode === 'edit' ? 'ativos-fixos' : undefined, editor?.mode === 'edit' ? editor.asset.code : undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    setError('')
    void fixedAssets(companyId, includeInactive)
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar os ativos fixos.') })
    return () => controller.abort()
  }, [companyId, onDatabase, fixedAssets, includeInactive, reload])

  const saved = () => { setEditor(null); setReload((value) => value + 1) }

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Ativos fixos</h1><p>O cadastro dos ativos fixos tangíveis e intangíveis e os dados da sua depreciação.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Factory /></span><h2>Requer ligação ao SQL Server</h2><p>Os ativos fixos são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Ativos fixos</h1><p>O cadastro dos ativos fixos tangíveis e intangíveis. As depreciações de cada exercício estão no <Link to="/mapa-depreciacoes">mapa de depreciações</Link>.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} /> Mostrar inativos</label>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo ativo</button>
    </div>

    {editor && <FixedAssetEditor key={editor.mode === 'edit' ? editor.asset.code : 'novo'} asset={editor.mode === 'edit' ? editor.asset : null} onSaved={saved} onClose={() => setEditor(null)} />}

    <section className="panel">
      <div className="table-wrap"><table>
        <thead><tr><th>Código</th><th>Descrição</th><th>Em funcionamento</th><th className="numeric">Valor de aquisição</th><th className="numeric">Taxa</th><th>Contas</th><th>Estado</th><th /></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.code}>
          <td className="primary-cell">{row.code}</td>
          <td>{row.description}</td>
          <td>{shortDate(row.startOfUseDate)}</td>
          <td className="numeric">{euro.format(row.acquisitionValue)}</td>
          <td className="numeric">{percent.format(row.annualRate)} %{row.byMonths ? ' · duodécimos' : ''}</td>
          <td>{[row.assetAccountCode, row.accumulatedAccountCode, row.expenseAccountCode].filter(Boolean).join(' · ') || '—'}</td>
          <td><span className={`badge ${row.disposalDate ? 'warning' : row.isActive ? 'success' : 'warning'}`}>{row.disposalDate ? `Alienado em ${shortDate(row.disposalDate)}` : row.isActive ? 'Ativo' : 'Inativo'}</span></td>
          <td><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', asset: row })}>Editar</button></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Ainda não há ativos no cadastro. Registe cada ativo com o valor de aquisição, a data de entrada em funcionamento e a taxa anual.</p>}
    </section>
  </div>
}

export function DepreciationSchedulePage() {
  const { activeCompany, source, depreciationSchedule } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [schedule, setSchedule] = useState<DepreciationSchedule | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setSchedule(null); return }
    const controller = new AbortController()
    setError('')
    void depreciationSchedule(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setSchedule(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o mapa de depreciações.') })
    return () => controller.abort()
  }, [depreciationSchedule, companyId, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa de depreciações</h1><p>As depreciações de cada ativo num exercício, por quotas constantes.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Factory /></span><h2>Requer ligação ao SQL Server</h2><p>O mapa é calculado a partir do cadastro de ativos fixos da empresa.</p></section>
    </div>
  }

  const rows = schedule?.rows ?? []
  const exportCsv = () => downloadCsv(
    csvFilename(`mapa-depreciacoes-${year}`, activeCompany.legalName),
    toCsv([
      ['Código', 'Descrição', 'Em funcionamento', 'Alienação', 'Valor de aquisição', 'Taxa (%)', 'Acumuladas no início', 'Depreciação do exercício', 'Acumuladas no fim', 'Valor líquido', 'Conta do gasto'],
      ...rows.map((row) => [row.code, row.description, row.startOfUseDate, row.disposalDate ?? '', row.acquisitionValue, row.annualRate, row.accumulatedAtStart, row.depreciation, row.accumulatedAtEnd, row.netValue, row.expenseAccountCode ?? '']),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa de depreciações</h1><p>As depreciações de cada ativo do <Link to="/ativos-fixos">cadastro</Link> no exercício, por quotas constantes. É um cálculo: não lança nada na contabilidade.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const value = Number(event.target.value); if (value >= 2000 && value <= 9999) setYear(value) }} /></label>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={rows.length === 0}><Download size={16} /> Exportar CSV</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={rows.length === 0}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {schedule && <>
      <section className="metadata-strip">
        <span><small>Valor de aquisição</small><strong>{euro.format(schedule.totalAcquisitionValue)}</strong></span>
        <span><small>Depreciações de {year}</small><strong>{euro.format(schedule.totalDepreciation)}</strong></span>
        <span><small>Acumuladas no fim de {year}</small><strong>{euro.format(schedule.totalAccumulatedAtEnd)}</strong></span>
        <span><small>Valor líquido</small><strong>{euro.format(schedule.totalNetValue)}</strong></span>
      </section>

      {schedule.difference === 0
        ? <p className="save-status success"><CircleCheck size={15} /> As depreciações do mapa coincidem com os gastos lançados nas contas 64 ({euro.format(schedule.ledgerDepreciation)}).</p>
        : <p className="save-status error"><CircleAlert size={15} /> Na contabilidade estão lançados {euro.format(schedule.ledgerDepreciation)} de gastos de depreciação (contas 64); o mapa calcula {euro.format(schedule.totalDepreciation)}. Diferença: {euro.format(schedule.difference)}.</p>}

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>Código</th><th>Descrição</th><th>Em funcionamento</th><th className="numeric">Valor de aquisição</th><th className="numeric">Taxa</th><th className="numeric">Acumuladas no início</th><th className="numeric">Depreciação de {year}</th><th className="numeric">Acumuladas no fim</th><th className="numeric">Valor líquido</th></tr></thead>
          <tbody>
            {rows.map((row) => <tr key={row.code}>
              <td className="primary-cell">{row.code}</td>
              <td>{row.description}{row.disposalDate ? ` (alienado em ${shortDate(row.disposalDate)})` : ''}</td>
              <td>{shortDate(row.startOfUseDate)}</td>
              <td className="numeric">{euro.format(row.acquisitionValue)}</td>
              <td className="numeric">{percent.format(row.annualRate)} %</td>
              <td className="numeric">{euro.format(row.accumulatedAtStart)}</td>
              <td className="numeric strong">{euro.format(row.depreciation)}</td>
              <td className="numeric">{euro.format(row.accumulatedAtEnd)}</td>
              <td className="numeric">{euro.format(row.netValue)}</td>
            </tr>)}
            {rows.length > 0 && <tr className="subtotal-row">
              <td colSpan={3}>Total</td>
              <td className="numeric">{euro.format(schedule.totalAcquisitionValue)}</td>
              <td />
              <td className="numeric">{euro.format(schedule.totalAccumulatedAtEnd - schedule.totalDepreciation)}</td>
              <td className="numeric">{euro.format(schedule.totalDepreciation)}</td>
              <td className="numeric">{euro.format(schedule.totalAccumulatedAtEnd)}</td>
              <td className="numeric">{euro.format(schedule.totalNetValue)}</td>
            </tr>}
          </tbody>
        </table></div>
        {rows.length === 0 && <p className="empty-state">Sem ativos em funcionamento em {year}. Registe-os no <Link to="/ativos-fixos">cadastro de ativos fixos</Link>.</p>}
      </section>

      <p className="statement-note">Quotas constantes sobre o valor de aquisição menos o valor residual. Sem duodécimos, o ano de entrada em funcionamento leva a quota anual inteira e o da alienação nenhuma; com duodécimos contam os meses desde o de entrada em funcionamento e até ao anterior ao da alienação. Não é o mapa fiscal modelo 32: não separa as depreciações não aceites fiscalmente.</p>
    </>}
  </div>
}
