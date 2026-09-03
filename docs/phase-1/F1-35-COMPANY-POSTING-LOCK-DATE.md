# F1-35 — Data de bloqueio de lançamentos por empresa

## Objetivo

Permitir que cada empresa fixe uma data até à qual (inclusive) não se aceitam
novos lançamentos, correções nem anulações — o típico "fecho de período" mensal
ou trimestral, independente do apuramento anual.

## Regras

`Company` ganha `PostingLockDate` (`DateOnly?`, `null` = sem bloqueio) e o método
`SetPostingLockDate(DateOnly?)`, que recusa anos fora de 2000–2200
(`ArgumentOutOfRangeException`).

`JournalPostingService` passa a validar a data em todas as escritas: o antigo
`EnsureYearIsOpenAsync` dá lugar a `EnsurePeriodIsOpenAsync`, que primeiro
verifica `Company.PostingLockDate` (`entryDate <= limite` ⇒
`InvalidOperationException`) e só depois a regra do exercício apurado. Continua a
correr no `PostAsync`, nas duas datas do `ReviseAsync` (a original e a nova) e na
anulação (`VoidAsync`). Os lançamentos de sistema **AP** e **AB** ficam isentos,
tal como já acontecia para o ano apurado, para não travar apuramento, abertura e
respetivas reaberturas.

`OrganizationAdminService.UpdateCompanyAsync` recebe `DateOnly? postingLockDate` e
aplica-o via `SetPostingLockDate`; `CompanyAdminData` devolve o valor.

## Alteração de esquema

Migração `20260903131441_CompanyPostingLockDate`: coluna `PostingLockDate date
NULL` em `organization.Companies` (não destrutiva; as empresas existentes ficam
sem bloqueio). Aplicada à base `accountingAI`.

## Superfície de validação local

`GET .../development/organization` inclui `postingLockDate` em cada empresa. O
`PUT .../development/organization/companies/{id}` aceita `"postingLockDate":
"AAAA-MM-DD"` ou `null`; uma data mal formada devolve 400 com a mensagem «Data de
bloqueio inválida. Use o formato AAAA-MM-DD.». No ecrã **Organização**, o editor
de empresa (só em edição) ganha o campo **Data de bloqueio de lançamentos**.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de domínio: `SetPostingLockDate` guarda e limpa a data e recusa um ano fora do intervalo;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; `UpdateCompanyAsync(..., postingLockDate: 2025-12-31)` persiste o valor; com a empresa de demonstração bloqueada a 2026-06-30, `PostAsync` de um OD a 2026-06-15 lança `InvalidOperationException`;
- teste de API: `/foundation` indica a fase `F1-35`; o `PUT` de empresa com `"postingLockDate":"ontem"` devolve 400 com «Data de bloqueio»;
- suite .NET completa (58 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- aviso prévio no editor de movimentos quando a data cai num período bloqueado (como o aviso de exercício apurado do F1-28);
- bloqueio automático ao registar o apuramento;
- histórico de quem alterou a data de bloqueio e quando.
