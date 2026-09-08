# F2-06B — Endpoint `record-info`

Segundo sub-incremento do [F2-06](F2-06-DATA-DICTIONARY-AND-RECORD-INFO.md).
Expõe os carimbos de auditoria de [F2-06A](F2-06A-AUDIT-COLUMNS.md) — quem criou
e quem alterou um registo, e quando.

## Regras

- `RecordInfoService` (`Infrastructure.SqlServer`) — um registo estático que
  mapeia um **nome lógico de tabela** para (esquema, tabela, âmbito de empresa)
  e um resolvedor: `movimentos`, `contas`, `diarios`, `documentos` (chave = o
  código), `series`, `compras-efatura`, `validacoes-saft` (chave = `Id`, âmbito
  de empresa), `empresas`, `utilizadores` (chave = `Id`, sem âmbito de
  empresa). Lê as colunas-sombra por projeção (`EF.Property<>`); as entidades
  que só têm `CreatedAtUtc` (`EFaturaPurchase`, `SaftValidationRun`) devolvem só
  a data de criação. `RecordAuditInfo` vive em `Application/Auditing`.
- `GET .../companies/{companyId}/record-info?table=&id=` — `HasCompanyAccessAsync`
  (`404`); `table`/`id` em falta → `400`; tabela desconhecida ou registo
  inexistente → `404`. Resolve os nomes dos autores em `UserAccounts` e devolve:

  ```json
  {
    "table": "contas",
    "id": "6221",
    "createdBy": { "id": "…", "name": "…" } | null,
    "createdAtUtc": "2026-…",
    "modifiedBy": { "id": "…", "name": "…" } | null,
    "modifiedAtUtc": "2026-…" | null
  }
  ```

Sem alteração de esquema nem da fase do `/foundation` (`F2-01`).

## Superfície de validação local

```
GET /api/v1/development/users/{userId}/companies/{companyId}/record-info?table=contas&id=6221
```

## Validação efetuada

- `AccountingAI.Api.Tests`: teste novo — um registo semeado devolve
  `createdAtUtc` e `createdBy` nulo; tabela desconhecida e registo inexistente
  dão `404`; sem parâmetros dá `400`; empresa sem acesso dá `404`.
- `AccountingAI.IntegrationTests`: o `AuditStampingTests` passou a verificar que
  o `RecordInfoService` lê os mesmos carimbos por nome lógico de tabela
  (`empresas`), e que nomes/registos desconhecidos devolvem `null`.
- `dotnet build`/`dotnet test` na solução inteira (cento e trinta e um testes
  .NET); `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências.
- Verificação em direto contra `accountingAI`: criar uma conta por
  `POST .../accounting/accounts` e pedir `record-info?table=contas&id=…` devolve
  `createdBy` com o nome «António Ferreira (Demonstração)»; um `PUT` acrescenta
  `modifiedBy`/`modifiedAtUtc` sem tocar na criação; `table=empresas` num
  registo semeado devolve `createdBy` nulo. Linha de teste removida da base.

## Fora deste incremento

- o serviço e os endpoints do dicionário de dados — F2-06C;
- o menu «Opções», o `RecordContext` e o diálogo «Sobre o Registo» — F2-06D.
