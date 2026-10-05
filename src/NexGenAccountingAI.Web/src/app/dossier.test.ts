import { describe, expect, it } from 'vitest'
import { dossierAmounts } from './dossier'

describe('dossierAmounts', () => {
  it('reads the income summary of a saved dossier', () => {
    const dossier = { hasComparative: true, incomeSummary: [{ label: 'Rendimentos', current: 1000, comparative: 800, delta: 200 }, { label: 'Gastos', current: 600, comparative: 'x' }] }
    expect(dossierAmounts(dossier, 'incomeSummary')).toEqual([{ label: 'Rendimentos', current: 1000, comparative: 800 }])
  })

  it('returns nothing for a dossier of another shape', () => {
    expect(dossierAmounts(null, 'incomeSummary')).toEqual([])
    expect(dossierAmounts({ positionSummary: 'nada' }, 'positionSummary')).toEqual([])
  })
})
