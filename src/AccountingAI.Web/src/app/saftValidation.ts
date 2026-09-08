import type { SaftValidation, SaftValidationIssue, SaftValidationSeverity } from './demo-data'

// Rótulos pt-PT para o relatório de validação do SAF-T.
export const SAFT_SEVERITY_LABELS: Record<SaftValidationSeverity, string> = {
  Error: 'Erro',
  Warning: 'Aviso',
  Information: 'Informação',
}

const SCOPE_LABELS: Record<string, string> = {
  Header: 'Cabeçalho',
  MasterFiles: 'Ficheiros mestre',
  GeneralLedgerEntries: 'Movimentos',
  Ficheiro: 'Ficheiro',
}

export function saftScopeLabel(scope: string): string {
  return SCOPE_LABELS[scope] ?? scope
}

const SEVERITY_ORDER: SaftValidationSeverity[] = ['Error', 'Warning', 'Information']

export function issuesBySeverity(validation: SaftValidation): { severity: SaftValidationSeverity; issues: SaftValidationIssue[] }[] {
  return SEVERITY_ORDER
    .map((severity) => ({ severity, issues: validation.issues.filter((issue) => issue.severity === severity) }))
    .filter((group) => group.issues.length > 0)
}

export function saftValidationBlocksImport(validation: SaftValidation | null): boolean {
  return (validation?.counts.error ?? 0) > 0
}
