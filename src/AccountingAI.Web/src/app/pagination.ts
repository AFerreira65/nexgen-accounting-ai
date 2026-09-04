// Paginação partilhada pelos ecrãs de lista: dado um conjunto já filtrado, a
// página pedida e o tamanho de página, devolve só a fatia a desenhar — para
// uma empresa com um histórico muito grande (ex.: importado de um SAF-T com
// anos de movimentos por liquidar) não pesar o browser a desenhar tudo de
// uma vez. A página pedida é sempre travada ao intervalo válido, para nunca
// devolver uma fatia vazia só porque um filtro reduziu o total depois de o
// utilizador ter avançado páginas.
export function paginate<T>(items: T[], pageSize: number, requestedPage: number): { items: T[]; currentPage: number; totalPages: number } {
  const safePageSize = pageSize > 0 ? pageSize : items.length || 1
  const totalPages = Math.max(1, Math.ceil(items.length / safePageSize))
  const currentPage = Math.min(Math.max(1, requestedPage), totalPages)
  const start = (currentPage - 1) * safePageSize
  return { items: items.slice(start, start + safePageSize), currentPage, totalPages }
}
