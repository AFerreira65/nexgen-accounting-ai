// Os vocábulos do motor de mapas (F4B) em português — os códigos vêm da API em
// camelCase e nunca se mostram ao utilizador.

export type MapMeasure = 'balance' | 'debitBalance' | 'creditBalance' | 'debit' | 'credit'
export type MapRowKind = 'accounts' | 'formula' | 'title'
export type MapSign = 'normal' | 'inverted' | 'absolute' | 'zeroIfNegative'
export type MapShow = 'always' | 'ifNotZero' | 'ifPositive' | 'ifNegative'
export type MapScope = 'period' | 'yearToDate' | 'opening' | 'month'
export type MapColumnKind = 'value' | 'formula'

export type MapRow = {
  code: string
  label: string
  kind: MapRowKind
  accounts: string | null
  formula: string | null
  measure: MapMeasure
  sign: MapSign
  show: MapShow
  level: number
  bold: boolean
  hidden: boolean
}

export type MapColumn = {
  code: string
  label: string
  kind: MapColumnKind
  scope: MapScope
  yearOffset: number
  month: number | null
  formula: string | null
  measure: MapMeasure | null
  percent: boolean
}

export type MapDefinition = { rows: MapRow[]; columns: MapColumn[] }

export const MEASURE_LABELS: Record<MapMeasure, string> = {
  balance: 'Saldo',
  debitBalance: 'Saldo devedor',
  creditBalance: 'Saldo credor',
  debit: 'Débito',
  credit: 'Crédito',
}

export const ROW_KIND_LABELS: Record<MapRowKind, string> = {
  accounts: 'Contas',
  formula: 'Fórmula',
  title: 'Título',
}

export const SIGN_LABELS: Record<MapSign, string> = {
  normal: 'Normal',
  inverted: 'Invertido',
  absolute: 'Absoluto',
  zeroIfNegative: 'Zero se negativo',
}

export const SHOW_LABELS: Record<MapShow, string> = {
  always: 'Sempre',
  ifNotZero: 'Se diferente de zero',
  ifPositive: 'Se positivo',
  ifNegative: 'Se negativo',
}

export const SCOPE_LABELS: Record<MapScope, string> = {
  period: 'Período pedido',
  yearToDate: 'Acumulado do exercício',
  opening: 'Saldo inicial',
  month: 'Mês fixo',
}

export const COLUMN_KIND_LABELS: Record<MapColumnKind, string> = {
  value: 'Valores',
  formula: 'Fórmula',
}

export const MONTH_NAMES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

export function emptyRow(code: string): MapRow {
  return { code, label: '', kind: 'accounts', accounts: '', formula: null, measure: 'balance', sign: 'normal', show: 'always', level: 0, bold: false, hidden: false }
}

export function emptyColumn(code: string): MapColumn {
  return { code, label: '', kind: 'value', scope: 'period', yearOffset: 0, month: null, formula: null, measure: null, percent: false }
}

/** O próximo código livre com um prefixo (L10, L20, … ou C1, C2, …). */
export function nextCode(prefix: string, taken: { code: string }[], step = 10): string {
  const used = new Set(taken.map((item) => item.code.toUpperCase()))
  for (let number = step; number < 100000; number += step) {
    const candidate = `${prefix}${number}`
    if (!used.has(candidate)) return candidate
  }
  return `${prefix}${Date.now()}`
}
