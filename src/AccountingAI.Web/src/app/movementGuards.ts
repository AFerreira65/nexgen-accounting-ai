import { useEffect, useState } from 'react'
import type { Movement } from './demo-data'
import { useWorkContext } from './WorkContext'

/**
 * Devolve uma função que diz o motivo pelo qual um movimento com aquela data não
 * pode ser conferido, reaberto, corrigido ou anulado — «Exercício apurado» ou
 * «Período bloqueado» —, ou `null` quando não há impedimento. Os lançamentos de
 * apuramento e abertura (`AP`/`AB`) ficam isentos, tal como na API.
 */
export function useMovementGuards(): (movement: Movement) => string | null {
  const { activeCompany, source, user } = useWorkContext()
  const [closedYears, setClosedYears] = useState<number[]>([])
  const [postingLockDate, setPostingLockDate] = useState<string | null>(null)

  useEffect(() => {
    if (source !== 'database') { setClosedYears([]); setPostingLockDate(null); return }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/closed-years`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as number[] : []))
      .then(setClosedYears)
      .catch(() => { /* aviso opcional */ })
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/posting-lock-date`, { signal: controller.signal })
      .then(async (response) => (response.ok ? (await response.json()) as { lockDate: string | null } : { lockDate: null }))
      .then((payload) => setPostingLockDate(payload.lockDate))
      .catch(() => { /* aviso opcional */ })
    return () => controller.abort()
  }, [activeCompany.companyId, source, user.id])

  return (movement: Movement): string | null => {
    if (movement.documentType === 'AP' || movement.documentType === 'AB') return null
    if (closedYears.includes(Number(movement.date.slice(0, 4)))) return 'Exercício apurado'
    if (postingLockDate !== null && movement.date <= postingLockDate) return 'Período bloqueado'
    return null
  }
}
