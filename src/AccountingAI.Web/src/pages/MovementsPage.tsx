import { ChevronLeft, ChevronRight, Database, Download, Plus, Search, StickyNote } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { ENTRY_SOURCE_LABELS, ENTRY_SOURCES, entrySourceLabel } from '../app/entrySources'
import { useMovementGuards } from '../app/movementGuards'
import { paginate } from '../app/pagination'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'
import { MovementEditor } from './MovementEditor'

type ServerPage = { items: Movement[]; totalItems: number; totalPages: number; currentPage: number }
type Filters = { search: string; journal: string; status: string; source: string; from: string; to: string }
const EMPTY_PAGE: ServerPage = { items: [], totalItems: 0, totalPages: 1, currentPage: 1 }

function withFilters(params: URLSearchParams, filters: Filters) {
  if (filters.search) params.set('search', filters.search)
  if (filters.journal) params.set('journal', filters.journal)
  if (filters.status) params.set('status', filters.status)
  if (filters.source) params.set('source', filters.source)
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  return params
}

export function MovementsPage() {
  const { accountingSetup, accountingSource, activeCompany, movements, reopenMovement, reviewMovement, user, voidMovement } = useWorkContext()
  const [editing, setEditing] = useState<{ movement: Movement | null; duplicate: boolean } | undefined>(undefined)
  const [busy, setBusy] = useState('')
  const [search, setSearch] = useState('')
  const [journalFilter, setJournalFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [sourceFilter, setSourceFilter] = useState('')
  // A lista arranca no exercício da empresa — como o livro diário e a razão —
  // para não abrir com anos misturados numa empresa importada de SAF-T;
  // «Limpar datas» abre tudo.
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(activeCompany.settings.defaultFiscalYear, activeCompany.companyId)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const blockedReason = useMovementGuards()

  const pageSize = activeCompany.settings.pageSize || 50

  // Numa empresa ligada à BD, a pesquisa dispara um pedido; espera que o
  // utilizador pare de escrever antes de o fazer.
  const [debouncedSearch, setDebouncedSearch] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300)
    return () => clearTimeout(timer)
  }, [search])

  const filters = useMemo<Filters>(() => ({
    search: debouncedSearch.trim(),
    journal: journalFilter,
    status: statusFilter,
    source: sourceFilter,
    from: fromDate,
    to: toDate,
  }), [debouncedSearch, journalFilter, statusFilter, sourceFilter, fromDate, toDate])

  const companyId = activeCompany.companyId
  const userId = user.id
  const movementsPath = `/api/v1/development/users/${userId}/companies/${companyId}/accounting/movements`

  // A empresa ligada à BD pede só a página atual ao servidor; a demonstração
  // (e uma falha de rede) continuam a filtrar o array que o contexto trouxe.
  const onDatabase = accountingSource === 'database'
  const [server, setServer] = useState<ServerPage>(EMPTY_PAGE)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!onDatabase) return
    const controller = new AbortController()
    setLoading(true)
    const params = withFilters(new URLSearchParams({ page: String(page), pageSize: String(pageSize) }), filters)
    fetch(`/api/v1/development/users/${userId}/companies/${companyId}/accounting/movements?${params}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as { items: Movement[]; page: number; totalItems: number; totalPages: number }
      })
      .then((body) => {
        setServer({ items: body.items, totalItems: body.totalItems, totalPages: body.totalPages, currentPage: body.page })
        setFailed(false)
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFailed(true)
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [onDatabase, page, pageSize, filters, companyId, userId, reload])

  // Volta à primeira página sempre que um filtro muda.
  useEffect(() => setPage(1), [filters])

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const clientVisible = useMemo(() => movements.filter((movement) =>
    (!term || `${movement.id} ${movement.description} ${movement.document ?? ''} ${movement.notes ?? ''}`.toLocaleLowerCase('pt-PT').includes(term))
    && (!journalFilter || movement.journalId === journalFilter)
    && (!statusFilter || movement.status === statusFilter)
    && (!sourceFilter || (movement.source ?? 'Manual') === sourceFilter)
    && (!fromDate || movement.date >= fromDate)
    && (!toDate || movement.date <= toDate)),
    [movements, term, journalFilter, statusFilter, sourceFilter, fromDate, toDate])

  // O caminho do cliente serve a demonstração e também a degradação graciosa
  // quando o pedido ao servidor falha.
  const useClient = !onDatabase || failed
  const clientPaged = paginate(clientVisible, pageSize, page)

  const rows = useClient ? clientPaged.items : server.items
  const currentPage = useClient ? clientPaged.currentPage : server.currentPage
  const totalPages = useClient ? clientPaged.totalPages : server.totalPages
  const filteredCount = useClient ? clientVisible.length : server.totalItems
  const anyFilter = Boolean(term || journalFilter || statusFilter || sourceFilter || fromDate || toDate)

  async function act(reference: string, action: () => Promise<void>) {
    setBusy(reference)
    try { await action(); setReload((value) => value + 1) } catch { /* a página volta a carregar o estado */ } finally { setBusy('') }
  }

  const [exporting, setExporting] = useState(false)
  const exportCsv = async () => {
    let source = clientVisible
    if (!useClient) {
      setExporting(true)
      try {
        // Sem `page`, o endpoint devolve o conjunto filtrado inteiro para o CSV.
        const response = await fetch(`${movementsPath}?${withFilters(new URLSearchParams(), filters)}`)
        source = response.ok ? await response.json() as Movement[] : []
      } catch { source = [] } finally { setExporting(false) }
    }
    const header = ['Movimento', 'Data', 'Diário', 'Tipo de documento', 'Documento', 'Origem', 'Descrição', 'Notas', 'Estado', 'Conta', 'Nome da conta', 'Descrição da linha', 'Débito', 'Crédito']
    const csvRows = source.flatMap((movement) => movement.lines.map((line) => [
      movement.id, movement.date, movement.journalId, movement.documentType ?? '', movement.document, entrySourceLabel(movement.source), movement.description, movement.notes ?? '', movement.status,
      line.account, line.accountName, line.description, line.debit, line.credit,
    ]))
    downloadCsv(csvFilename('movimentos', activeCompany.legalName), toCsv([header, ...csvRows]))
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading"><div><p className="eyebrow">Contabilidade</p><h1>Movimentos contabilísticos</h1><p>Consulte, percorra e registe lançamentos sem perder o contexto.</p></div><span className={`connection-badge ${accountingSource}`}><Database size={15} />{accountingSource === 'database' ? 'Dados da BD' : 'Fallback de demonstração'}</span></header>
    <div className="filterbar">
      <label><Search size={17} /><input aria-label="Pesquisar movimentos" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar movimento ou descrição…" /></label>
      <label><span className="sr-only">Diário</span><select value={journalFilter} onChange={(event) => setJournalFilter(event.target.value)}><option value="">Todos os diários</option>{accountingSetup.journals.map((journal) => <option key={journal.code} value={journal.code}>{journal.code} · {journal.name}</option>)}</select></label>
      <label><span className="sr-only">Estado</span><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="">Todos os estados</option><option value="Por conferir">Por conferir</option><option value="Conferido">Conferido</option><option value="Anulado">Anulado</option></select></label>
      <label><span className="sr-only">Origem</span><select value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value)}><option value="">Todas as origens</option>{ENTRY_SOURCES.map((code) => <option key={code} value={code}>{ENTRY_SOURCE_LABELS[code]}</option>)}</select></label>
      <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
      <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
      <PeriodShortcuts fiscalYear={activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      {(fromDate || toDate) && <button className="button button-secondary" type="button" onClick={() => { setFromDate(''); setToDate('') }}>Limpar datas</button>}
      <span className="catalog-count">{useClient ? `${filteredCount} de ${movements.length}` : `${filteredCount} ${filteredCount === 1 ? 'movimento' : 'movimentos'}`}</span>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={exporting || filteredCount === 0}><Download size={15} /> Exportar CSV</button>
      <button className="button button-primary" type="button" onClick={() => setEditing({ movement: null, duplicate: false })}><Plus size={16} /> Novo lançamento</button>
    </div>
    {editing !== undefined && <MovementEditor movement={editing.movement} duplicate={editing.duplicate} onClose={() => { setEditing(undefined); setReload((value) => value + 1) }} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{rows.map((movement) => <tr key={movement.id}>
      <td><Link className="primary-cell" to={`/movimentos/${movement.id}`}>{movement.id.replaceAll('-', ' ')}</Link><small>{formatMovementDate(movement.date)}</small></td>
      <td>{movement.description}{movement.notes ? <span className="note-flag" title={movement.notes}><StickyNote size={13} /> nota</span> : null}<small>{movement.documentType ? `${movement.documentType} · ` : ''}{movement.document}{movement.source && movement.source !== 'Manual' ? ` · ${entrySourceLabel(movement.source)}` : ''}</small></td>
      <td><Link className="inline-link" to={`/diarios/${movement.journalId}`}>{movement.journalId}</Link></td>
      <td><span className={`badge ${movement.status === 'Conferido' ? 'success' : movement.status === 'Anulado' ? 'neutral' : 'warning'}`}>{movement.status}</span></td>
      <td className="numeric strong">{euro.format(movement.amount)}</td>
      <td><div className="row-actions">
        {(() => {
          const blocked = blockedReason(movement)
          return <>
            {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => setEditing({ movement, duplicate: false })}>Corrigir</button>}
            {movement.status === 'Por conferir' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => act(movement.id, () => reviewMovement(movement.id))}>Conferir</button>}
            {movement.status === 'Conferido' && movement.documentType !== 'AP' && movement.documentType !== 'AB' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => act(movement.id, () => reopenMovement(movement.id))}>Reabrir</button>}
            {movement.status !== 'Anulado' && <button className="button button-secondary" type="button" disabled={busy === movement.id || blocked !== null} title={blocked ?? undefined} onClick={() => act(movement.id, () => voidMovement(movement.id))}>Anular</button>}
            {movement.documentType !== 'AP' && movement.documentType !== 'AB' && <button className="button button-secondary" type="button" disabled={busy === movement.id} onClick={() => setEditing({ movement, duplicate: true })}>Duplicar</button>}
            {blocked !== null && movement.status !== 'Anulado' && <small className="row-actions-note">{blocked}</small>}
          </>
        })()}
      </div></td>
    </tr>)}</tbody></table></div>
    {totalPages > 1 && <div className="pager">
      <button className="button button-secondary" type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}><ChevronLeft size={15} /> Anterior</button>
      <span>Página {currentPage} de {totalPages}</span>
      <button className="button button-secondary" type="button" disabled={currentPage === totalPages} onClick={() => setPage(currentPage + 1)}>Seguinte <ChevronRight size={15} /></button>
    </div>}
    {!useClient && loading && <p className="empty-state">A carregar movimentos da base de dados…</p>}
    {!useClient && !loading && filteredCount === 0 && <p className="empty-state">{anyFilter ? 'Nenhum movimento corresponde aos filtros.' : 'Esta empresa ainda não tem movimentos.'}</p>}
    {useClient && onDatabase && failed && <p className="empty-state">Sem ligação à base de dados — a mostrar os movimentos já carregados.</p>}
    {useClient && movements.length === 0 && <p className="empty-state">Esta empresa ainda não tem movimentos.</p>}
    {useClient && movements.length > 0 && clientVisible.length === 0 && <p className="empty-state">Nenhum movimento corresponde aos filtros.</p>}</section>
  </div>
}
