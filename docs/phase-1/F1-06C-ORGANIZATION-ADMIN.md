# F1-06C — Empresas e utilizadores

## Objetivo

Fechar as tabelas mestras organizacionais: até aqui só o seed criava empresas e
utilizadores. Passa a existir manutenção de empresas, utilizadores e associações.

Ficam disponíveis:

- criar uma empresa, que provisiona o plano standard da norma na mesma transação e recusa NIF duplicado;
- renomear, mudar região e moeda, e ativar/desativar uma empresa;
- criar utilizadores (email único), renomear, mudar email e ativar/desativar;
- criar ou alterar a associação utilizador–empresa, com função e estado.

## Regras de domínio

- `Company` ganha `Rename`, `ChangeTaxRegion`, `ChangeBaseCurrency`, `Deactivate` e `Reactivate`. A norma continua bloqueada após o primeiro provisionamento.
- `UserAccount` ganha `Rename`, `ChangeEmail` (revalida o `@` e recalcula o email normalizado) e ativação/desativação.
- `CompanyUser` ganha `ChangeRole` e ativação/desativação.
- `OrganizationAdminService` orquestra as operações: a criação de empresa abre transação, verifica o gabinete, recusa duplicados de NIF, cria a `Company` e provisiona pelo `CompanyAccountingProvisioner`; a criação/edição de utilizador garante email único; `SetMembershipAsync` cria ou atualiza a associação.

## Superfície de validação local

Endpoints só do ambiente `Development`:

```text
GET  /api/v1/development/organization                                   (já existia — leitura)
POST /api/v1/development/organization/companies
PUT  /api/v1/development/organization/companies/{companyId}
POST /api/v1/development/organization/users
PUT  /api/v1/development/organization/users/{userId}
PUT  /api/v1/development/organization/companies/{companyId}/members/{userId}
```

Regiões, normas e funções desconhecidas, duplicados e registos inexistentes
devolvem `ValidationProblem`.

Na PWA, o ecrã **Organização** (rota `/organizacao`) lista empresas e utilizadores
do endpoint de leitura e oferece criação/edição por formulário. A gestão exige
ligação ao SQL Server; em modo de demonstração o ecrã indica-o.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes de domínio para as mutações de `Company`, `UserAccount` e `CompanyUser`;
- teste de integração SQL Server LocalDB: criação de empresa com provisionamento do plano, recusa de NIF e email duplicados, upsert de associação com mudança de função, e renomear/desativar empresa;
- teste de API para as recusas dos novos endpoints, sem escrever na base configurada;
- lint, onze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- autenticação, MFA, sessões e autorização de produção;
- gestão de gabinetes (só existe o gabinete de desenvolvimento);
- convites por email e fluxo de aceitação;
- remoção definitiva de empresas ou utilizadores (apenas desativação);
- edição das preferências por par utilizador–empresa neste ecrã (continuam em **Definições**).

O próximo incremento é o ecrã de lançamento de movimentos.
