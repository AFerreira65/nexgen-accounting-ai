import { useEffect, useState, type ReactNode } from 'react'
import { LoginPage, type AuthenticatedUser } from '../pages/LoginPage'
import { ProducerShell } from './ProducerShell'
import { WorkContextProvider } from './WorkContext'

type Status = 'checking' | 'authenticated' | 'unauthenticated'

/**
 * Exige sessão antes de mostrar a app (ADR-0011) — chama `/api/v1/auth/me` ao
 * arrancar e mostra o `LoginPage` sem sessão válida. Em modo de teste (onde
 * mais de 50 testes montam `<App/>` e esperam o painel logo ali, sem esperar
 * por nenhum pedido assíncrono) fica sempre "autenticado" como o utilizador
 * de demonstração, tal como `WorkContext.loadWorkContext` já faz para o seu
 * próprio pedido.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const isTestMode = import.meta.env.MODE === 'test'
  const [status, setStatus] = useState<Status>(isTestMode ? 'authenticated' : 'checking')
  const [user, setUser] = useState<AuthenticatedUser | null>(null)

  useEffect(() => {
    if (isTestMode) return
    const controller = new AbortController()
    fetch('/api/v1/auth/me', { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) { setStatus('unauthenticated'); return }
        setUser(await response.json() as AuthenticatedUser)
        setStatus('authenticated')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setStatus('unauthenticated')
      })
    return () => controller.abort()
  }, [isTestMode])

  if (status === 'checking') {
    return <div className="login-shell" aria-busy="true" />
  }

  if (status === 'unauthenticated') {
    return <LoginPage onSuccess={(loggedIn) => { setUser(loggedIn); setStatus('authenticated') }} />
  }

  // A conta do produtor não tem empresas: só vê a configuração geral (ADR-0017).
  if (user?.isProducer) {
    return <ProducerShell user={user} />
  }

  return <WorkContextProvider userId={user?.id}>{children}</WorkContextProvider>
}
