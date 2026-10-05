import { CircleAlert, CircleCheck, Database, Download, Factory, Plus, Printer, X } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type AssetDisposalPreview, type DepreciationSchedule, type FixedAssetRow } from '../app/demo-data'
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
  const [taxTableCode, setTaxTableCode] = useState(asset?.taxTableCode ?? '')
  const [taxRate, setTaxRate] = useState(asset?.taxRate != null ? String(asset.taxRate) : '')
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
        taxTableCode: taxTableCode.trim() || null,
        taxRate: taxRate.trim() ? number(taxRate) : null,
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
      <label><span>Código no DR n.º 25/2009</span><input value={taxTableCode} onChange={(event) => setTaxTableCode(event.target.value.replace(/\D/g, '').slice(0, 4))} placeholder="Opcional — quatro dígitos" inputMode="numeric" /></label>
      <label><span>Taxa máxima fiscal (%)</span><input inputMode="decimal" value={taxRate} onChange={(event) => setTaxRate(event.target.value)} placeholder="Opcional — a das tabelas do DR n.º 25/2009" /></label>
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
  const [disposing, setDisposing] = useState<FixedAssetRow | null>(null)
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
      <section className="panel empty-state"><span className="empty-icon"><Factory /></span><h2>Requer ligação à base de dados</h2><p>Os ativos fixos são lidos da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Ativos fixos</h1><p>O cadastro dos ativos fixos tangíveis e intangíveis. As depreciações de cada exercício estão no <Link to="/mapa-depreciacoes">mapa de depreciações</Link>.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} /> Mostrar inativos</label>
      <button className="button button-primary" type="button" onClick={() => setEditor({ mode: 'create' })}><Plus size={16} /> Novo ativo</button>
    </div>

    {disposing && <AssetDisposalPanel key={disposing.code} asset={disposing} onDone={() => { setDisposing(null); setReload((value) => value + 1) }} onClose={() => setDisposing(null)} />}

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
          <td><span className="inline-check"><button className="button button-secondary" type="button" onClick={() => setEditor({ mode: 'edit', asset: row })}>Editar</button>{!row.disposalDate && <button className="text-link" type="button" onClick={() => setDisposing(row)}>Alienar / abater</button>}</span></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Ainda não há ativos no cadastro. Registe cada ativo com o valor de aquisição, a data de entrada em funcionamento e a taxa anual.</p>}
    </section>
  </div>
}

export function DepreciationSchedulePage() {
  const { activeCompany, source, depreciationSchedule, postDepreciation } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [schedule, setSchedule] = useState<DepreciationSchedule | null>(null)
  const [error, setError] = useState('')
  const [journal, setJournal] = useState('')
  const [posting, setPosting] = useState(false)
  const [posted, setPosted] = useState('')
  const [reload, setReload] = useState(0)
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])
  useEffect(() => { setPosted('') }, [year, companyId])

  useEffect(() => {
    if (!onDatabase) { setSchedule(null); return }
    const controller = new AbortController()
    setError('')
    void depreciationSchedule(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setSchedule(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível calcular o mapa de depreciações.') })
    return () => controller.abort()
  }, [depreciationSchedule, companyId, onDatabase, year, reload])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa de depreciações</h1><p>As depreciações de cada ativo num exercício, por quotas constantes.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Factory /></span><h2>Requer ligação à base de dados</h2><p>O mapa é calculado a partir do cadastro de ativos fixos da empresa.</p></section>
    </div>
  }

  const rows = schedule?.rows ?? []
  async function post(allowExisting: boolean) {
    setPosting(true)
    setError('')
    setPosted('')
    try {
      const movement = await postDepreciation(companyId, { year, journal: journal.trim(), allowExisting })
      setPosted(movement.reference)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o lançamento.')
    } finally {
      setPosting(false)
    }
  }
  const exportCsv = () => downloadCsv(
    csvFilename(`mapa-depreciacoes-${year}`, activeCompany.legalName),
    toCsv([
      ['Código', 'Descrição', 'Em funcionamento', 'Alienação', 'Valor de aquisição', 'Taxa (%)', 'Acumuladas no início', 'Depreciação do exercício', 'Acumuladas no fim', 'Valor líquido', 'Conta do gasto'],
      ...rows.map((row) => [row.code, row.description, row.startOfUseDate, row.disposalDate ?? '', row.acquisitionValue, row.annualRate, row.accumulatedAtStart, row.depreciation, row.accumulatedAtEnd, row.netValue, row.expenseAccountCode ?? '']),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapa de depreciações</h1><p>As depreciações de cada ativo do <Link to="/ativos-fixos">cadastro</Link> no exercício, por quotas constantes. O mapa é um cálculo; o lançamento só é gerado quando o pedir, e fica por conferir.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

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

      {rows.length > 0 && <section className="panel settings-panel">
        <div className="panel-heading"><div><h2>Lançar as depreciações de {year}</h2><p>Um lançamento a 31 de dezembro, com o gasto a débito e as depreciações acumuladas a crédito, somados por conta. Fica por conferir nos movimentos.</p></div></div>
        <div className="filterbar">
          <label className="inline-check">Diário <input value={journal} onChange={(event) => setJournal(event.target.value.toUpperCase())} placeholder="código do diário" aria-label="Diário" /></label>
          {schedule.ledgerDepreciation === 0
            ? <button className="button button-primary" type="button" disabled={posting || journal.trim() === ''} onClick={() => void post(false)}>{posting ? 'A lançar…' : 'Gerar lançamento'}</button>
            : <button className="button button-secondary" type="button" disabled={posting || journal.trim() === ''} onClick={() => void post(true)}>{posting ? 'A lançar…' : `Gerar mesmo com ${euro.format(schedule.ledgerDepreciation)} já lançados`}</button>}
          {posted && <span className="save-status success"><CircleCheck size={15} /> Lançamento {posted} criado, por conferir.</span>}
        </div>
      </section>}

      <p className="statement-note">Quotas constantes sobre o valor de aquisição menos o valor residual. Sem duodécimos, o ano de entrada em funcionamento leva a quota anual inteira e o da alienação nenhuma; com duodécimos contam os meses desde o de entrada em funcionamento e até ao anterior ao da alienação. Não é o mapa fiscal modelo 32: não separa as depreciações não aceites fiscalmente.</p>
    </>}
  </div>
}

/** A saída de um ativo: a pré-visualização do ganho ou da perda e o lançamento, por conferir. */
function AssetDisposalPanel({ asset, onDone, onClose }: { asset: FixedAssetRow; onDone: () => void; onClose: () => void }) {
  const { activeCompany, previewAssetDisposal, postAssetDisposal } = useWorkContext()
  const companyId = activeCompany.companyId
  const [date, setDate] = useState(today())
  const [price, setPrice] = useState('0')
  const [counterpart, setCounterpart] = useState('')
  const [journal, setJournal] = useState('')
  const [document, setDocument] = useState('')
  const [coefficient, setCoefficient] = useState('')
  const [preview, setPreview] = useState<AssetDisposalPreview | null>(null)
  const [posted, setPosted] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const input = () => ({ date, salePrice: Number(price.replace(/\s/g, '').replace(',', '.')) || 0, counterpartAccountCode: counterpart.trim() || null, journalCode: journal.trim(), documentReference: document.trim() || null, monetaryCoefficient: coefficient.trim() ? Number(coefficient.replace(',', '.')) : null })
  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setError('')
    try { await action() } catch (problem) { setError(problem instanceof Error ? problem.message : 'Não foi possível concluir.') } finally { setBusy(false) }
  }

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>Saída do ativo {asset.code}</h2><p>{asset.description}. Com preço, é uma alienação; sem preço, um abate. As depreciações contam até ao mês anterior ao da saída; o ganho vai para a 7871 e a perda para a 6871 (alienação) ou 6873 (abate).</p></div><button className="text-link" type="button" onClick={onClose}>Fechar</button></div>
    <div className="filterbar">
      <label className="inline-check">Data <input type="date" aria-label="Data da saída" value={date} onChange={(event) => { setDate(event.target.value); setPreview(null) }} /></label>
      <label className="inline-check">Preço sem IVA <input aria-label="Preço de venda" inputMode="decimal" value={price} onChange={(event) => { setPrice(event.target.value); setPreview(null) }} style={{ width: '8em' }} /></label>
      <label className="inline-check">Conta do preço <input aria-label="Conta que recebe o preço" value={counterpart} onChange={(event) => { setCounterpart(event.target.value); setPreview(null) }} placeholder="ex.: 2781 ou 12" style={{ width: '8em' }} /></label>
      <label className="inline-check">Diário <input aria-label="Diário" value={journal} onChange={(event) => setJournal(event.target.value.toUpperCase())} placeholder="código" style={{ width: '7em' }} /></label>
      <label className="inline-check">Documento <input aria-label="Documento" value={document} onChange={(event) => setDocument(event.target.value)} placeholder="opcional" style={{ width: '9em' }} /></label>
      <label className="inline-check" title="Artigo 47.º do CIRC: o coeficiente da portaria do ano, para o Modelo 31">Coef. desvalorização <input aria-label="Coeficiente de desvalorização da moeda" inputMode="decimal" value={coefficient} onChange={(event) => setCoefficient(event.target.value)} placeholder="1,00" style={{ width: '6em' }} /></label>
      <button className="button button-secondary" type="button" disabled={busy || !date} onClick={() => void run(async () => setPreview(await previewAssetDisposal(companyId, asset.code, input())))}>Calcular</button>
    </div>
    {error && <p className="save-status error">{error}</p>}
    {posted && <p className="save-status">Lançado por conferir: <Link to={`/movimentos/${posted}`}>{posted}</Link>.</p>}
    {preview && <>
      <div className="metadata-strip">
        <span><small>Valor de aquisição</small><strong>{euro.format(preview.acquisitionValue)}</strong></span>
        <span><small>Depreciações acumuladas</small><strong>{euro.format(preview.accumulated)}</strong></span>
        <span><small>Valor líquido</small><strong>{euro.format(preview.netBookValue)}</strong></span>
        <span><small>{preview.result >= 0 ? 'Ganho' : 'Perda'}</small><strong>{euro.format(Math.abs(preview.result))}</strong><small>{preview.kind}</small></span>
      </div>
      {preview.warning && <p className="save-status error">{preview.warning}</p>}
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead>
        <tbody>{preview.lines.map((line) => <tr key={line.accountCode}><td>{line.accountCode} {line.accountName}</td><td className="numeric">{line.debit ? euro.format(line.debit) : ''}</td><td className="numeric">{line.credit ? euro.format(line.credit) : ''}</td></tr>)}</tbody>
      </table></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <button className="button button-primary" type="button" disabled={busy || !journal.trim() || posted !== ''} onClick={() => void run(async () => { const result = await postAssetDisposal(companyId, asset.code, input()); setPosted(result.movement.id); onDone() })}>Lançar por conferir</button>
        {!journal.trim() && <small>Indique o diário para lançar.</small>}
      </div>
    </>}
  </section>
}
