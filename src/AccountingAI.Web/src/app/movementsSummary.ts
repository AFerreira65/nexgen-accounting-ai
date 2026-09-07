import { useEffect, useMemo, useState } from 'react'
import type { Movement } from './demo-data'
import { useWorkContext } from './WorkContext'

export type PendingPreviewItem = { id: string; date: string; description: string; amount: number }

// Números do estado da fila de trabalho da empresa ativa — usados pela barra
// de topo (sino) e pelo ecrã de Tarefas — sem depender do array completo de
// movimentos: numa empresa ligada à base de dados vêm do endpoint
// `.../accounting/movements/summary`, agregados no SQL.
export type MovementsSummary = {
  total: number
  pendingReview: number
  vatPosition: number
  pendingPreview: PendingPreviewItem[]
}

// Mesmo cálculo, no cliente, para a demonstração e para a degradação graciosa
// quando o pedido falha. Fórmula de IVA igual à do resumo de IVA: 2433
// liquidado menos 2432 dedutível.
function fromMovements(movements: Movement[]): MovementsSummary {
  let dedutivel = 0
  let liquidado = 0
  const pending: Movement[] = []
  for (const movement of movements) {
    if (movement.status === 'Anulado') continue
    if (movement.status === 'Por conferir') pending.push(movement)
    for (const line of movement.lines) {
      if (line.account.startsWith('2432')) dedutivel += line.debit - line.credit
      if (line.account.startsWith('2433')) liquidado += line.credit - line.debit
    }
  }
  pending.sort((a, b) => a.date.localeCompare(b.date) || a.id.localeCompare(b.id))
  return {
    total: movements.length,
    pendingReview: pending.length,
    vatPosition: liquidado - dedutivel,
    pendingPreview: pending.slice(0, 7).map((movement) => ({
      id: movement.id,
      date: movement.date,
      description: movement.description,
      amount: movement.amount,
    })),
  }
}

export function useMovementsSummary(): MovementsSummary {
  const { accountingSource, activeCompany, movements, user } = useWorkContext()
  const clientSummary = useMemo(() => fromMovements(movements), [movements])
  const [summary, setSummary] = useState<MovementsSummary>(clientSummary)

  useEffect(() => {
    if (accountingSource !== 'database') {
      setSummary(clientSummary)
      return
    }
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/accounting/movements/summary`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return await response.json() as MovementsSummary
      })
      .then((body) => setSummary(body))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setSummary(clientSummary)
      })
    return () => controller.abort()
  }, [accountingSource, activeCompany.companyId, user.id, clientSummary])

  return summary
}
