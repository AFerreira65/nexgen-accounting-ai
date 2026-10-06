/** Os tipos e os rótulos das listagens à medida (F4D-01): espelham a API; os códigos nunca se mostram. */

export type ListingFieldType = 'text' | 'date' | 'integer' | 'money'

export type CatalogField = {
  key: string
  label: string
  type: ListingFieldType
  filterable: boolean
  sortable: boolean
  groupable: boolean
  measure: boolean
  operators: string[]
}

export type CatalogSource = { key: string; label: string; description: string; fields: CatalogField[] }
export type ListingCatalog = { sources: CatalogSource[]; dateTokens: string[] }

export type ListingCondition = { field: string; operator: string; value?: string | null; value2?: string | null }
export type ListingFilterGroup = { op: 'and' | 'or'; conditions?: ListingCondition[]; groups?: ListingFilterGroup[] }
export type ListingSort = { field: string; descending: boolean }
export type ListingParameter = { name: string; label: string; type: 'date' | 'text' | 'number'; default?: string | null }

export type ListingDefinition = {
  source: string
  columns: string[]
  filter?: ListingFilterGroup | null
  sort?: ListingSort[] | null
  groupBy?: string[] | null
  parameters?: ListingParameter[] | null
}

export type ListingScope = 'private' | 'company' | 'firm'

export type SavedListing = {
  id: string
  name: string
  description: string | null
  scope: ListingScope
  isFavorite: boolean
  mine: boolean
  definition: ListingDefinition
}

export type ListingColumn = { key: string; label: string; type: ListingFieldType; measure: boolean }
export type ListingGroup = { key: string | null; label: string; count: number; sums: Record<string, number> }
export type ListingRow = { cells: (string | number | null)[]; link: { entryReference: string | null; partyId: string | null } }
export type ListingResult = {
  kind: 'groups' | 'rows'
  columns: ListingColumn[]
  groupBy: string[]
  groups: ListingGroup[]
  rows: ListingRow[]
  totalRows: number
  totals: Record<string, number>
  warnings: string[]
}

export const OPERATOR_LABELS: Record<string, string> = {
  contains: 'contém',
  notcontains: 'não contém',
  eq: 'é igual a',
  ne: 'é diferente de',
  starts: 'começa por',
  empty: 'está vazio',
  notempty: 'não está vazio',
  gt: 'é maior que',
  ge: 'é maior ou igual a',
  lt: 'é menor que',
  le: 'é menor ou igual a',
  between: 'está entre',
}

export const SCOPE_LABELS: Record<ListingScope, string> = {
  private: 'Só eu',
  company: 'Esta empresa',
  firm: 'Todo o gabinete',
}

export const PARAMETER_TYPE_LABELS: Record<ListingParameter['type'], string> = { date: 'Data', text: 'Texto', number: 'Número' }

export const DATE_TOKEN_LABELS: Record<string, string> = {
  hoje: 'Hoje',
  'inicio-mes': 'Início do mês',
  'fim-mes': 'Fim do mês',
  'inicio-mes-anterior': 'Início do mês anterior',
  'fim-mes-anterior': 'Fim do mês anterior',
  'inicio-ano': 'Início do ano',
  'fim-ano': 'Fim do ano',
  'inicio-ano-anterior': 'Início do ano anterior',
  'fim-ano-anterior': 'Fim do ano anterior',
}

export const MAX_GROUP_LEVELS = 3

export function emptyDefinition(source: CatalogSource): ListingDefinition {
  const preferred = ['data', 'lancamento', 'conta', 'terceiro', 'descricao', 'debito', 'credito']
  return {
    source: source.key,
    columns: preferred.filter((key) => source.fields.some((field) => field.key === key)),
    filter: null,
    sort: [],
    groupBy: [],
    parameters: [],
  }
}

/** Uma condição nova para o campo: o primeiro operador que o tipo admite. */
export function newCondition(field: CatalogField): ListingCondition {
  return { field: field.key, operator: field.operators[0], value: '', value2: '' }
}

/** Cópia de uma definição para editar sem mexer na guardada. */
export function cloneDefinition(definition: ListingDefinition): ListingDefinition {
  return JSON.parse(JSON.stringify(definition)) as ListingDefinition
}
