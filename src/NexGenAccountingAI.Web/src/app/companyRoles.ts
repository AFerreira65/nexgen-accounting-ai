// As funções de um utilizador numa empresa, com os nomes em português.
// A API usa os códigos do enum `CompanyUserRole`.

export const COMPANY_ROLES = ['Owner', 'Administrator', 'Accountant', 'Viewer'] as const
export type CompanyRole = (typeof COMPANY_ROLES)[number]

const labels: Record<CompanyRole, string> = {
  Owner: 'Dono',
  Administrator: 'Administrador',
  Accountant: 'Contabilista',
  Viewer: 'Consulta',
}

export const companyRoleLabel = (role: string) => labels[role as CompanyRole] ?? role
