export type MovementLine = {
  account: string
  accountName: string
  description: string
  debit: number
  credit: number
}

export type Movement = {
  id: string
  date: string
  journalId: string
  journalName: string
  document: string
  description: string
  status: 'Conferido' | 'Por conferir'
  amount: number
  lines: MovementLine[]
}

export const movements: Movement[] = [
  {
    id: 'COMP-2026-00842',
    date: '29 ago 2026',
    journalId: 'COMPRAS',
    journalName: 'Diário de Compras',
    document: 'FT 2026/418 · Norte Office, Lda.',
    description: 'Aquisição de material de escritório',
    status: 'Conferido',
    amount: 1476,
    lines: [
      { account: '6221', accountName: 'Material de escritório', description: 'Base tributável', debit: 1200, credit: 0 },
      { account: '24323', accountName: 'IVA dedutível — Outros bens e serviços', description: 'IVA 23%', debit: 276, credit: 0 },
      { account: '2211001', accountName: 'Norte Office, Lda.', description: 'Conta corrente do fornecedor', debit: 0, credit: 1476 },
    ],
  },
  {
    id: 'VEND-2026-00327',
    date: '28 ago 2026',
    journalId: 'VENDAS',
    journalName: 'Diário de Vendas',
    document: 'FT A/327 · Maré Clara, SA',
    description: 'Prestação de serviços de consultoria',
    status: 'Conferido',
    amount: 3075,
    lines: [
      { account: '2111001', accountName: 'Maré Clara, SA', description: 'Conta corrente do cliente', debit: 3075, credit: 0 },
      { account: '7211', accountName: 'Serviços prestados — Continente', description: 'Base tributável', debit: 0, credit: 2500 },
      { account: '24331', accountName: 'IVA liquidado', description: 'IVA 23%', debit: 0, credit: 575 },
    ],
  },
  {
    id: 'BANC-2026-00196',
    date: '27 ago 2026',
    journalId: 'BANCOS',
    journalName: 'Diário de Bancos',
    document: 'TRF 908433 · Norte Office, Lda.',
    description: 'Pagamento a fornecedor por transferência',
    status: 'Por conferir',
    amount: 1476,
    lines: [
      { account: '2211001', accountName: 'Norte Office, Lda.', description: 'Liquidação do fornecedor', debit: 1476, credit: 0 },
      { account: '1211', accountName: 'Banco Atlântico — DO', description: 'Saída bancária', debit: 0, credit: 1476 },
    ],
  },
]

export function findMovement(id?: string) {
  return movements.find((movement) => movement.id === id)
}

export const euro = new Intl.NumberFormat('pt-PT', { style: 'currency', currency: 'EUR' })
