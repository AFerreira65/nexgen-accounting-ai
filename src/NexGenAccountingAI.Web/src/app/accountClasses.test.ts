import { describe, expect, it } from 'vitest'
import { classNameOf, groupBySubclass, subclassNameOf, type SubclassLine } from './accountClasses'

describe('classNameOf', () => {
  it('names the SNC class from the first digit', () => {
    expect(classNameOf('6221')).toBe('Gastos')
    expect(classNameOf('7111')).toBe('Rendimentos')
  })

  it('trims surrounding whitespace before reading the digit', () => {
    expect(classNameOf('  2431')).toBe('Contas a receber e a pagar')
  })

  it('falls back for a code outside the known classes', () => {
    expect(classNameOf('X99')).toBe('Outras contas')
  })
})

describe('subclassNameOf', () => {
  it('names the SNC subclass from the first two digits', () => {
    expect(subclassNameOf('6221')).toBe('Fornecimentos e serviços externos')
    expect(subclassNameOf('2111')).toBe('Clientes')
  })

  it('falls back to the bare two digits for an unknown subclass', () => {
    expect(subclassNameOf('9901')).toBe('Subclasse 99')
  })
})

describe('groupBySubclass', () => {
  const lines: SubclassLine[] = [
    { code: '6231', name: 'Ferramentas', amount: 120 },
    { code: '6221', name: 'Trabalhos especializados', amount: 300 },
    { code: '6262', name: 'Comunicação', amount: 80 },
    { code: '6111', name: 'CMVMC', amount: 500 },
  ]

  it('groups lines by two-digit subclass and sums each group', () => {
    const { groups } = groupBySubclass(lines)
    expect(groups.map((g) => [g.code, g.amount])).toEqual([
      ['61', 500],
      ['62', 500],
    ])
  })

  it('names each group from the SNC subclass table', () => {
    const { groups } = groupBySubclass(lines)
    expect(groups.find((g) => g.code === '62')?.name).toBe('Fornecimentos e serviços externos')
  })

  it('orders groups by code and lines within a group by code', () => {
    const { groups } = groupBySubclass(lines)
    expect(groups.map((g) => g.code)).toEqual(['61', '62'])
    expect(groups[1].lines.map((l) => l.code)).toEqual(['6221', '6231', '6262'])
  })

  it('drops lines with a zero amount so empty accounts never show', () => {
    const { groups, total } = groupBySubclass([
      { code: '6221', name: 'Movimentada', amount: 300 },
      { code: '6222', name: 'Sem saldo', amount: 0 },
    ])
    expect(groups).toHaveLength(1)
    expect(groups[0].lines.map((l) => l.code)).toEqual(['6221'])
    expect(total).toBe(300)
  })

  it('keeps negative amounts (passivo and capital próprio are sign-flipped upstream)', () => {
    const { groups, total } = groupBySubclass([
      { code: '2211', name: 'Fornecedor A', amount: -400 },
      { code: '2212', name: 'Fornecedor B', amount: -150 },
    ])
    expect(groups[0].amount).toBe(-550)
    expect(total).toBe(-550)
  })

  it('returns the sum of every group as the total', () => {
    expect(groupBySubclass(lines).total).toBe(1000)
  })

  it('falls back to a generic name for an unknown subclass', () => {
    const { groups } = groupBySubclass([{ code: '9901', name: 'Estranha', amount: 10 }])
    expect(groups[0].name).toBe('Subclasse 99')
  })

  it('returns nothing for an empty list', () => {
    expect(groupBySubclass([])).toEqual({ groups: [], total: 0 })
  })
})
