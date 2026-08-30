# F1-04B — Manutenção de contas e validação assistida de taxonomias

## Objetivo

Os ecrãs de cadastros da F1-04A eram apenas de consulta. Este incremento torna o
plano de contas editável e dá à empresa uma leitura acionável da cobertura de
taxonomia, antes de começar a importação de ficheiros SAF-T.

Ficam disponíveis:

- criação de contas próprias da empresa, com validação da conta-mãe;
- renomeação e ativação/desativação de contas existentes, incluindo as copiadas do plano base;
- atribuição e limpeza do código de taxonomia nas contas de movimento;
- aplicação em bloco das correspondências inequívocas do referencial;
- um mapa de revisão que classifica cada conta de movimento como *atribuída*, *com sugestão*, *por atribuir* ou *com referencial diferente* da norma da empresa.

Os diários e os tipos de documento continuam apenas de consulta nesta iteração.

## Regras de domínio

- `LedgerAccount` ganha `Rename`, `Deactivate` e `Reactivate`; a atribuição de taxonomia continua a passar por `AssignTaxonomy`, que exige referência e código em conjunto, recusa contas agregadoras e aplica a regra do referencial `O` (código 1).
- Uma conta de movimento nova tem de indicar uma conta-mãe agregadora existente na mesma empresa, ou nenhuma; o código é único dentro da empresa.
- Uma conta não pode ser desativada enquanto tiver linhas de movimento ou subcontas ativas; não pode ser reativada com a conta-mãe inativa.
- A reaplicação das sugestões do referencial é idempotente: só toca em contas de movimento ativas sem código e uma segunda execução devolve 0.

Não há alteração de esquema: as colunas `Name`, `IsActive`, `ParentCode`,
`AllowsPosting`, `TaxonomyReference` e `TaxonomyCode` do schema `accounting` já
existiam desde a F1-03B/F1-04A.

Este incremento não reproduz um catálogo legal de taxonomias. As únicas sugestões
automáticas são as duas correspondências inequívocas do plano base versionado
(`AccountingTemplateCatalog`): conta 11 → código 1, conta 12 → código 2. As
restantes contas permanecem visivelmente por classificar.

## Superfície de validação local

Endpoints só do ambiente `Development`, todos confirmando primeiro a associação
ativa entre utilizador e empresa:

```text
GET  /api/v1/development/users/{userId}/companies/{companyId}/accounting/taxonomy-review
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/accounts
PUT  /api/v1/development/users/{userId}/companies/{companyId}/accounting/accounts/{code}
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/accounts/apply-taxonomy-suggestions
```

Os erros de validação do domínio são devolvidos como `ValidationProblem` em
Português. Estas rotas usam ainda o utilizador lógico de desenvolvimento e não
substituem autenticação nem autorização de produção.

Na PWA, os ecrãs **Plano de contas** e **Taxonomias** passam a escrever pela API
quando ligados ao SQL Server e a operar sobre o conjunto de demonstração quando a
API local não está disponível, mantendo a indicação explícita da origem.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes de domínio para renomeação, ativação/desativação e limpeza de taxonomia;
- teste de API para o isolamento por empresa das contas e da revisão de taxonomia, criação, recusa de conta-mãe de movimento, recusa de desativação com movimentos e idempotência das sugestões;
- teste de integração SQL Server LocalDB a exercitar `AccountingMasterDataService` e `TaxonomyReviewService` sobre as empresas sintéticas, com a migração aplicada duas vezes;
- lint, sete testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

Esta evidência é local e sintética; não valida parametrizações fiscais,
certificação nem uma instalação de produção.

## Fora deste incremento

- criação, edição e desativação de diários e de tipos de documento pela PWA;
- criação, alteração, aprovação e anulação de movimentos;
- importação do plano de contas ou dos movimentos a partir de SAF-T;
- autenticação, MFA, sessões e autorização de produção;
- um catálogo legal completo de taxonomias e a validação assistida conta a conta contra esse catálogo.

O próximo incremento deve iniciar o fluxo de leitura de SAF-T real definido na
F1-04A: ler o cabeçalho, localizar a empresa pelo NIF, propor a criação quando
não existir, confirmar norma e referencial, provisionar os cadastros e
pré-visualizar antes de importar.
