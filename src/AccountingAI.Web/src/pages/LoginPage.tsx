import { useState, type FormEvent } from 'react'
import { ShieldCheck } from 'lucide-react'

export type AuthenticatedUser = { id: string; displayName: string; email: string }

export function LoginPage({ onSuccess }: { onSuccess: (user: AuthenticatedUser) => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!email.trim() || !password) {
      setError('Indique o email e a senha.')
      return
    }

    setBusy(true)
    setError('')
    try {
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: email.trim(), password }),
      })
      if (!response.ok) {
        setError('Email ou senha incorretos.')
        return
      }

      onSuccess(await response.json() as AuthenticatedUser)
    } catch {
      setError('Não foi possível ligar à API. Verifique se está a correr.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-shell">
      <form className="panel login-card" onSubmit={(event) => void submit(event)}>
        <div className="login-brand"><span className="brand-mark">A</span><span><strong>Accounting</strong><small>AI</small></span></div>
        <h1>Entrar</h1>
        <p className="mr-hint">Sessão de um utilizador com senha configurada nesta plataforma.</p>
        {error && <p className="save-status error">{error}</p>}
        <label>Email
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="username"
            autoFocus
          />
        </label>
        <label>Senha
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
          />
        </label>
        <button type="submit" className="button button-primary login-submit" disabled={busy}>
          <ShieldCheck size={16} />{busy ? 'A entrar…' : 'Entrar'}
        </button>
      </form>
    </div>
  )
}
