import { describe, expect, it } from 'vitest'
import { monthRange, periodReference, quarterRange, yearRange } from './periods'

const utc = (year: number, month1: number, day: number) => new Date(Date.UTC(year, month1 - 1, day))

describe('yearRange', () => {
  it('spans 1 January to 31 December of the fiscal year', () => {
    expect(yearRange(2025)).toEqual({ from: '2025-01-01', to: '2025-12-31' })
  })
})

describe('periodReference', () => {
  it('returns today while it is still inside the fiscal year', () => {
    expect(periodReference(2026, utc(2026, 6, 1))).toEqual(utc(2026, 6, 1))
  })

  it('caps at 31 December once today is past the fiscal year end', () => {
    expect(periodReference(2025, utc(2026, 6, 1))).toEqual(utc(2025, 12, 31))
  })

  it('is exact on the last day of the fiscal year', () => {
    expect(periodReference(2025, utc(2025, 12, 31))).toEqual(utc(2025, 12, 31))
  })
})

describe('monthRange', () => {
  it('bounds the calendar month of the reference date', () => {
    expect(monthRange(utc(2025, 3, 15))).toEqual({ from: '2025-03-01', to: '2025-03-31' })
  })

  it('gives 29 days for February of a leap year', () => {
    expect(monthRange(utc(2024, 2, 10))).toEqual({ from: '2024-02-01', to: '2024-02-29' })
  })

  it('gives 28 days for February of a non-leap year', () => {
    expect(monthRange(utc(2025, 2, 10))).toEqual({ from: '2025-02-01', to: '2025-02-28' })
  })
})

describe('quarterRange', () => {
  it('maps January to Q1', () => {
    expect(quarterRange(utc(2025, 1, 8))).toEqual({ from: '2025-01-01', to: '2025-03-31' })
  })

  it('maps May to Q2', () => {
    expect(quarterRange(utc(2025, 5, 20))).toEqual({ from: '2025-04-01', to: '2025-06-30' })
  })

  it('maps December to Q4', () => {
    expect(quarterRange(utc(2025, 12, 31))).toEqual({ from: '2025-10-01', to: '2025-12-31' })
  })
})
