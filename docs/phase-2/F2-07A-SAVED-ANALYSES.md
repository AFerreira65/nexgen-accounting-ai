# F2-07A — Definições guardadas e catálogo de campos

Primeiro sub-incremento do [F2-07](F2-07-CONFIGURABLE-REPORTS.md)
([`ADR-0005`](../architecture-decisions/ADR-0005-REPORT-ENGINE.md)). Guarda e
valida definições de análises; ainda não as corre (isso é o F2-07B).

## Regras

- **Esquema `reporting`.** `reporting.SavedAnalysis`
  (`Id, CompanyId, Name, Description?, DefinitionJson, RowVersion` + as quatro
  colunas de auditoria de F2-06A), única por `(CompanyId, Name)`. Migração
  `20260908154347_SavedAnalysis` (aditiva, cria o esquema).
- **`AnalysisDefinition`** (`Application/Reporting`) — `{ Source, Columns[],
  Filters[{ Field, Operator, Value }], GroupBy[], Sort[{ Field, Descending }],
  Aggregates[{ Field, Function }], IncludeGrandTotal }`. `AnalysisSource`
  começa com `Movimentos`. `AnalysisJson` serializa em `camelCase` com enums
  como texto (opções locais ao módulo, para não mexer no JSON global da API).
- **`AnalysisFieldCatalog`** — em código, não editável. Para os **Movimentos**:
  data, ano, mês, diário, nome do diário, referência, tipo de documento, nº de
  documento, descrição, estado, origem, conta, nome da conta, descrição da
  linha, débito, crédito, valor (débito − crédito). Cada campo diz o tipo
  (texto / número / dinheiro / data) e se é agrupável / agregável. `Validate`
  devolve as mensagens de erro em pt-PT (coluna desconhecida, agrupar por campo
  não agrupável, agregar campo não agregável exceto `count`, operador que não
  se aplica ao tipo, sem colunas).
- **`SavedAnalysisService`** (`Infrastructure.SqlServer`) — CRUD por empresa,
  valida contra o catálogo e o nome único ao gravar; devolve `NotFound`
  (sem acesso / registo inexistente) ou a lista de erros.
- **API:** `GET .../analyses/catalog` (o catálogo, sem empresa),
  `GET .../companies/{companyId}/analyses`, `POST .../analyses`,
  `PUT .../analyses/{id}`, `DELETE .../analyses/{id}`. As definições entram e
  saem com as opções de JSON do módulo.
- A tabela entra na auditoria (F2-06A) e no `record-info` (F2-06B, nome lógico
  `analises`) e no dicionário de dados (F2-06C).

## Superfície de validação local

```
GET  /api/v1/development/analyses/catalog
POST /api/v1/development/users/{userId}/companies/{companyId}/analyses
```

## Validação efetuada

- `NexGenAccountingAI.Infrastructure.Files.Tests`: sete testes novos
  (`AnalysisFieldCatalogTests`) — definição bem formada não gera erros; rejeita
  coluna desconhecida, agrupamento não agrupável, agregado não agregável
  (exceto `count`), operador inválido, sem colunas; a definição faz round-trip
  pelo JSON com enums em texto.
- `NexGenAccountingAI.IntegrationTests`: teste novo `SavedAnalysisTests` — rejeita a
  definição com campo desconhecido; grava a válida com `CreatedBy` carimbado;
  recusa nome duplicado; lista, actualiza e apaga; sem acesso devolve `null`.
  O teste da migração aplica `SavedAnalysis` duas vezes.
- `NexGenAccountingAI.Api.Tests`: teste novo — o catálogo traz a origem `Movimentos`,
  o campo `accountCode` e os operadores por tipo; a lista da empresa devolve um
  array; empresa sem acesso dá `404`.
- `dotnet build`/`dotnet test` na solução inteira (cento e quarenta e quatro
  testes .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI`: criar análises pelos endpoints,
  listá-las, `record-info?table=analises` devolve o autor «António Ferreira
  (Demonstração)»; linhas de teste removidas.

## Fora deste incremento

- correr as análises (`AnalysisRunner` + `.../analyses/run`) — F2-07B;
- o ecrã «Análises» — F2-07C;
- os modelos de impressão dos relatórios fixos — F2-07D.
