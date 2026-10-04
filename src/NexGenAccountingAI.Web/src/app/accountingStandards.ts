// Descrições em Português Europeu das normas contabilísticas (`AccountingStandard`).
// Fonte única partilhada pelos ecrãs de organização e de importação de SAF-T.

export const ACCOUNTING_STANDARD_NAMES: Record<string, string> = {
  SncGeneral: 'SNC — Regime geral',
  SncSmallEntities: 'SNC — Pequenas entidades (NC-PE)',
  SncMicroEntities: 'SNC — Microentidades (NC-ME)',
  SncNonProfit: 'SNC — Entidades do setor não lucrativo (ESNL)',
  IfrsEu: 'Normas internacionais (IAS/IFRS) adotadas pela UE',
}

export const ACCOUNTING_STANDARDS = Object.keys(ACCOUNTING_STANDARD_NAMES)

/** Descrição em Português da norma; devolve o próprio código se não for conhecido. */
export function accountingStandardName(code: string | null | undefined): string {
  if (!code) return '—'
  return ACCOUNTING_STANDARD_NAMES[code] ?? code
}
