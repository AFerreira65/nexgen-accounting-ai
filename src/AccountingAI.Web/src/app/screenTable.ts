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
  '/declaracao-iva': { schema: 'accounting', name: 'JournalEntryLines' },
  '/declaracao-recapitulativa': { schema: 'accounting', name: 'JournalEntryLines' },
  '/modelo-22': { schema: 'accounting', name: 'IrcReturnValues' },
  '/ies': { schema: 'accounting', name: 'JournalEntryLines' },
  '/retencoes': { schema: 'accounting', name: 'JournalEntryLines' },
  '/codigos-iva': { schema: 'accounting', name: 'VatCodes' },
  '/config-iva': { schema: 'accounting', name: 'AccountVatClassification' },
  '/estimativa-irc': { schema: 'accounting', name: 'JournalEntryLines' },
  '/contas-correntes': { schema: 'accounting', name: 'JournalEntryLines' },
  '/apuramento': { schema: 'accounting', name: 'JournalEntries' },
  '/abertura': { schema: 'accounting', name: 'JournalEntries' },
  '/contas': { schema: 'accounting', name: 'Accounts' },
  '/centros-custo': { schema: 'accounting', name: 'CostCenters' },
  '/ativos-fixos': { schema: 'accounting', name: 'FixedAssets' },
  '/mapa-depreciacoes': { schema: 'accounting', name: 'FixedAssets' },
  '/mapa-analitico': { schema: 'accounting', name: 'JournalEntryLines' },
  '/demonstracoes': { schema: 'accounting', name: 'JournalEntryLines' },
  '/evolucao': { schema: 'accounting', name: 'JournalEntryLines' },
  '/mapa-exploracao': { schema: 'accounting', name: 'JournalEntryLines' },
  '/racios': { schema: 'accounting', name: 'JournalEntryLines' },
  '/lancamentos-tipo': { schema: 'accounting', name: 'EntryTemplates' },
  '/saldos-terceiros': { schema: 'accounting', name: 'JournalEntryLines' },
  '/terceiros': { schema: 'accounting', name: 'Parties' },
  '/taxonomias': { schema: 'accounting', name: 'Accounts' },
  '/diarios': { schema: 'accounting', name: 'Journals' },
  '/documentos': { schema: 'accounting', name: 'AccountingDocumentTypes' },
  '/series': { schema: 'accounting', name: 'DocumentNumberSeries' },
  '/exportar-saft': { schema: 'accounting', name: 'JournalEntries' },
  '/importar': { schema: 'accounting', name: 'SaftValidationRun' },
  '/compras-efatura': { schema: 'accounting', name: 'EFaturaPurchase' },
  '/cruzamento': { schema: 'accounting', name: 'EFaturaPurchase' },
  '/comunicacao-at': null,
  '/tabelas-fiscais': null,
  '/organizacao': { schema: 'organization', name: 'Companies' },
  '/definicoes': { schema: 'identity', name: 'UserCompanySettings' },
}

const PREFIX: { prefix: string; table: ScreenTable }[] = [
  { prefix: '/movimentos/', table: { schema: 'accounting', name: 'JournalEntries' } },
  { prefix: '/contas/', table: { schema: 'accounting', name: 'Accounts' } },
  { prefix: '/terceiros/', table: { schema: 'accounting', name: 'Parties' } },
  { prefix: '/diarios/', table: { schema: 'accounting', name: 'Journals' } },
]

export function screenTableForPath(pathname: string): ScreenTable | null {
  if (pathname in EXACT) return EXACT[pathname]
  const match = PREFIX.find((entry) => pathname.startsWith(entry.prefix))
  return match ? match.table : null
}
