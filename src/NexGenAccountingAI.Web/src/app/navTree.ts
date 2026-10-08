import { Archive, ArrowLeftRight, Banknote, BarChart3, Bot, BookOpen, BookText, Briefcase, Building2, Calculator, CalendarCheck, CalendarClock, ChartLine, CircleGauge, ClipboardCheck, Contact, Database, DoorOpen, Factory, FileDown, FileSpreadsheet, FileText, FileUp, FolderCheck, FolderTree, HandCoins, Hourglass, Globe, Inbox, KeyRound, Landmark, ListChecks, LayoutDashboard, LayoutTemplate, ListFilter, ListOrdered, ListTodo, Newspaper, PencilRuler, Percent, PiggyBank, Receipt, Scale, ScanSearch, ShieldCheck, Sparkles, Table2, Tags, Target, TrendingUp, Users, WalletCards, WandSparkles, type LucideIcon } from 'lucide-react'

// O menu lateral em árvore: secções principais (a barra), e cada secção abre ao
// passar o rato um menu com ecrãs e, quando convém, submenus de outros níveis.
// Um nó é uma ligação (`to`) ou um grupo (`children`).
export type NavNode = { label: string; icon?: LucideIcon; to?: string; end?: boolean; children?: NavNode[] }

export const navSections: (NavNode & { icon: LucideIcon; children: NavNode[] })[] = [
  { label: 'Trabalho', icon: Briefcase, children: [
    { to: '/', label: 'Visão geral', icon: CircleGauge, end: true },
    { to: '/tarefas', label: 'Tarefas', icon: ClipboardCheck },
    { to: '/painel-gabinete', label: 'Painel do gabinete', icon: LayoutDashboard },
    { to: '/movimentos', label: 'Movimentos', icon: WalletCards },
    { to: '/lancamentos-sugeridos', label: 'Lançamentos sugeridos', icon: WandSparkles },
    { to: '/bancos', label: 'Bancos', icon: Banknote },
    { to: '/arquivo', label: 'Arquivo digital', icon: Archive },
  ] },
  { label: 'Mapas', icon: FileSpreadsheet, children: [
    { to: '/livro-diario', label: 'Livro diário', icon: BookText },
    { to: '/balancete', label: 'Balancete', icon: Scale },
    { to: '/resultados', label: 'Resultados', icon: TrendingUp },
    { to: '/balanco', label: 'Balanço', icon: Scale },
    { to: '/demonstracoes', label: 'Demonstrações financeiras', icon: Landmark },
    { label: 'Terceiros', icon: Users, children: [
      { to: '/contas-correntes', label: 'Contas correntes', icon: Users },
      { to: '/saldos-terceiros', label: 'Saldos de terceiros', icon: Contact },
      { to: '/idade-saldos', label: 'Idade de saldos', icon: Hourglass },
    ] },
    { label: 'Analítica e ativos', icon: FolderTree, children: [
      { to: '/mapa-analitico', label: 'Mapa analítico', icon: FolderTree },
      { to: '/mapa-depreciacoes', label: 'Mapa de depreciações', icon: Factory },
    ] },
    { label: 'À medida', icon: PencilRuler, children: [
      { to: '/mapas', label: 'Mapas à medida', icon: PencilRuler },
      { to: '/listagens', label: 'Listagens à medida', icon: ListFilter },
    ] },
  ] },
  { label: 'Fiscal', icon: Receipt, children: [
    { to: '/obrigacoes', label: 'Calendário de obrigações', icon: CalendarClock },
    { to: '/consola-obrigacoes', label: 'Consola de obrigações', icon: ListChecks },
    { label: 'IVA', icon: Receipt, children: [
      { to: '/iva', label: 'Resumo de IVA', icon: Receipt },
      { to: '/apuramento-iva', label: 'Apuramento de IVA', icon: Receipt },
      { to: '/declaracao-iva', label: 'Declaração de IVA', icon: Receipt },
      { to: '/declaracao-recapitulativa', label: 'Declaração recapitulativa', icon: ArrowLeftRight },
      { to: '/config-iva', label: 'Configuração de IVA', icon: Percent },
      { to: '/codigos-iva', label: 'Códigos de IVA', icon: Percent },
    ] },
    { label: 'IRC e declarações anuais', icon: Calculator, children: [
      { to: '/estimativa-irc', label: 'Estimativa de IRC', icon: Calculator },
      { to: '/modelo-22', label: 'Modelo 22', icon: Calculator },
      { to: '/ies', label: 'IES — Anexo A', icon: FileText },
    ] },
    { to: '/retencoes', label: 'Retenções na fonte', icon: HandCoins },
    { label: 'Dossier fiscal', icon: FolderCheck, children: [
      { to: '/dossier-fiscal', label: 'Dossier fiscal', icon: FolderCheck },
      { to: '/mapas-dossier', label: 'Mapas 30 a 32', icon: Table2 },
    ] },
    { to: '/tabelas-fiscais', label: 'Tabelas fiscais', icon: BookText },
    { to: '/comunicacao-at', label: 'Comunicação com a AT', icon: ShieldCheck },
  ] },
  { label: 'Gestão', icon: BarChart3, children: [
    { to: '/tarefas-gabinete', label: 'Tarefas do gabinete', icon: ListTodo },
    { to: '/assistente', label: 'Assistente', icon: Bot },
    { to: '/fecho', label: 'Fecho do mês', icon: ClipboardCheck },
    { label: 'Análise', icon: ChartLine, children: [
      { to: '/variacoes', label: 'Variações', icon: ChartLine },
      { to: '/anomalias', label: 'Anomalias', icon: ScanSearch },
      { to: '/evolucao', label: 'Evolução mensal', icon: ChartLine },
      { to: '/analises', label: 'Análises', icon: BarChart3 },
    ] },
    { label: 'Indicadores', icon: Percent, children: [
      { to: '/mapa-exploracao', label: 'Mapa de exploração', icon: TrendingUp },
      { to: '/racios', label: 'Rácios', icon: Percent },
      { to: '/orcamento', label: 'Orçamento', icon: Target },
    ] },
    { to: '/previsao-tesouraria', label: 'Previsão de tesouraria', icon: PiggyBank },
    { label: 'Relatórios', icon: Newspaper, children: [
      { to: '/relatorio-empresario', label: 'Relatório do mês', icon: Newspaper },
      { to: '/relatorios-gestao', label: 'Relatórios de gestão', icon: Sparkles },
      { to: '/relatorio-banca', label: 'Relatório para a banca', icon: Landmark },
    ] },
  ] },
  { label: 'Exercício', icon: CalendarCheck, children: [
    { to: '/apuramento', label: 'Apuramento', icon: CalendarCheck },
    { to: '/abertura', label: 'Abertura', icon: DoorOpen },
  ] },
  { label: 'Cadastros', icon: BookOpen, children: [
    { to: '/contas', label: 'Plano de contas', icon: BookOpen },
    { to: '/terceiros', label: 'Terceiros', icon: Contact },
    { to: '/diarios', label: 'Diários', icon: Landmark },
    { to: '/documentos', label: 'Documentos', icon: FileText },
    { to: '/lancamentos-tipo', label: 'Lançamentos-tipo', icon: LayoutTemplate },
    { to: '/centros-custo', label: 'Centros de custo', icon: FolderTree },
    { to: '/ativos-fixos', label: 'Ativos fixos', icon: Factory },
    { to: '/series', label: 'Séries', icon: ListOrdered },
    { to: '/taxonomias', label: 'Taxonomias', icon: Tags },
  ] },
  { label: 'Dados', icon: Database, children: [
    { to: '/importar', label: 'Importar SAF-T', icon: FileUp },
    { to: '/exportar-saft', label: 'Exportar SAF-T', icon: FileDown },
    { to: '/compras-efatura', label: 'Compras e-Fatura', icon: Receipt },
    { to: '/caixa-entrada', label: 'Caixa de entrada', icon: Inbox },
    { to: '/portal-cliente', label: 'Portal do cliente', icon: Globe },
    { to: '/cruzamento', label: 'Cruzamento', icon: ArrowLeftRight },
  ] },
  { label: 'Administração', icon: Building2, children: [
    { to: '/organizacao', label: 'Organização', icon: Building2 },
    { to: '/grupos-permissoes', label: 'Grupos de permissões', icon: KeyRound },
  ] },
]

/** O nó (ou algum descendente) é o ecrã aberto? */
export function isNodeActive(node: NavNode, pathname: string): boolean {
  if (node.to) return node.end ? pathname === node.to : pathname === node.to || pathname.startsWith(`${node.to}/`)
  return node.children?.some((child) => isNodeActive(child, pathname)) ?? false
}

// A permissão que cada ecrã exige para aparecer no menu (a mesma que a API exige para o consultar).
// Os ecrãs que não estão aqui aparecem sempre.
const screenPermissions: Record<string, string> = {
  '/tarefas': 'tasks.view', '/movimentos': 'movements.view', '/lancamentos-sugeridos': 'suggested.view', '/bancos': 'banks.view', '/arquivo': 'archive.view',
  '/livro-diario': 'reports.view', '/balancete': 'reports.view', '/resultados': 'reports.view', '/balanco': 'reports.view', '/demonstracoes': 'reports.view',
  '/contas-correntes': 'parties.view', '/saldos-terceiros': 'reports.view', '/idade-saldos': 'reports.view',
  '/mapa-analitico': 'costcenters.view', '/mapa-depreciacoes': 'fixedassets.view', '/mapas': 'custom.view', '/listagens': 'custom.view',
  '/obrigacoes': 'obligations.view', '/consola-obrigacoes': 'obligations.view',
  '/iva': 'vat.view', '/apuramento-iva': 'vat.view', '/declaracao-iva': 'vat.view', '/declaracao-recapitulativa': 'vat.view',
  '/config-iva': 'vatconfig.view', '/codigos-iva': 'vatconfig.view',
  '/estimativa-irc': 'irc.view', '/modelo-22': 'irc.view', '/ies': 'irc.view',
  '/retencoes': 'withholdings.view', '/dossier-fiscal': 'dossier.view', '/mapas-dossier': 'dossier.view', '/comunicacao-at': 'at.view',
  '/assistente': 'ai.use', '/fecho': 'closing.view',
  '/variacoes': 'management.view', '/anomalias': 'management.view', '/evolucao': 'management.view', '/analises': 'management.view',
  '/mapa-exploracao': 'management.view', '/racios': 'management.view', '/orcamento': 'budget.view', '/previsao-tesouraria': 'management.view',
  '/relatorio-empresario': 'management.view', '/relatorios-gestao': 'management.view', '/relatorio-banca': 'management.view',
  '/apuramento': 'yearend.view', '/abertura': 'yearend.view',
  '/contas': 'accounts.view', '/terceiros': 'parties.view', '/diarios': 'accounts.view', '/documentos': 'accounts.view', '/lancamentos-tipo': 'templates.view',
  '/centros-custo': 'costcenters.view', '/ativos-fixos': 'fixedassets.view', '/series': 'series.view', '/taxonomias': 'accounts.view',
  '/importar': 'saftimport.import', '/exportar-saft': 'saftexport.export', '/compras-efatura': 'efatura.view', '/caixa-entrada': 'archive.view', '/portal-cliente': 'portal.view', '/cruzamento': 'reports.view',
}

const adminScreens = new Set(['/grupos-permissoes'])

/** A árvore só com o que o utilizador pode ver; os grupos que ficam vazios desaparecem. */
export function visibleNodes(nodes: NavNode[], can: (permission: string) => boolean, canManage: boolean): NavNode[] {
  return nodes.flatMap((node) => {
    if (node.children) {
      const children = visibleNodes(node.children, can, canManage)
      return children.length > 0 ? [{ ...node, children }] : []
    }
    if (node.to && adminScreens.has(node.to)) return canManage ? [node] : []
    const permission = node.to ? screenPermissions[node.to] : undefined
    return !permission || can(permission) ? [node] : []
  })
}
