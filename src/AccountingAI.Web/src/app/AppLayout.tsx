import {
  Bell,
  BookOpen,
  BookText,
  Building2,
  CalendarCheck,
  ChevronDown,
  CircleGauge,
  ClipboardCheck,
  DoorOpen,
  FileText,
  FileUp,
  Landmark,
  ListOrdered,
  Receipt,
  Scale,
  Search,
  Settings,
  Tags,
  TrendingUp,
  Users,
  WalletCards,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useWorkContext } from './WorkContext'

const links = [
  { to: '/', label: 'Visão geral', icon: CircleGauge, end: true },
  { to: '/movimentos', label: 'Movimentos', icon: WalletCards },
  { to: '/livro-diario', label: 'Livro diário', icon: BookText },
  { to: '/balancete', label: 'Balancete', icon: Scale },
  { to: '/resultados', label: 'Resultados', icon: TrendingUp },
  { to: '/balanco', label: 'Balanço', icon: Scale },
  { to: '/iva', label: 'Resumo de IVA', icon: Receipt },
  { to: '/contas-correntes', label: 'Contas correntes', icon: Users },
  { to: '/apuramento', label: 'Apuramento', icon: CalendarCheck },
  { to: '/abertura', label: 'Abertura', icon: DoorOpen },
  { to: '/contas', label: 'Plano de contas', icon: BookOpen },
  { to: '/diarios', label: 'Diários', icon: Landmark },
  { to: '/documentos', label: 'Documentos', icon: FileText },
  { to: '/series', label: 'Séries', icon: ListOrdered },
  { to: '/taxonomias', label: 'Taxonomias', icon: Tags },
  { to: '/importar', label: 'Importar SAF-T', icon: FileUp },
  { to: '/organizacao', label: 'Organização', icon: Building2 },
  { to: '/tarefas', label: 'Tarefas', icon: ClipboardCheck },
]

export function AppLayout() {
  const { activeCompany, companies, saveSettings, selectCompany, source, user } = useWorkContext()
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const [periodOpen, setPeriodOpen] = useState(false)
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

  return (
    <div className="product-shell">
      <aside className="sidebar">
        <NavLink className="brand" to="/" aria-label="Accounting AI — início">
          <span className="brand-mark">A</span>
          <span><strong>Accounting</strong><small>AI</small></span>
        </NavLink>
        <nav className="primary-nav" aria-label="Navegação principal">
          {links.map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}>
              <Icon size={19} strokeWidth={1.8} />
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-footer">
          <NavLink className="nav-item" to="/definicoes"><Settings size={19} />Definições</NavLink>
          <div className="user-card"><span className="avatar">{userInitials}</span><span><strong>{user.displayName.replace(' (Demonstração)', '')}</strong><small>{activeCompany.role}</small></span></div>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <div className="company-switcher-wrap">
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
          <label className="global-search">
            <Search size={18} />
            <span className="sr-only">Pesquisar</span>
            <input placeholder="Pesquisar conta, movimento ou documento…" />
            <kbd>Ctrl K</kbd>
          </label>
          <div className="topbar-actions">
            <div className="period-selector-wrap">
              <button className="period-selector" type="button" aria-expanded={periodOpen} onClick={() => setPeriodOpen((open) => !open)}>Exercício {activeCompany.settings.defaultFiscalYear ?? 'atual'} <ChevronDown size={15} /></button>
              {periodOpen && <div className="period-menu" role="menu" aria-label="Escolher exercício">
                {yearOptions.map((year) => <button key={year} type="button" role="menuitem" className={year === activeCompany.settings.defaultFiscalYear ? 'selected' : ''} onClick={() => pickYear(year)}>{year}</button>)}
              </div>}
            </div>
            <button className="icon-button" type="button" aria-label="Notificações"><Bell size={19} /><span className="notification-dot" /></button>
          </div>
        </header>
        <main className="content-area"><Outlet /></main>
      </div>
    </div>
  )
}
