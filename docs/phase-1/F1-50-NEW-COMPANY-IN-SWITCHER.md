# F1-50 — Empresa nova aparece logo no seletor

## Objetivo

Corrigir: uma empresa criada no ecrã de organização não aparecia no seletor de
empresa (combo do topo), porque não ficava associada a nenhum utilizador e o
contexto de trabalho não recarregava.

## Regras

- `OrganizationAdminService.CreateCompanyAsync` recebe um `associateUserId`
  opcional; o endpoint `POST .../organization/companies` passa o utilizador de
  demonstração, que fica associado à empresa como `Owner` na mesma transação.
- A PWA ganha `refreshWorkContext()` no `WorkContext` (recarrega
  `/work-context/{userId}` e mantém a empresa ativa se ainda existir); o ecrã de
  organização chama-o depois de criar ou editar, pelo que o seletor atualiza sem
  recarregar a página.

## Alteração de esquema

Migração `20260903161831_AssociateDemoUserWithMemberlessCompanies` (SQL de dados):
associa o utilizador de demonstração (`Owner`, ativo) a todas as empresas sem
membros — corrige as empresas já criadas sem associação. Aplicada à base
`accountingAI`.

## Superfície de validação local

Criar uma empresa em **Organização** passa a mostrá-la de imediato no seletor de
empresa do topo, já como empresa ativa selecionável.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; `CreateCompanyAsync(..., associateUserId: DemoUserId)` cria a associação `Owner` ativa;
- teste de API: `/foundation` indica a fase `F1-50`;
- verificação no browser contra a base `accountingAI`: as duas empresas que o utilizador tinha criado sem associação passaram a aparecer no seletor após a migração; criar uma empresa nova no ecrã de organização acrescenta-a ao seletor na hora (como `Owner`), sem recarregar;
- suite .NET completa (65 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- ecrã de organização para gerir associações utilizador–empresa (hoje não há UI de membros);
- criar gabinetes (organizações) pela interface;
- escolher a função ao associar.
