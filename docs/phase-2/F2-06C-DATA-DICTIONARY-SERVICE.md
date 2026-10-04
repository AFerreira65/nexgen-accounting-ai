# F2-06C — Serviço e endpoints do dicionário de dados

Terceiro sub-incremento do [F2-06](F2-06-DATA-DICTIONARY-AND-RECORD-INFO.md).
Gera a estrutura das tabelas da base a partir do modelo do EF Core, enriquecida
com texto pt-PT.

## Regras

- `DataDictionaryService` (`Infrastructure.SqlServer`) — percorre
  `context.Model.GetEntityTypes()` (só metadados, sem consultar a base) e produz:
  - **por tabela:** esquema, nome, propósito, contagem de colunas;
  - **por coluna:** nome, tipo .NET (amigável, ex.: `Guid?`), tipo SQL
    (`nvarchar(500)`, `decimal(18,2)`, `date`, `rowversion`, …), anulável,
    comprimento máximo, precisão/escala, se é chave primária, se é chave
    estrangeira (→ `esquema.tabela` alvo), se está sob índice único ou
    indexada.
  - Numa FK **composta** (ex.: `(CompanyId, JournalId) → Journals`), só a
    última coluna «aponta» para o principal; a coluna partilhada de empresa
    fica como coluna indexada, não como FK.
  - Ordem das colunas: chave primária primeiro, depois as FK, depois as
    restantes.
- `DataDictionaryText` (`Infrastructure.SqlServer`) — mapa mantido à mão: o
  propósito de cada tabela e a descrição de cada coluna, com um conjunto
  genérico (`Id`, `CompanyId`, `RowVersion`, `CreatedAtUtc`/`CreatedBy`/
  `ModifiedAtUtc`/`ModifiedBy`, `IsActive`) usado quando não há texto
  específico.
- `GET /api/v1/development/data-dictionary` → `{ tables: [...] }`.
- `GET /api/v1/development/data-dictionary/{schema}/{table}` → o detalhe das
  colunas, ou `404`. Endpoints planos (a estrutura é global, não é âmbito de
  empresa).

Sem alteração de esquema nem da fase do `/foundation` (`F2-01`).

## Superfície de validação local

```
GET /api/v1/development/data-dictionary
GET /api/v1/development/data-dictionary/accounting/JournalEntries
```

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: três testes novos (`DataDictionaryTests`) —
  lista todas as tabelas mapeadas com propósito não vazio; o detalhe de
  `accounting.Accounts` traz a PK primeiro, `CompanyId` como FK para
  `organization.Companies`, `Code` com comprimento e descrição, cobertura de
  descrições ≥ 70 %; tabela desconhecida devolve `null`.
- `NexGenAccountingAI.Api.Tests`: um teste novo — `GET /data-dictionary` lista
  `Accounts` e o esquema `organization`; o detalhe traz `columns`,
  `primaryKey:true` e `organization.Companies`; tabela desconhecida dá `404`.
- `dotnet build`/`dotnet test` na solução inteira (cento e trinta e cinco
  testes .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI`: `data-dictionary/accounting/
  JournalEntries` devolve os tipos SQL resolvidos, `JournalId → Journals`,
  `DocumentTypeId → AccountingDocumentTypes`, `CompanyId` como coluna indexada.

## Fora deste incremento

- o menu «Opções», o `RecordContext` e a página `/dicionario-de-dados` — F2-06D;
- a impressão / PDF — F2-06E.
