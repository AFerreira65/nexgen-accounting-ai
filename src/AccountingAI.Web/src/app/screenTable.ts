/**
 * A tabela da base de dados que melhor corresponde a cada ecrã — para o menu
 * «Opções» abrir o dicionário de dados já na tabela certa. Devolve `null` nos
 * ecrãs sem uma tabela dominante (Tarefas, Visão geral).
 */
export type ScreenTable = { schema: string; name: string }

const EXACT: Record<string, ScreenTable | null> = {
  '/': null,
  '/tarefas': null,
  '/movimentos': { schema: 'accounting', name: 'JournalEntries' },
  '/livro-diario': { schema: 'accounting', name: 'JournalEntries' },
  '/analises': { schema: 'reporting', name: 'SavedAnalysis' },
  '/relatorios-gestao': { schema: 'reporting', name: 'ManagementReport' },
  '/balancete': { schema: 'accounting', name: 'JournalEntryLines' },
  '/resultados': { schema: 'accounting', name: 'JournalEntryLines' },
  '/balanco': { schema: 'accounting', name: 'JournalEntryLines' },
  '/iva': { schema: 'accounting', name: 'JournalEntryLines' },
  '/apuramento-iva': { schema: 'accounting', name: 'JournalEntryLines' },
  '/contas-correntes': { schema: 'accounting', name: 'JournalEntryLines' },
  '/apuramento': { schema: 'accounting', name: 'JournalEntries' },
  '/abertura': { schema: 'accounting', name: 'JournalEntries' },
  '/contas': { schema: 'accounting', name: 'Accounts' },
  '/taxonomias': { schema: 'accounting', name: 'Accounts' },
  '/diarios': { schema: 'accounting', name: 'Journals' },
  '/documentos': { schema: 'accounting', name: 'AccountingDocumentTypes' },
  '/series': { schema: 'accounting', name: 'DocumentNumberSeries' },
  '/importar': { schema: 'accounting', name: 'SaftValidationRun' },
  '/compras-efatura': { schema: 'accounting', name: 'EFaturaPurchase' },
  '/cruzamento': { schema: 'accounting', name: 'EFaturaPurchase' },
  '/organizacao': { schema: 'organization', name: 'Companies' },
  '/definicoes': { schema: 'identity', name: 'UserCompanySettings' },
}

const PREFIX: { prefix: string; table: ScreenTable }[] = [
  { prefix: '/movimentos/', table: { schema: 'accounting', name: 'JournalEntries' } },
  { prefix: '/contas/', table: { schema: 'accounting', name: 'Accounts' } },
  { prefix: '/diarios/', table: { schema: 'accounting', name: 'Journals' } },
]

export function screenTableForPath(pathname: string): ScreenTable | null {
  if (pathname in EXACT) return EXACT[pathname]
  const match = PREFIX.find((entry) => pathname.startsWith(entry.prefix))
  return match ? match.table : null
}
