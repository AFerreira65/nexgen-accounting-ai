import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { ClientPortalApp } from './pages/ClientPortalApp'
import './styles.css'

// O portal do cliente (`/cliente`) é outra aplicação: sem o menu, o contexto de trabalho nem a sessão do gabinete.
const isClientPortal = window.location.pathname === '/cliente' || window.location.pathname.startsWith('/cliente/')

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {isClientPortal ? <ClientPortalApp /> : <App />}
  </StrictMode>,
)

if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/service-worker.js')
  })
}
