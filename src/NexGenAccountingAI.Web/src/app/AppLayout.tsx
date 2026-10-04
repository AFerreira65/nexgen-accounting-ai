import {
  BarChart3,
  Bell,
  BookOpen,
  BookText,
  Building2,
  Calculator,
  CalendarCheck,
  ChevronDown,
  CircleGauge,
  Contact,
  ArrowLeftRight,
  ClipboardCheck,
  DoorOpen,
  ChartLine,
  Factory,
  FolderCheck,
  HandCoins,
  Target,
  Hourglass,
  FileDown,
  FileText,
  FileUp,
  FolderTree,
  Landmark,
  LayoutTemplate,
  ListOrdered,
  LogOut,
  Menu,
  PanelLeftClose,
  PanelLeftOpen,
  Percent,
  Receipt,
  Scale,
  Settings,
  ShieldCheck,
  Sparkles,
  Tags,
  TrendingUp,
  Users,
  WalletCards,
  WandSparkles,
  type LucideIcon,
} from 'lucide-react'
import { useCallback, useRef, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { GlobalSearch } from './GlobalSearch'
import { OptionsMenu } from './OptionsMenu'
import { useDismiss } from './useDismiss'
import { useMovementsSummary, useWorkContext } from './WorkContext'

// O menu lateral em grupos de trabalho, pela ordem em que o contabilista os usa.
type NavLinkItem = { to: string; label: string; icon: LucideIcon; end?: boolean }

const navGroups: { label: string; links: NavLinkItem[] }[] = [
  { label: 'Trabalho', links: [
    { to: '/', label: 'Visão geral', icon: CircleGauge, end: true },
    { to: '/tarefas', label: 'Tarefas', icon: ClipboardCheck },
    { to: '/movimentos', label: 'Movimentos', icon: WalletCards },
    { to: '/lancamentos-sugeridos', label: 'Lançamentos sugeridos', icon: WandSparkles },
  ] },
  { label: 'Mapas', links: [
    { to: '/livro-diario', label: 'Livro diário', icon: BookText },
    { to: '/balancete', label: 'Balancete', icon: Scale },
    { to: '/resultados', label: 'Resultados', icon: TrendingUp },
    { to: '/balanco', label: 'Balanço', icon: Scale },
    { to: '/demonstracoes', label: 'Demonstrações financeiras', icon: Landmark },
    { to: '/contas-correntes', label: 'Contas correntes', icon: Users },
    { to: '/saldos-terceiros', label: 'Saldos de terceiros', icon: Contact },
    { to: '/idade-saldos', label: 'Idade de saldos', icon: Hourglass },
    { to: '/mapa-analitico', label: 'Mapa analítico', icon: FolderTree },
    { to: '/mapa-depreciacoes', label: 'Mapa de depreciações', icon: Factory },
  ] },
  { label: 'Fiscal', links: [
    { to: '/iva', label: 'Resumo de IVA', icon: Receipt },
    { to: '/apuramento-iva', label: 'Apuramento de IVA', icon: Receipt },
    { to: '/declaracao-iva', label: 'Declaração de IVA', icon: Receipt },
    { to: '/declaracao-recapitulativa', label: 'Declaração recapitulativa', icon: ArrowLeftRight },
    { to: '/config-iva', label: 'Configuração de IVA', icon: Percent },
    { to: '/codigos-iva', label: 'Códigos de IVA', icon: Percent },
    { to: '/estimativa-irc', label: 'Estimativa de IRC', icon: Calculator },
    { to: '/modelo-22', label: 'Modelo 22', icon: Calculator },
    { to: '/ies', label: 'IES — Anexo A', icon: FileText },
    { to: '/retencoes', label: 'Retenções na fonte', icon: HandCoins },
    { to: '/dossier-fiscal', label: 'Dossier fiscal', icon: FolderCheck },
    { to: '/tabelas-fiscais', label: 'Tabelas fiscais', icon: BookText },
    { to: '/comunicacao-at', label: 'Comunicação com a AT', icon: ShieldCheck },
  ] },
  { label: 'Gestão', links: [
    { to: '/evolucao', label: 'Evolução mensal', icon: ChartLine },
    { to: '/mapa-exploracao', label: 'Mapa de exploração', icon: TrendingUp },
    { to: '/racios', label: 'Rácios', icon: Percent },
    { to: '/orcamento', label: 'Orçamento', icon: Target },
    { to: '/analises', label: 'Análises', icon: BarChart3 },
    { to: '/relatorios-gestao', label: 'Relatórios de gestão', icon: Sparkles },
  ] },
  { label: 'Exercício', links: [
    { to: '/apuramento', label: 'Apuramento', icon: CalendarCheck },
    { to: '/abertura', label: 'Abertura', icon: DoorOpen },
  ] },
  { label: 'Cadastros', links: [
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
  { label: 'Dados', links: [
    { to: '/importar', label: 'Importar SAF-T', icon: FileUp },
    { to: '/exportar-saft', label: 'Exportar SAF-T', icon: FileDown },
    { to: '/compras-efatura', label: 'Compras e-Fatura', icon: Receipt },
    { to: '/cruzamento', label: 'Cruzamento', icon: ArrowLeftRight },
  ] },
  { label: 'Administração', links: [
    { to: '/organizacao', label: 'Organização', icon: Building2 },
  ] },
]

const SIDEBAR_KEY = 'accounting-ai.sidebar-collapsed'

function storedSidebarCollapsed() {
  try { return window.localStorage.getItem(SIDEBAR_KEY) === 'true' }
  catch { return false }
}

export function AppLayout() {
  const { activeCompany, companies, saveSettings, selectCompany, source, taxonomyReview, user } = useWorkContext()
  const movementsSummary = useMovementsSummary()
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const [periodOpen, setPeriodOpen] = useState(false)
  // Recolhido, o menu fica só com os ícones (guardado no navegador); em ecrãs
  // estreitos é uma gaveta que abre pelo botão da barra de topo.
  const [collapsed, setCollapsed] = useState(storedSidebarCollapsed)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const toggleCollapsed = () => setCollapsed((current) => {
    try { window.localStorage.setItem(SIDEBAR_KEY, String(!current)) } catch { /* storage is optional */ }
    return !current
  })
  const closeDrawer = () => setDrawerOpen(false)
  const switcherRef = useRef<HTMLDivElement>(null)
  const periodRef = useRef<HTMLDivElement>(null)
  const closeSwitcher = useCallback(() => setSwitcherOpen(false), [])
  const closePeriod = useCallback(() => setPeriodOpen(false), [])
  useDismiss(switcherOpen, switcherRef, closeSwitcher)
  useDismiss(periodOpen, periodRef, closePeriod)
  const regionNames = { Mainland: 'Continente', Azores: 'Açores', Madeira: 'Madeira' }
  // O exercício é a preferência do utilizador nesta empresa; mudá-lo aqui
  // acerta logo os atalhos de período e os valores por omissão de toda a app.
  const fiscalYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const yearOptions = Array.from({ length: 8 }, (_, index) => fiscalYear + 1 - index)
  const pickYear = (year: number) => {
    setPeriodOpen(false)
    if (year !== activeCompany.settings.defaultFiscalYear) {
      void saveSettings({ ...activeCompany.settings, defaultFiscalYear: year })
    }
  }
  const companyMonogram = activeCompany.legalName.split(/\s+/).slice(0, 2).map((word) => word[0]).join('').toUpperCase()
  const userInitials = user.displayName.split(/\s+/).slice(0, 2).map((word) => word[0]).join('').toUpperCase()
  // O sino leva às Tarefas; o ponto acende quando há trabalho pendente na
  // empresa ativa (movimentos por conferir ou contas de movimento por
  // classificar) — os mesmos itens acionáveis do ecrã de Tarefas.
  const pendingTasks = movementsSummary.pendingReview
    + taxonomyReview.accounts.filter((account) => account.status === 'Unclassified' || account.status === 'SuggestionAvailable').length
  const logOut = () => {
    if (import.meta.env.MODE === 'test') return
    void fetch('/api/v1/auth/logout', { method: 'POST' }).finally(() => window.location.reload())
  }

  return (
    <div className={`product-shell${collapsed ? ' nav-collapsed' : ''}`}>
      {drawerOpen && <div className="sidebar-backdrop" onClick={closeDrawer} />}
      <aside className={`sidebar${drawerOpen ? ' open' : ''}`}>
        <div className="sidebar-head">
          <NavLink className="brand" to="/" aria-label="NexGen Accounting AI — início" onClick={closeDrawer}>
            <span className="brand-mark">A</span>
            <span><strong>Accounting</strong><small>AI</small></span>
          </NavLink>
          <button type="button" className="sidebar-toggle" onClick={toggleCollapsed} aria-pressed={collapsed} aria-label={collapsed ? 'Expandir o menu' : 'Recolher o menu'} title={collapsed ? 'Expandir o menu' : 'Recolher o menu'}>
            {collapsed ? <PanelLeftOpen size={17} /> : <PanelLeftClose size={17} />}
          </button>
        </div>
        <nav className="primary-nav" aria-label="Navegação principal">
          {navGroups.map((group) => <div className="nav-group" key={group.label}>
            <p className="nav-group-label">{group.label}</p>
            {group.links.map(({ to, label, icon: Icon, end }) => (
              <NavLink key={to} to={to} end={end} title={label} onClick={closeDrawer} className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}>
                <Icon size={18} strokeWidth={1.8} />
                <span>{label}</span>
              </NavLink>
            ))}
          </div>)}
        </nav>
        <div className="sidebar-footer">
          <NavLink className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`} to="/definicoes" title="Definições" onClick={closeDrawer}><Settings size={18} strokeWidth={1.8} /><span>Definições</span></NavLink>
          <button type="button" className="nav-item logout-item" title="Sair" onClick={logOut}><LogOut size={18} strokeWidth={1.8} /><span>Sair</span></button>
          <div className="user-card"><span className="avatar">{userInitials}</span><span><strong>{user.displayName.replace(' (Demonstração)', '')}</strong><small>{activeCompany.role}</small></span></div>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <button type="button" className="icon-button nav-drawer-button" onClick={() => setDrawerOpen(true)} aria-label="Abrir o menu"><Menu size={19} /></button>
          <div className="company-switcher-wrap" ref={switcherRef}>
            <button className="company-switcher" type="button" aria-expanded={switcherOpen} onClick={() => setSwitcherOpen((open) => !open)}>
              <span className="company-monogram">{companyMonogram}</span>
              <span><strong>{activeCompany.legalName.replace(' (Demonstração)', '')}</strong><small>{activeCompany.taxRegistrationNumber} · {regionNames[activeCompany.region]} · {source === 'database' ? 'SQL' : 'demo'}</small></span>
              <ChevronDown size={17} />
            </button>
            {switcherOpen && <div className="company-menu" role="menu" aria-label="Escolher empresa">
              {companies.map((company) => <button key={company.companyId} type="button" role="menuitem" className={company.companyId === activeCompany.companyId ? 'selected' : ''} onClick={() => { selectCompany(company.companyId); setSwitcherOpen(false) }}>
                <span className="company-monogram">{company.legalName.split(/\s+/).slice(0, 2).map((word) => word[0]).join('').toUpperCase()}</span>
                <span><strong>{company.legalName.replace(' (Demonstração)', '')}</strong><small>{regionNames[company.region]} · {company.role}</small></span>
              </button>)}
            </div>}
          </div>
          <GlobalSearch />
          <div className="topbar-actions">
            <div className="period-selector-wrap" ref={periodRef}>
              <button className="period-selector" type="button" aria-expanded={periodOpen} onClick={() => setPeriodOpen((open) => !open)}>Exercício {activeCompany.settings.defaultFiscalYear ?? 'atual'} <ChevronDown size={15} /></button>
              {periodOpen && <div className="period-menu" role="menu" aria-label="Escolher exercício">
                {yearOptions.map((year) => <button key={year} type="button" role="menuitem" className={year === activeCompany.settings.defaultFiscalYear ? 'selected' : ''} onClick={() => pickYear(year)}>{year}</button>)}
              </div>}
            </div>
            <OptionsMenu />
            <NavLink className="icon-button" to="/tarefas" title="Tarefas" aria-label={pendingTasks > 0 ? `Tarefas — ${pendingTasks} pendentes` : 'Tarefas'}><Bell size={19} />{pendingTasks > 0 && <span className="notification-dot" />}</NavLink>
          </div>
        </header>
        <main className="content-area"><Outlet /></main>
      </div>
    </div>
  )
}
