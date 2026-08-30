# F1-06A — Diários e tipos de documento editáveis

## Objetivo

Completar a manutenção das tabelas mestras contabilísticas: os diários e os tipos
de documento passam de consulta a editáveis, tal como já acontece com as contas
desde a F1-04B.

Ficam disponíveis:

- criação de diários próprios da empresa e renomeação/ativação/desativação dos existentes;
- criação de tipos de documento com diário predefinido validado, renomeação, mudança de diário predefinido e ativação/desativação;
- guardas: um diário com movimentos ou usado como predefinido de um documento ativo não pode ser desativado; o diário predefinido de um documento tem de existir e estar ativo.

## Regras de domínio

- `AccountingJournal` ganha `Rename`, `Deactivate` e `Reactivate`.
- `AccountingDocumentType` ganha `Rename`, `ChangeDefaultJournal`, `Deactivate` e `Reactivate`.
- `AccountingMasterDataService` acrescenta `CreateJournalAsync`/`UpdateJournalAsync` e `CreateDocumentTypeAsync`/`UpdateDocumentTypeAsync`, sempre filtrados por `CompanyId` e em transação, com código único por empresa.
- A desativação de um diário verifica `JournalEntries` e tipos de documento ativos que o usem como predefinido.
- A resolução do diário predefinido de um tipo de documento exige que exista e esteja ativo na mesma empresa.

Não há alteração de esquema. Os diários e os tipos de documento continuam a ser
copiados do catálogo base versionado (8 diários, 11 documentos por norma) no
arranque de cada empresa; este incremento só adiciona a manutenção.

## Superfície de validação local

Endpoints só do ambiente `Development`, com verificação da associação ativa
utilizador–empresa:

```text
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/journals
PUT  /api/v1/development/users/{userId}/companies/{companyId}/accounting/journals/{code}
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/documents
PUT  /api/v1/development/users/{userId}/companies/{companyId}/accounting/documents/{code}
```

Erros de validação do domínio devolvem `ValidationProblem` em Português.

Na PWA, os ecrãs **Diários** e **Documentos contabilísticos** ganham "Novo" e
"Editar", escrevendo pela API quando ligados ao SQL Server e operando sobre o
conjunto de demonstração caso contrário.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes de domínio para renomeação, mudança de diário predefinido e ativação/desativação;
- teste de integração SQL Server LocalDB: criação de diário e documento, recusa de duplicados, recusa de desativar um diário com movimentos e um diário usado como predefinido, diário predefinido inexistente e mudança de diário de um documento;
- teste de API para as recusas dos novos endpoints, sem escrever na base configurada;
- lint, dez testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- ecrãs de empresas e de utilizadores/associações;
- catálogo completo de taxonomias por norma;
- criação, edição e conferência de movimentos;
- alteração do código de um registo já criado.

Os próximos incrementos são: ecrãs de empresas e utilizadores (a criação de
empresa aplica o plano standard da norma), o catálogo completo de taxonomias da
Portaria n.º 302/2016 por norma, e o ecrã de lançamento de movimentos.
