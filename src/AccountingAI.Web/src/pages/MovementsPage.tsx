import { ChevronLeft, ChevronRight, Database, Download, Plus, Search, StickyNote } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type Movement } from '../app/demo-data'
import { ENTRY_SOURCE_LABELS, ENTRY_SOURCES, entrySourceLabel } from '../app/entrySources'
import { useMovementGuards } from '../app/movementGuards'
import { paginate } from '../app/pagination'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { useWorkContext } from '../app/WorkContext'
import { MovementEditor } from './MovementEditor'

export function MovementsPage() {
  const { accountingLoading, accountingSetup, accountingSource, activeCompany, movements, reopenMovement, reviewMovement, voidMovement } = useWorkContext()
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
  const blockedReason = useMovementGuards()

  async function act(reference: string, action: () => Promise<void>) {
    setBusy(reference)
    try { await action() } catch { /* a página volta a carregar o estado */ } finally { setBusy('') }
  }

  const term = search.trim().toLocaleLowerCase('pt-PT')
  const visible = movements.filter((movement) =>
    (!term || `${movement.id} ${movement.description} ${movement.document ?? ''} ${movement.notes ?? ''}`.toLocaleLowerCase('pt-PT').includes(term))
    && (!journalFilter || movement.journalId === journalFilter)
    && (!statusFilter || movement.status === statusFilter)
    && (!sourceFilter || (movement.source ?? 'Manual') === sourceFilter)
    && (!fromDate || movement.date >= fromDate)
    && (!toDate || movement.date <= toDate))

  // Pagina-se pelo tamanho de página da empresa (definido em Preferências),
  // voltando à primeira página quando um filtro muda.
  const { items: paged, currentPage, totalPages } = paginate(visible, activeCompany.settings.pageSize, page)

  useEffect(() => setPage(1), [search, journalFilter, statusFilter, sourceFilter, fromDate, toDate])

  const exportCsv = () => {
    const header = ['Movimento', 'Data', 'Diário', 'Tipo de documento', 'Documento', 'Origem', 'Descrição', 'Notas', 'Estado', 'Conta', 'Nome da conta', 'Descrição da linha', 'Débito', 'Crédito']
    const rows = visible.flatMap((movement) => movement.lines.map((line) => [
      movement.id, movement.date, movement.journalId, movement.documentType ?? '', movement.document, entrySourceLabel(movement.source), movement.description, movement.notes ?? '', movement.status,
      line.account, line.accountName, line.description, line.debit, line.credit,
    ]))
    downloadCsv(csvFilename('movimentos', activeCompany.legalName), toCsv([header, ...rows]))
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
      <span className="catalog-count">{visible.length} de {movements.length}</span>
      <button className="button button-secondary" type="button" onClick={exportCsv} disabled={visible.length === 0}><Download size={15} /> Exportar CSV</button>
      <button className="button button-primary" type="button" onClick={() => setEditing({ movement: null, duplicate: false })}><Plus size={16} /> Novo lançamento</button>
    </div>
    {editing !== undefined && <MovementEditor movement={editing.movement} duplicate={editing.duplicate} onClose={() => setEditing(undefined)} />}
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Movimento</th><th>Descrição</th><th>Diário</th><th>Estado</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{paged.map((movement) => <tr key={movement.id}>
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
    {accountingLoading && <p className="empty-state">A carregar movimentos da base de dados…</p>}{!accountingLoading && movements.length === 0 && <p className="empty-state">Esta empresa ainda não tem movimentos.</p>}{!accountingLoading && movements.length > 0 && visible.length === 0 && <p className="empty-state">Nenhum movimento corresponde aos filtros.</p>}</section>
  </div>
}
