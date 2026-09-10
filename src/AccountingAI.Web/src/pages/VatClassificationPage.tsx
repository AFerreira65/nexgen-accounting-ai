import { Database, Percent, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { type VatClassificationRow } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { VAT_NATURES, VAT_RATES, VAT_REGIONS } from '../app/vatClassification'
import { useWorkContext } from '../app/WorkContext'

export function VatClassificationPage() {
  const { activeCompany, source, vatClassification, setVatClassification, bulkVatClassification } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  usePublishRecord(undefined, undefined)

  const [rows, setRows] = useState<VatClassificationRow[]>([])
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [reload, setReload] = useState(0)

  const [bulkPrefix, setBulkPrefix] = useState('')
  const [bulkRate, setBulkRate] = useState('Nenhuma')
  const [bulkNature, setBulkNature] = useState('Nenhuma')
  const [bulkRegion, setBulkRegion] = useState('Continente')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    setError('')
    void vatClassification(companyId)
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as contas.') })
    return () => controller.abort()
  }, [companyId, onDatabase, vatClassification, reload])

  const change = async (row: VatClassificationRow, patch: Partial<Pick<VatClassificationRow, 'rate' | 'nature' | 'region'>>) => {
    const next = { rate: patch.rate ?? row.rate, nature: patch.nature ?? row.nature, region: patch.region ?? row.region }
    setRows((current) => current.map((item) => item.accountCode === row.accountCode
      ? { ...item, ...next, classified: next.rate !== 'Nenhuma' || next.nature !== 'Nenhuma' }
      : item))
    setStatus(''); setError('')
    try {
      await setVatClassification(companyId, row.accountCode, next)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar a classificação.')
      setReload((value) => value + 1)
    }
  }

  const applyBulk = async () => {
    if (!bulkPrefix.trim()) { setError('Indique um prefixo de conta.'); return }
    setBusy(true); setError(''); setStatus('')
    try {
      const count = await bulkVatClassification(companyId, { prefix: bulkPrefix.trim(), rate: bulkRate, nature: bulkNature, region: bulkRegion })
      setStatus(`${count} ${count === 1 ? 'conta atualizada' : 'contas atualizadas'}.`)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível aplicar em lote.')
    } finally {
      setBusy(false)
    }
  }

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const shown = useMemo(
    () => term ? rows.filter((row) => `${row.accountCode} ${row.accountName}`.toLocaleLowerCase('pt-PT').includes(term)) : rows,
    [rows, term],
  )
  const classifiedCount = rows.filter((row) => row.classified).length

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Contabilidade</p><h1>Configuração de IVA</h1><p>A taxa, a natureza e a região de cada conta de IVA e de base.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Percent /></span><h2>Requer ligação ao SQL Server</h2><p>A classificação de IVA lê o plano de contas da base de dados da empresa.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Configuração de IVA</h1><p>A taxa, a natureza da operação e a região de cada conta de movimento das classes 6 e 7 e das contas 24.3. Serve a desagregação do IVA por taxa/natureza para a declaração periódica.</p></div><span className="connection-badge database"><Database size={15} /> Ligado ao SQL Server</span></header>

    {error && <p className="save-status error">{error}</p>}
    {status && !error && <p className="save-status success">{status}</p>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Aplicar em lote</h2><p>Classifica todas as contas relevantes cujo código comece pelo prefixo.</p></div></div>
      <div className="builder-row">
        <label>Prefixo <input value={bulkPrefix} onChange={(event) => setBulkPrefix(event.target.value)} placeholder="ex.: 24331" /></label>
        <label>Taxa <select value={bulkRate} onChange={(event) => setBulkRate(event.target.value)}>{VAT_RATES.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
        <label>Natureza <select value={bulkNature} onChange={(event) => setBulkNature(event.target.value)}>{VAT_NATURES.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
        <label>Região <select value={bulkRegion} onChange={(event) => setBulkRegion(event.target.value)}>{VAT_REGIONS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
        <button type="button" className="button button-primary" onClick={() => void applyBulk()} disabled={busy}>{busy ? 'A aplicar…' : 'Aplicar'}</button>
      </div>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Contas</h2><p>{classifiedCount} de {rows.length} classificadas.</p></div>
        <label className="vat-config-search"><Search size={15} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta…" /></label>
      </div>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th>Descrição</th><th>Taxa</th><th>Natureza</th><th>Região</th></tr></thead>
        <tbody>{shown.map((row) => <tr key={row.accountCode} className={row.classified ? undefined : 'muted-row'}>
          <td><Link className="primary-cell" to={`/contas/${row.accountCode}`}>{row.accountCode}</Link></td>
          <td>{row.accountName}</td>
          <td><select value={row.rate} onChange={(event) => void change(row, { rate: event.target.value })}>{VAT_RATES.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></td>
          <td><select value={row.nature} onChange={(event) => void change(row, { nature: event.target.value })}>{VAT_NATURES.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></td>
          <td><select value={row.region} onChange={(event) => void change(row, { region: event.target.value })}>{VAT_REGIONS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></td>
        </tr>)}</tbody>
      </table></div>
      {rows.length === 0 && <p className="empty-state">Sem contas de movimento das classes 6/7 ou 24.3.</p>}
    </section>
  </div>
}
