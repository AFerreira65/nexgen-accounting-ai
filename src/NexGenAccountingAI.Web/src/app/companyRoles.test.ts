import { COMPANY_ROLES, companyRoleLabel } from './companyRoles'

describe('funções na empresa', () => {
  it('tem nome em português para cada código da API', () => {
    expect(COMPANY_ROLES.map(companyRoleLabel)).toEqual(['Dono', 'Administrador', 'Contabilista', 'Consulta'])
  })
})
