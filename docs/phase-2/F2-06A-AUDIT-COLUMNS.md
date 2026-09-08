# F2-06A — Colunas de auditoria e carimbo

Primeiro sub-incremento do [F2-06](F2-06-DATA-DICTIONARY-AND-RECORD-INFO.md).
Dá a cada registo editável a memória de quem o criou e quem o alterou pela
última vez, e quando — sem tocar no domínio.

## Regras

- **Propriedades-sombra** `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`,
  `ModifiedBy` em nove entidades: `JournalEntry`, `LedgerAccount`,
  `AccountingJournal`, `AccountingDocumentType`, `DocumentNumberSeries`,
  `Company`, `UserAccount`, `CompanyUser`, `UserCompanySettings`
  (`AuditShadowProperties.Apply`). `Company`, `UserAccount` e `CompanyUser` já
  tinham um `CreatedAtUtc` próprio — não se lhe toca; as outras seis ganham-no
  com o mesmo default (`SYSUTCDATETIME()`). `EFaturaPurchase` e
  `SaftValidationRun` ficam de fora (já guardam o seu `CreatedAtUtc` e são
  insert-only).
- `AuditStampingInterceptor : SaveChangesInterceptor` — em `Added` carimba
  `CreatedAtUtc`/`CreatedBy`; em `Modified` carimba `ModifiedAtUtc`/`ModifiedBy`
  e protege o carimbo de criação (`IsModified = false`). O «quando» é sempre
  gravado; o «quem» só quando é conhecido. O sinal de que um tipo é auditado é
  ter a propriedade `ModifiedAtUtc`.
- `ICurrentUserAccessor` (`Application/Auditing`, `scoped`, mutável) — o
  `CurrentUserMiddleware` da API preenche-o com o `userId` da rota, depois do
  encaminhamento. Registado em `AddAccountingSqlServer` junto com o interceptor
  (`AddDbContext` passa a resolver o interceptor do contentor).
- Migração aditiva `20260908145523_AuditColumns` — quinze colunas
  `datetimeoffset` e dezoito `uniqueidentifier`, em nove tabelas, sem
  `AlterColumn`.

Sem alteração de contrato da API nem da fase do `/foundation` (`F2-01`). Os
carimbos ainda não são expostos — isso é o F2-06B (`record-info`).

## Superfície de validação local

Criar ou alterar uma conta, um diário, um documento, uma série, um lançamento
ou uma empresa e observar as colunas `CreatedBy`/`ModifiedBy` na base.

## Validação efetuada

- `AccountingAI.IntegrationTests`: teste novo `AuditStampingTests` — criar uma
  empresa carimba `CreatedBy`/`CreatedAtUtc` e deixa `Modified*` nulo; renomear
  carimba `ModifiedBy`/`ModifiedAtUtc` sem mexer na criação. O teste da
  migração aplica `AuditColumns` duas vezes.
- `dotnet build`/`dotnet test` na solução inteira sem falhas (cento e trinta
  testes .NET — Integration 6, +1);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação em direto contra `accountingAI`: `POST .../accounting/accounts`
  grava `CreatedBy = 33333333-…` (o `userId` da rota) e `CreatedAtUtc`;
  `PUT .../accounting/accounts/{code}` acrescenta `ModifiedBy`/`ModifiedAtUtc`
  sem tocar na criação. Linha de teste removida da base.

## Fora deste incremento

- expor os carimbos (`record-info`) — F2-06B;
- o dicionário de dados — F2-06C;
- o menu «Opções» e o diálogo «Sobre o Registo» — F2-06D.
