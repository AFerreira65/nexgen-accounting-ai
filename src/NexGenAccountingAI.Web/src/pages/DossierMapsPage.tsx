import { CircleAlert, Database, Download, Printer, Table2 } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, type DossierMaps } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const rate = (value: number | null) => (value === null ? '' : `${value.toLocaleString('pt-PT', { maximumFractionDigits: 4 })}`)
const money = (value: number) => (value === 0 ? '' : euro.format(value))

export function DossierMapsPage() {
  const { activeCompany, source, dossierMaps, setMonetaryCoefficient } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [maps, setMaps] = useState<DossierMaps | null>(null)
  const [coefficients, setCoefficients] = useState<Record<string, string>>({})
  const [reload, setReload] = useState(0)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])
  useEffect(() => {
    if (!onDatabase) { setMaps(null); return }
    const controller = new AbortController()
    setError('')
    void dossierMaps(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setMaps(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setMaps(null); setError(problem instanceof Error ? problem.message : 'Não foi possível montar os mapas.') } })
    return () => controller.abort()
  }, [companyId, dossierMaps, onDatabase, reload, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Mapas do dossier fiscal</h1><p>O mapa de depreciações e amortizações (Modelo 32) e o mapa de mais-valias e menos-valias (Modelo 31), a partir do cadastro dos ativos fixos.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Table2 /></span><h2>Requer ligação à base de dados</h2><p>Os mapas saem do cadastro dos ativos fixos da empresa.</p></section>
    </div>
  }

  const saveCoefficient = async (code: string) => {
    const text = (coefficients[code] ?? '').trim()
    try {
      await setMonetaryCoefficient(companyId, code, text ? Number(text.replace(',', '.')) : null)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar o coeficiente.')
    }
  }

  const exportCsv = () => {
    if (!maps) return
    downloadCsv(csvFilename(`mapas-31-32-${year}`, activeCompany.legalName), toCsv([
      ['Modelo', 'Natureza', 'Ativo', '(1) Código DR 25/2009', '(2) Descrição', '(3) Mês', '(4) Ano', '(5) Valor contabilístico', '(6) Valor fiscal', '(8) Contabilizadas no período', '(9) Aceites em períodos anteriores', '(10) Taxa %', '(12) Limite fiscal', '(13) Imparidades aceites', '(15) Não aceites', '(16) Recuperadas'],
      ...maps.modelo32.flatMap((map) => map.lines.map((line) => ['32', map.natureLabel, line.assetCode, line.taxTableCode ?? '', line.description, line.startMonth ?? '', line.startYear, line.bookValue, line.taxValue, line.bookedInPeriod, line.acceptedInPriorPeriods, line.rate, line.taxLimit, line.acceptedImpairment, line.notAccepted, line.recovered])),
      [],
      ['Modelo', 'Natureza', 'Ativo', '(1) Descrição', '(2) Valor de realização', '(3) Valor de aquisição fiscal', '(4) Ano de aquisição', '(5) Valor de aquisição contabilístico', '(6) Depreciações registadas', '(8) Mais/menos-valia contabilística', '(9) Mais-valia não tributada', '(10) Depreciações aceites', '(11) Coeficiente', '(13) Mais/menos-valia fiscal'],
      ...maps.modelo31.flatMap((map) => map.lines.map((line) => ['31', map.natureLabel, line.assetCode, line.description, line.realisationValue, line.taxAcquisitionValue, line.acquisitionYear, line.bookAcquisitionValue, line.bookDepreciation, line.bookGain, line.untaxedGain, line.acceptedDepreciation, line.coefficient, line.taxGain])),
    ]))
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Mapas do dossier fiscal</h1><p>O Modelo 32 (depreciações e amortizações) e o Modelo 31 (mais-valias e menos-valias) do exercício, com as colunas dos impressos oficiais, a partir dos <Link to="/ativos-fixos">ativos fixos</Link>. Fazem parte do <Link to="/dossier-fiscal">dossier fiscal</Link>.</p></div>
      <div className="heading-actions no-print">
        <button className="button button-secondary" type="button" onClick={exportCsv} disabled={!maps}><Download size={16} /> CSV</button>
        <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!maps}><Printer size={16} /> Imprimir / PDF</button>
      </div>
    </header>

    <div className="filterbar no-print">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
    </div>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {maps?.warnings.map((warning) => <p key={warning} className="save-status"><CircleAlert size={15} /> {warning}</p>)}

    {maps && <section className="metadata-strip">
      <span><small>Depreciações do mapa (8)</small><strong>{euro.format(maps.modelo32Booked)}</strong><small>contas 64: {euro.format(maps.ledgerDepreciation)}</small></span>
      <span><small>Não aceites (15) — a acrescer no quadro 07</small><strong>{euro.format(maps.modelo32NotAccepted)}</strong></span>
      <span><small>Mais/menos-valias contabilísticas (8)</small><strong>{euro.format(maps.modelo31BookGain)}</strong></span>
      <span><small>Mais/menos-valias fiscais (13)</small><strong>{euro.format(maps.modelo31TaxGain)}</strong></span>
    </section>}

    {maps && maps.modelo32.map((map) => <section key={`m32-${map.nature}`} className="panel">
      <div className="panel-heading"><div><h2>Modelo 32 — {map.natureLabel}</h2><p>Método: quotas constantes.{maps.fullyDepreciatedOmitted > 0 ? ` ${maps.fullyDepreciatedOmitted} ativos totalmente depreciados não incluídos (alínea a) das instruções).` : ''}</p></div></div>
      <div className="table-wrap"><table className="dense-table">
        <thead><tr><th>(1)</th><th>(2) Descrição</th><th>(3) Mês</th><th>(4) Ano</th><th className="numeric">(5) Valor contab.</th><th className="numeric">(6) Valor fiscal</th><th className="numeric">(8) Contabilizadas</th><th className="numeric">(9) Aceites anteriores</th><th className="numeric">(10) Taxa %</th><th className="numeric">(12) Limite fiscal</th><th className="numeric">(13) Imparidades</th><th className="numeric">(15) Não aceites</th><th className="numeric">(16) Recuperadas</th></tr></thead>
        <tbody>
          {map.lines.map((line) => <tr key={line.assetCode}>
            <td>{line.taxTableCode ?? '—'}</td>
            <td>{line.description}<small>{line.assetCode}</small></td>
            <td>{line.startMonth ?? ''}</td>
            <td>{line.startYear}</td>
            <td className="numeric">{euro.format(line.bookValue)}</td>
            <td className="numeric">{euro.format(line.taxValue)}</td>
            <td className="numeric">{money(line.bookedInPeriod)}</td>
            <td className="numeric">{money(line.acceptedInPriorPeriods)}</td>
            <td className="numeric">{rate(line.rate)}{line.rateFromAccounting && <small>contabilística</small>}</td>
            <td className="numeric">{money(line.taxLimit)}</td>
            <td className="numeric">{money(line.acceptedImpairment)}</td>
            <td className="numeric strong">{money(line.notAccepted)}</td>
            <td className="numeric">{money(line.recovered)}</td>
          </tr>)}
        </tbody>
        <tfoot><tr><td colSpan={4}>Total</td><td className="numeric">{euro.format(map.bookValue)}</td><td className="numeric">{euro.format(map.taxValue)}</td><td className="numeric">{euro.format(map.bookedInPeriod)}</td><td className="numeric">{euro.format(map.acceptedInPriorPeriods)}</td><td /><td className="numeric">{euro.format(map.taxLimit)}</td><td className="numeric">{euro.format(map.acceptedImpairment)}</td><td className="numeric">{euro.format(map.notAccepted)}</td><td className="numeric">{euro.format(map.recovered)}</td></tr></tfoot>
      </table></div>
    </section>)}

    {maps && maps.modelo32.length === 0 && <section className="panel empty-state"><h2>Sem ativos a depreciar em {year}</h2><p>Registe os ativos em <Link to="/ativos-fixos">Ativos fixos</Link>.</p></section>}

    {maps && maps.modelo31.map((map) => <section key={`m31-${map.nature}`} className="panel">
      <div className="panel-heading"><div><h2>Modelo 31 — {map.natureLabel}</h2><p>As alienações e os abates lançados pela aplicação no exercício.</p></div></div>
      <div className="table-wrap"><table className="dense-table">
        <thead><tr><th>(1) Descrição</th><th className="numeric">(2) Realização</th><th className="numeric">(3) Aquisição fiscal</th><th>(4) Ano</th><th className="numeric">(5) Aquisição contab.</th><th className="numeric">(6) Depreciações registadas</th><th className="numeric">(8) Contabilística</th><th className="numeric">(9) Não tributada</th><th className="numeric">(10) Depreciações aceites</th><th className="numeric">(11) Coeficiente</th><th className="numeric">(13) Fiscal</th></tr></thead>
        <tbody>{map.lines.map((line) => <Fragment key={line.assetCode}><tr>
          <td>{line.description}<small>{line.assetCode} · {line.disposalDate.split('-').reverse().join('/')}</small></td>
          <td className="numeric">{euro.format(line.realisationValue)}</td>
          <td className="numeric">{euro.format(line.taxAcquisitionValue)}</td>
          <td>{line.acquisitionYear}</td>
          <td className="numeric">{euro.format(line.bookAcquisitionValue)}</td>
          <td className="numeric">{euro.format(line.bookDepreciation)}</td>
          <td className="numeric">{euro.format(line.bookGain)}</td>
          <td className="numeric">{money(line.untaxedGain)}</td>
          <td className="numeric">{euro.format(line.acceptedDepreciation)}</td>
          <td className="numeric"><span className="inline-check no-print"><input aria-label={`Coeficiente de ${line.assetCode}`} inputMode="decimal" style={{ width: '5em' }} value={coefficients[line.assetCode] ?? (line.coefficientIndicated ? String(line.coefficient).replace('.', ',') : '')} placeholder="1,00" onChange={(event) => setCoefficients((current) => ({ ...current, [line.assetCode]: event.target.value }))} onBlur={() => void saveCoefficient(line.assetCode)} /></span><span className="print-only">{rate(line.coefficient)}</span></td>
          <td className="numeric strong">{euro.format(line.taxGain)}</td>
        </tr></Fragment>)}</tbody>
        <tfoot><tr><td colSpan={6}>Saldo</td><td className="numeric">{euro.format(map.bookGain)}</td><td colSpan={3} /><td className="numeric">{euro.format(map.taxGain)}</td></tr></tfoot>
      </table></div>
    </section>)}

    {maps && <p className="statement-note">
      Modelo 32: coluna (8) é a depreciação calculada do exercício (a que o mapa de depreciações lança); (12) o limite pela taxa máxima fiscal indicada na ficha do ativo sobre o valor de aquisição; (15) = (8) − [(12) + (13)]; (9) o aceite nos exercícios anteriores. As perdas por imparidade (13), as taxas perdidas (14), a recuperação (16), a vida útil esperada (7) e as quotas decrescentes não são calculadas. Modelo 31: (8) = (2) − [(5) − (6)]; (13) = (2) − [((3) − (9) − (10)) × (11)]. Pelas instruções, um saldo negativo da coluna (8) acresce-se e um positivo deduz-se no quadro 07 da declaração Modelo 22, onde entram também as mais e menos-valias fiscais (13). Fontes: {maps.sources.join(' ')}
    </p>}
  </div>
}
