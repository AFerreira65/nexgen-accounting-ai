import type { Movement } from './demo-data'

export type PendingPreviewItem = { id: string; date: string; description: string; amount: number }

// Números do estado da fila de trabalho da empresa ativa — usados pela barra
// de topo (sino), pelo ecrã de Tarefas e pelo painel — sem depender do array
// completo de movimentos: numa empresa ligada à base de dados vêm do endpoint
// `.../accounting/movements/summary`, agregados no SQL. O `WorkContext`
// carrega-o uma vez e partilha-o (ver `useMovementsSummary`).
export type MovementsSummary = {
  total: number
  pendingReview: number
  voided: number
  vatPosition: number
  pendingPreview: PendingPreviewItem[]
}

export const EMPTY_MOVEMENTS_SUMMARY: MovementsSummary = {
  total: 0,
  pendingReview: 0,
  voided: 0,
  vatPosition: 0,
  pendingPreview: [],
}

// Mesmo cálculo, no cliente, para a demonstração e para a degradação graciosa
// quando o pedido falha. Fórmula de IVA igual à do resumo de IVA: 2433
// liquidado menos 2432 dedutível.
export function summaryFromMovements(movements: Movement[]): MovementsSummary {
  let dedutivel = 0
  let liquidado = 0
  let voided = 0
  const pending: Movement[] = []
  for (const movement of movements) {
    if (movement.status === 'Anulado') { voided += 1; continue }
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
    voided,
    vatPosition: liquidado - dedutivel,
    pendingPreview: pending.slice(0, 7).map((movement) => ({
      id: movement.id,
      date: movement.date,
      description: movement.description,
      amount: movement.amount,
    })),
  }
}
