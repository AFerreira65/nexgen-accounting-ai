import { ArrowUp, Database, FileText, Hash, Landmark, Save, Search, ShieldCheck } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from '../app/WorkContext'

type NumberSeries = {
  series: string
  year: number
  highestIssuedNumber: number
  nextNumber: number
  hasCounter: boolean
  origin: string
  name: string
  originRoute: string | null
}

function parsedReference(reference: string) {
  const match = /^(.*)-(\d{4})-(\d+)$/.exec(reference)
  return match ? { series: match[1], year: Number(match[2]), number: Number(match[3]) } : null
}

export function DocumentNumberSeriesPage() {
  const { activeCompany, accountingSetup, movements, source, user } = useWorkContext()
  const [year, setYear] = useState(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear())
  const [rows, setRows] = useState<NumberSeries[]>([])
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [requestedNext, setRequestedNext] = useState('')
  const [status, setStatus] = useState<'idle' | 'loading' | 'saving' | 'error' | 'saved'>('idle')
  const [message, setMessage] = useState('')

  const demonstrationRows = useMemo(() => {
    const definitions = new Map<string, { origin: string; name: string; originRoute: string }>()
    accountingSetup.documentTypes.forEach((item) => definitions.set(item.code, { origin: 'Tipo de documento', name: item.name, originRoute: '/documentos' }))
    accountingSetup.journals.forEach((item) => {
      const existing = definitions.get(item.code)
      definitions.set(item.code, existing
        ? { origin: 'Tipo de documento e diário', name: `${existing.name} / ${item.name}`, originRoute: '/documentos' }
        : { origin: 'Diário', name: item.name, originRoute: '/diarios' })
    })
    const highest = new Map<string, number>()
    movements.forEach((movement) => {
      const parsed = parsedReference(movement.id)
      if (parsed?.year === year) highest.set(parsed.series, Math.max(highest.get(parsed.series) ?? 0, parsed.number))
    })
    highest.forEach((_, series) => {
      if (!definitions.has(series)) definitions.set(series, { origin: 'Histórico', name: 'Série detetada nos movimentos', originRoute: '/movimentos' })
    })
    return [...definitions.entries()].sort(([left], [right]) => left.localeCompare(right, 'pt-PT')).map(([series, definition]) => ({
      series,
      year,
      highestIssuedNumber: highest.get(series) ?? 0,
      nextNumber: (highest.get(series) ?? 0) + 1,
      hasCounter: false,
      ...definition,
    }))
  }, [accountingSetup, movements, year])

  useEffect(() => {
    setYear(activeCompany.settings.defaultFiscalYear ?? new Date().getFullYear())
  }, [activeCompany.companyId, activeCompany.settings.defaultFiscalYear])

  useEffect(() => {
    setEditing(null)
    setMessage('')
    if (source !== 'database') {
      setRows(demonstrationRows)
      setStatus('idle')
      return
    }

    const controller = new AbortController()
    setStatus('loading')
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/number-series?year=${year}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(await problemMessage(response))
        setRows(await response.json() as NumberSeries[])
        setStatus('idle')
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setRows([])
        setMessage(error instanceof Error ? error.message : 'Não foi possível consultar as séries.')
        setStatus('error')
      })
    return () => controller.abort()
  }, [activeCompany.companyId, demonstrationRows, source, user.id, year])

  const filtered = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('pt-PT')
    return term ? rows.filter((row) => `${row.series} ${row.name} ${row.origin}`.toLocaleLowerCase('pt-PT').includes(term)) : rows
  }, [rows, search])
  const activeCounters = rows.filter((row) => row.hasCounter).length
  const issuedSeries = rows.filter((row) => row.highestIssuedNumber > 0).length

  function beginEdit(row: NumberSeries) {
    setEditing(row.series)
    setRequestedNext(String(row.nextNumber))
    setStatus('idle')
    setMessage('')
  }

  async function save(event: FormEvent, row: NumberSeries) {
    event.preventDefault()
    const nextNumber = Number(requestedNext)
    const minimum = Math.max(row.nextNumber, row.highestIssuedNumber + 1)
    if (!Number.isInteger(nextNumber) || nextNumber < minimum) {
      setStatus('error')
      setMessage(`O próximo número tem de ser um inteiro igual ou superior a ${minimum}.`)
      return
    }

    setStatus('saving')
    setMessage('')
    try {
      let updated = { ...row, nextNumber, hasCounter: true }
      if (source === 'database') {
        const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/number-series/${encodeURIComponent(row.series)}/${row.year}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ nextNumber }),
        })
        if (!response.ok) throw new Error(await problemMessage(response))
        updated = await response.json() as NumberSeries
      }
      setRows((current) => current.map((item) => item.series === row.series ? updated : item))
      setEditing(null)
      setStatus('saved')
      setMessage(source === 'database' ? `Série ${row.series} atualizada no SQL Server.` : `Série ${row.series} ajustada apenas nesta demonstração.`)
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof Error ? error.message : 'Não foi possível atualizar a série.')
    }
  }

  return <div className="page-stack">
    <header className="page-heading">
      <div><p className="eyebrow">Numeração contabilística</p><h1>Séries documentais</h1><p>Consulte o último número emitido e avance com segurança o próximo número de cada série.</p></div>
      <span className={`connection-badge ${source}`}><Database size={15} />{source === 'database' ? 'Contadores no SQL Server' : 'Modo de demonstração'}</span>
    </header>

    <section className="metric-grid series-metrics">
      <article className="metric-card"><span className="metric-icon teal"><Hash /></span><div><small>Séries configuradas</small><strong>{rows.length}</strong><span>Tipos de documento e diários</span></div></article>
      <article className="metric-card"><span className="metric-icon blue"><ShieldCheck /></span><div><small>Contadores persistidos</small><strong>{activeCounters}</strong><span>{source === 'database' ? 'Protegidos por empresa e exercício' : 'Pré-visualização local'}</span></div></article>
      <article className="metric-card"><span className="metric-icon amber"><FileText /></span><div><small>Séries com emissões</small><strong>{issuedSeries}</strong><span>No exercício {year}</span></div></article>
      <article className="metric-card"><span className="metric-icon teal"><ArrowUp /></span><div><small>Política de segurança</small><strong>Só avança</strong><span>Sem reciclagem de números</span></div></article>
    </section>

    <div className="filterbar series-filterbar">
      <label><Search size={17} /><span className="sr-only">Pesquisar séries</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar série, origem ou descrição…" /></label>
      <label className="year-filter"><span>Exercício</span><input aria-label="Exercício das séries" type="number" min="2000" max="2200" value={year} onChange={(event) => setYear(Number(event.target.value))} /></label>
    </div>

    {message && <div className={`series-message ${status === 'error' ? 'error' : 'success'}`}>{message}</div>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Controlo por série</h2><p>O contador só pode avançar. A emissão concorrente é serializada para evitar referências duplicadas.</p></div><ShieldCheck size={19} /></div>
      <div className="table-wrap"><table><thead><tr><th>Série</th><th>Origem</th><th>Descrição</th><th className="numeric">Último emitido</th><th className="numeric">Próximo</th><th>Estado</th><th><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{filtered.map((row) => <tr key={row.series}>
          <td><strong>{row.series}</strong><small className="table-subline">{row.year}</small></td>
          <td>{row.originRoute ? <Link className="inline-link" to={row.originRoute}>{row.origin.includes('Diário') ? <Landmark size={14} /> : <FileText size={14} />}{row.origin}</Link> : row.origin}</td>
          <td>{row.name}</td><td className="numeric">{row.highestIssuedNumber || '—'}</td><td className="numeric strong">{row.nextNumber}</td>
          <td><span className={`badge ${row.hasCounter ? 'success' : 'neutral'}`}>{row.hasCounter ? 'Persistido' : 'Por iniciar'}</span></td>
          <td>{editing === row.series
            ? <form className="series-editor" onSubmit={(event) => void save(event, row)}><input aria-label={`Próximo número de ${row.series}`} type="number" min={Math.max(row.nextNumber, row.highestIssuedNumber + 1)} value={requestedNext} onChange={(event) => setRequestedNext(event.target.value)} autoFocus /><button className="button button-primary" type="submit" disabled={status === 'saving'}><Save size={14} />Guardar</button><button className="button button-secondary" type="button" onClick={() => setEditing(null)}>Cancelar</button></form>
            : <button className="button button-secondary" type="button" onClick={() => beginEdit(row)}><ArrowUp size={14} />Avançar</button>}</td>
        </tr>)}</tbody>
      </table></div>
      {status === 'loading' && <div className="series-table-status">A consultar os contadores…</div>}
      {status !== 'loading' && filtered.length === 0 && <div className="series-table-status">Sem séries para apresentar neste exercício.</div>}
    </section>
  </div>
}
