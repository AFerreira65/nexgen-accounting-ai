import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useEffect, useState } from 'react'
import { paginate } from './pagination'

// Paginação de listas grandes partilhada pelos ecrãs. `resetKey` volta à
// primeira página quando muda (tipicamente a pesquisa e outros filtros
// juntos numa string).
export function usePagedList<T>(items: T[], resetKey: string, pageSize: number) {
  const [page, setPage] = useState(1)
  useEffect(() => setPage(1), [resetKey])
  const { items: paged, currentPage, totalPages } = paginate(items, pageSize, page)
  return { paged, count: items.length, currentPage, totalPages, setPage }
}

export function Pager({ currentPage, totalPages, onPage }: { currentPage: number; totalPages: number; onPage: (page: number) => void }) {
  if (totalPages <= 1) return null
  return <div className="pager">
    <button className="button button-secondary" type="button" disabled={currentPage === 1} onClick={() => onPage(currentPage - 1)}><ChevronLeft size={15} /> Anterior</button>
    <span>Página {currentPage} de {totalPages}</span>
    <button className="button button-secondary" type="button" disabled={currentPage === totalPages} onClick={() => onPage(currentPage + 1)}>Seguinte <ChevronRight size={15} /></button>
  </div>
}
