// Leitura do dossiê guardado com cada relatório de gestão (JSON vindo da API, sem tipo).

export type DossierAmount = { label: string; current: number; comparative: number }

/** As rubricas de resultados ou de posição do dossiê; ignora o que não tiver a forma esperada. */
export function dossierAmounts(dossier: unknown, key: 'incomeSummary' | 'positionSummary'): DossierAmount[] {
  if (!dossier || typeof dossier !== 'object') return []
  const list = (dossier as Record<string, unknown>)[key]
  if (!Array.isArray(list)) return []
  return list.flatMap((item) => {
    if (!item || typeof item !== 'object') return []
    const { label, current, comparative } = item as Record<string, unknown>
    return typeof label === 'string' && typeof current === 'number' && typeof comparative === 'number' ? [{ label, current, comparative }] : []
  })
}
