import { Bell, ChevronDown, CircleAlert, LogOut, Menu, Settings } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { clearLoadFailure, useLoadFailure } from './loadFailure'
import { companyRoleLabel } from './companyRoles'
import { GlobalSearch } from './GlobalSearch'
import { OptionsMenu } from './OptionsMenu'
import { SideMenu } from './SideMenu'
import { useDismiss } from './useDismiss'
import { useMovementsSummary, useWorkContext } from './WorkContext'

export function AppLayout() {
  const { activeCompany, companies, saveSettings, selectCompany, source, taxonomyReview, user } = useWorkContext()
  const movementsSummary = useMovementsSummary()
  const loadFailed = useLoadFailure()
  const location = useLocation()
  // O aviso de falha de leitura é do ecrã onde aconteceu: muda-se de ecrã ou de empresa, limpa-se.
  useEffect(() => { clearLoadFailure() }, [location.pathname, activeCompany.companyId])
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const [periodOpen, setPeriodOpen] = useState(false)
  // A barra mostra só as secções; em ecrãs estreitos é uma gaveta que abre pelo
  // botão da barra de topo. O lado vem das preferências do utilizador.
  const [drawerOpen, setDrawerOpen] = useState(false)
  const menuSide = activeCompany.settings.menuOnRight ? 'right' : 'left'
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
    <div className={`product-shell${menuSide === 'right' ? ' menu-right' : ''}`}>
      {drawerOpen && <div className="sidebar-backdrop" onClick={closeDrawer} />}
      <aside className={`sidebar${drawerOpen ? ' open' : ''}`}>
        <div className="sidebar-head">
          <NavLink className="brand" to="/" aria-label="NexGen Accounting AI — início" onClick={closeDrawer}>
            <span className="brand-mark">A</span>
          </NavLink>
        </div>
        <SideMenu side={menuSide} onNavigate={closeDrawer} />
        <div className="sidebar-footer">
          <NavLink className={({ isActive }) => `menu-row${isActive ? ' active' : ''}`} to="/definicoes" title="Definições" onClick={closeDrawer}><Settings size={21} strokeWidth={1.8} /><span>Definições</span></NavLink>
          <button type="button" className="menu-row logout-item" title="Sair" onClick={logOut}><LogOut size={21} strokeWidth={1.8} /><span>Sair</span></button>
          <div className="user-card" title={`${user.displayName.replace(' (Demonstração)', '')} · ${companyRoleLabel(activeCompany.role)}${activeCompany.role === 'Viewer' ? ' · só consulta, sem alterações' : ''}`}><span className="avatar">{userInitials}</span></div>
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
        <main className="content-area">
          {loadFailed && <p className="save-status error load-failure" role="alert"><CircleAlert size={15} /> Não foi possível ler os dados deste ecrã no servidor, por isso não são mostrados valores. <button className="text-link" type="button" onClick={() => window.location.reload()}>Tentar de novo</button></p>}
          <Outlet />
        </main>
      </div>
    </div>
  )
}
