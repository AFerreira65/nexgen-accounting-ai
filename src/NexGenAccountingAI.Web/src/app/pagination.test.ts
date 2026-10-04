import { describe, expect, it } from 'vitest'
import { paginate } from './pagination'

describe('paginate', () => {
  const items = Array.from({ length: 137 }, (_, index) => index + 1)

  it('returns the requested slice and page count', () => {
    const result = paginate(items, 50, 2)
    expect(result.items).toEqual(items.slice(50, 100))
    expect(result.currentPage).toBe(2)
    expect(result.totalPages).toBe(3)
  })

  it('clamps a page beyond the total back to the last page', () => {
    // Um filtro pode reduzir o total depois de o utilizador ter avançado
    // páginas; a página pedida nunca deve devolver uma fatia vazia por isso.
    const result = paginate(items, 50, 99)
    expect(result.currentPage).toBe(3)
    expect(result.items).toEqual(items.slice(100, 137))
  })

  it('clamps a page below 1 up to the first page', () => {
    const result = paginate(items, 50, 0)
    expect(result.currentPage).toBe(1)
    expect(result.items).toEqual(items.slice(0, 50))
  })

  it('fits everything on one page when the list is smaller than the page size', () => {
    const result = paginate(items.slice(0, 3), 50, 1)
    expect(result.totalPages).toBe(1)
    expect(result.items).toEqual(items.slice(0, 3))
  })

  it('never divides by a zero or negative page size', () => {
    const result = paginate(items.slice(0, 5), 0, 1)
    expect(result.totalPages).toBe(1)
    expect(result.items).toEqual(items.slice(0, 5))
  })

  it('returns an empty page for an empty list without throwing', () => {
    const result = paginate<number>([], 50, 1)
    expect(result.totalPages).toBe(1)
    expect(result.currentPage).toBe(1)
    expect(result.items).toEqual([])
  })
})
