import { LogOut } from 'lucide-react'
import type { AuthenticatedUser } from '../pages/LoginPage'
import { AiKeyPanel } from '../pages/AiKeyPanel'
import { AiUsagePanel } from '../pages/AiUsagePanel'
import { MailOAuthAppsPanel } from '../pages/MailOAuthAppsPanel'
import { ProducerSettingsPage } from '../pages/ProducerSettingsPage'

/**
 * O que a conta do produtor vê (ADR-0017): só a configuração geral da aplicação. Não tem empresas,
 * por isso não carrega o contexto de trabalho nem o menu da aplicação — o menu de configuração
 * não existe para mais ninguém.
 */
export function ProducerShell({ user }: { user: AuthenticatedUser }) {
  const logOut = () => {
    void fetch('/api/v1/auth/logout', { method: 'POST' }).finally(() => window.location.reload())
  }

  return <div className="producer-shell">
    <header className="producer-bar">
      <div className="login-brand"><span className="brand-mark">A</span><span><strong>Accounting</strong><small>AI</small></span></div>
      <span className="producer-role">Produtor</span>
      <span className="producer-user">{user.displayName}</span>
      <button type="button" className="button" onClick={logOut}><LogOut size={15} />Sair</button>
    </header>
    <main className="producer-main"><ProducerSettingsPage /><AiKeyPanel /><AiUsagePanel /><MailOAuthAppsPanel /></main>
  </div>
}
