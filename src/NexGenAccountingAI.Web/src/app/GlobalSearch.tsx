import { Search } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import type { Movement } from './demo-data'
import { useDismiss } from './useDismiss'
import { useWorkContext } from './WorkContext'

type Hit = { to: string; label: string; hint: string }

const moveHit = (movement: Movement): Hit => ({
  to: `/movimentos/${movement.id}`,
  label: movement.id.replaceAll('-', ' '),
  hint: `Movimento · ${movement.description}`,
})

// Pesquisa rápida da barra de topo: salta para uma conta, diário ou
// movimento. As contas e os diários vêm dos cadastros já carregados; os
// movimentos, numa empresa ligada à base de dados, de um pedido de
// pesquisa (não do array inteiro). Ctrl/Cmd+K foca o campo; setas
// percorrem; Enter salta para o resultado ativo.
export function GlobalSearch() {
  const { accountingSetup, accountingSource, activeCompany, movements, user } = useWorkContext()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const onDatabase = accountingSource === 'database'

  const [debouncedQuery, setDebouncedQuery] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQuery(query), 250)
    return () => clearTimeout(timer)
  }, [query])

  const [serverMoves, setServerMoves] = useState<Hit[]>([])
  useEffect(() => {
    const raw = debouncedQuery.trim()
    if (!onDatabase || raw.length < 2) { setServerMoves([]); return }
    const controller = new AbortController()
    const params = new URLSearchParams({ page: '1', pageSize: '5', search: raw })
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements?${params}`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as { items: Movement[] }
      })
      .then((body) => setServerMoves(body.items.slice(0, 4).map(moveHit)))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setServerMoves([])
      })
    return () => controller.abort()
  }, [onDatabase, debouncedQuery, activeCompany.companyId, user.id])

  useDismiss(open, wrapRef, () => setOpen(false))

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        inputRef.current?.focus()
        inputRef.current?.select()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  const hits = useMemo<Hit[]>(() => {
    const raw = query.trim()
    if (raw.length < 2) return []
    const q = raw.toLocaleLowerCase('pt-PT')
    const accounts = accountingSetup.accounts
      .filter((item) => item.code.toLowerCase().startsWith(q) || item.name.toLocaleLowerCase('pt-PT').includes(q))
      .slice(0, 5)
      .map<Hit>((item) => ({ to: `/contas/${item.code}`, label: item.code, hint: `Conta · ${item.name}` }))
    const journals = accountingSetup.journals
      .filter((item) => item.code.toLowerCase().startsWith(q) || item.name.toLocaleLowerCase('pt-PT').includes(q))
      .slice(0, 2)
      .map<Hit>((item) => ({ to: `/diarios/${item.code}`, label: item.code, hint: `Diário · ${item.name}` }))
    const moves = onDatabase
      ? serverMoves
      : movements
          .filter((item) => item.id.toLowerCase().includes(q)
            || item.description.toLocaleLowerCase('pt-PT').includes(q)
            || (item.document ?? '').toLocaleLowerCase('pt-PT').includes(q))
          .slice(0, 4)
          .map(moveHit)
    return [...accounts, ...journals, ...moves].slice(0, 9)
  }, [query, accountingSetup.accounts, accountingSetup.journals, movements, onDatabase, serverMoves])

  const go = (hit: Hit | undefined) => {
    if (!hit) return
    navigate(hit.to)
    setQuery('')
    setOpen(false)
    inputRef.current?.blur()
  }

  const showMenu = open && query.trim().length >= 2

  return (
    <div className="global-search-wrap" ref={wrapRef}>
      <label className="global-search">
        <Search size={18} />
        <span className="sr-only">Pesquisar conta, movimento ou diário</span>
        <input
          ref={inputRef}
          role="combobox"
          aria-expanded={showMenu && hits.length > 0}
          aria-autocomplete="list"
          value={query}
          placeholder="Pesquisar conta, movimento ou diário…"
          onChange={(event) => { setQuery(event.target.value); setActive(0); setOpen(true) }}
          onFocus={() => setOpen(true)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') { setOpen(false); inputRef.current?.blur() }
            else if (event.key === 'ArrowDown') { event.preventDefault(); setActive((index) => Math.min(index + 1, hits.length - 1)) }
            else if (event.key === 'ArrowUp') { event.preventDefault(); setActive((index) => Math.max(index - 1, 0)) }
            else if (event.key === 'Enter') { event.preventDefault(); go(hits[active]) }
          }}
        />
        <kbd>Ctrl K</kbd>
      </label>
      {showMenu && <div className="global-search-menu" role="listbox" aria-label="Resultados da pesquisa">
        {hits.length === 0
          ? <p className="empty">Sem resultados para «{query.trim()}».</p>
          : hits.map((hit, index) => <button
              key={hit.to}
              type="button"
              role="option"
              aria-selected={index === active}
              className={index === active ? 'active' : ''}
              onMouseEnter={() => setActive(index)}
              onClick={() => go(hit)}
            ><strong>{hit.label}</strong><small>{hit.hint}</small></button>)}
      </div>}
    </div>
  )
}
