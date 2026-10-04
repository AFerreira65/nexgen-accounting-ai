// Rótulos pt-PT para a origem de um lançamento (enum EntrySource da API).

export const ENTRY_SOURCE_LABELS: Record<string, string> = {
  Manual: 'Manual',
  SaftImport: 'Importado do SAF-T',
  Opening: 'Abertura',
  Closing: 'Apuramento',
  EFatura: 'Sugerido do e-Fatura',
}

export const ENTRY_SOURCES = Object.keys(ENTRY_SOURCE_LABELS)

export function entrySourceLabel(code: string | null | undefined): string {
  if (!code) return ENTRY_SOURCE_LABELS.Manual
  return ENTRY_SOURCE_LABELS[code] ?? code
}
