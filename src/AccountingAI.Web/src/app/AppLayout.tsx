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
  WalletCards,
} from 'lucide-react'
import { NavLink, Outlet } from 'react-router-dom'

const links = [
  { to: '/', label: 'Visão geral', icon: CircleGauge, end: true },
  { to: '/movimentos', label: 'Movimentos', icon: WalletCards },
  { to: '/contas', label: 'Plano de contas', icon: BookOpen },
  { to: '/diarios', label: 'Diários', icon: Landmark },
  { to: '/documentos', label: 'Documentos', icon: FileText },
  { to: '/tarefas', label: 'Tarefas', icon: ClipboardCheck },
]

export function AppLayout() {
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
          <div className="user-card"><span className="avatar">AF</span><span><strong>António Ferreira</strong><small>Contabilista</small></span></div>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <button className="company-switcher" type="button">
            <span className="company-monogram">AC</span>
            <span><strong>Almeida &amp; Costa, Lda.</strong><small>NIF DEMO-PT-0001 · Continente</small></span>
            <ChevronDown size={17} />
          </button>
          <label className="global-search">
            <Search size={18} />
            <span className="sr-only">Pesquisar</span>
            <input placeholder="Pesquisar conta, movimento ou documento…" />
            <kbd>Ctrl K</kbd>
          </label>
          <div className="topbar-actions">
            <button className="period-selector" type="button">Exercício 2026 <ChevronDown size={15} /></button>
            <button className="icon-button" type="button" aria-label="Notificações"><Bell size={19} /><span className="notification-dot" /></button>
          </div>
        </header>
        <main className="content-area"><Outlet /></main>
      </div>
    </div>
  )
}
