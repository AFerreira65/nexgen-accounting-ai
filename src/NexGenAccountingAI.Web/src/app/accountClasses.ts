// Nomes das classes (1 dígito) e subclasses (2 dígitos) do SNC, para os
// relatórios que agrupam contas de movimento pelo início do código.

export const CLASS_NAMES: Record<string, string> = {
  '1': 'Meios financeiros líquidos',
  '2': 'Contas a receber e a pagar',
  '3': 'Inventários e ativos biológicos',
  '4': 'Investimentos',
  '5': 'Capital, reservas e resultados transitados',
  '6': 'Gastos',
  '7': 'Rendimentos',
  '8': 'Resultados',
  '0': 'Contas extrapatrimoniais',
}

export const SUBCLASS_NAMES: Record<string, string> = {
  '11': 'Caixa',
  '12': 'Depósitos à ordem',
  '13': 'Outros depósitos bancários',
  '14': 'Outros instrumentos financeiros',
  '21': 'Clientes',
  '22': 'Fornecedores',
  '23': 'Pessoal',
  '24': 'Estado e outros entes públicos',
  '25': 'Financiamentos obtidos',
  '26': 'Acionistas/sócios',
  '27': 'Outras contas a receber e a pagar',
  '28': 'Diferimentos',
  '29': 'Provisões',
  '31': 'Compras',
  '32': 'Mercadorias',
  '33': 'Matérias-primas, subsidiárias e de consumo',
  '34': 'Produtos acabados e intermédios',
  '35': 'Subprodutos, desperdícios, resíduos e refugos',
  '36': 'Produtos e trabalhos em curso',
  '37': 'Ativos biológicos',
  '38': 'Reclassificação e regularização de inventários',
  '39': 'Adiantamentos por conta de compras',
  '41': 'Investimentos financeiros',
  '42': 'Propriedades de investimento',
  '43': 'Ativos fixos tangíveis',
  '44': 'Ativos intangíveis',
  '45': 'Investimentos em curso',
  '46': 'Ativos não correntes detidos para venda',
  '51': 'Capital',
  '52': 'Ações (quotas) próprias',
  '53': 'Outros instrumentos de capital próprio',
  '54': 'Prémios de emissão',
  '55': 'Reservas',
  '56': 'Resultados transitados',
  '57': 'Ajustamentos em ativos financeiros',
  '58': 'Excedentes de revalorização',
  '59': 'Outras variações no capital próprio',
  '61': 'Custo das mercadorias vendidas e das matérias consumidas',
  '62': 'Fornecimentos e serviços externos',
  '63': 'Gastos com o pessoal',
  '64': 'Gastos de depreciação e de amortização',
  '65': 'Perdas por imparidade',
  '66': 'Perdas por reduções de justo valor',
  '67': 'Provisões do período',
  '68': 'Outros gastos',
  '69': 'Gastos e perdas de financiamento',
  '71': 'Vendas',
  '72': 'Prestações de serviços',
  '73': 'Variações nos inventários da produção',
  '74': 'Trabalhos para a própria entidade',
  '75': 'Subsídios à exploração',
  '76': 'Reversões',
  '77': 'Ganhos por aumentos de justo valor',
  '78': 'Outros rendimentos',
  '79': 'Juros, dividendos e outros rendimentos similares',
}

export function classNameOf(code: string) {
  return CLASS_NAMES[code.trim().charAt(0)] ?? 'Outras contas'
}

export function subclassNameOf(code: string) {
  const digits = code.trim().slice(0, 2)
  return SUBCLASS_NAMES[digits] ?? `Subclasse ${digits}`
}

export type SubclassLine = { code: string; name: string; amount: number }
export type SubclassGroup = { code: string; name: string; amount: number; lines: SubclassLine[] }

/** Agrupa `{ code, name, amount }` por subclasse de dois dígitos, ordenado. */
export function groupBySubclass(entries: SubclassLine[]): { groups: SubclassGroup[]; total: number } {
  const byGroup = new Map<string, SubclassGroup>()
  for (const entry of entries) {
    if (entry.amount === 0) continue
    const code = entry.code.trim().slice(0, 2)
    let group = byGroup.get(code)
    if (!group) {
      group = { code, name: subclassNameOf(entry.code), amount: 0, lines: [] }
      byGroup.set(code, group)
    }
    group.lines.push(entry)
    group.amount += entry.amount
  }
  const groups = [...byGroup.values()].sort((a, b) => a.code.localeCompare(b.code, 'pt-PT'))
  groups.forEach((group) => group.lines.sort((a, b) => a.code.localeCompare(b.code, 'pt-PT')))
  return { groups, total: groups.reduce((sum, group) => sum + group.amount, 0) }
}
