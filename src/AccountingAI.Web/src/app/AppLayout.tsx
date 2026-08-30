import {
  Bell,
  BookOpen,
  ChevronDown,
  CircleGauge,
  ClipboardCheck,
  FileText,
  Landmark,
  Search,
  Settings,
  Tags,
  WalletCards,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useWorkContext } from './WorkContext'

const links = [
  { to: '/', label: 'Visão geral', icon: CircleGauge, end: true },
  { to: '/movimentos', label: 'Movimentos', icon: WalletCards },
  { to: '/contas', label: 'Plano de contas', icon: BookOpen },
  { to: '/diarios', label: 'Diários', icon: Landmark },
  { to: '/documentos', label: 'Documentos', icon: FileText },
  { to: '/taxonomias', label: 'Taxonomias', icon: Tags },
  { to: '/tarefas', label: 'Tarefas', icon: ClipboardCheck },
]

export function AppLayout() {
  const { activeCompany, companies, selectCompany, source, user } = useWorkContext()
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const regionNames = { Mainland: 'Continente', Azores: 'Açores', Madeira: 'Madeira' }
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
            <button className="period-selector" type="button">Exercício {activeCompany.settings.defaultFiscalYear ?? 'atual'} <ChevronDown size={15} /></button>
            <button className="icon-button" type="button" aria-label="Notificações"><Bell size={19} /><span className="notification-dot" /></button>
          </div>
        </header>
        <main className="content-area"><Outlet /></main>
      </div>
    </div>
  )
}
