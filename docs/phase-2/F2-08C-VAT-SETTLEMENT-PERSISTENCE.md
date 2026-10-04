# F2-08C — Persistência, histórico e encadeamento

Terceiro e último sub-incremento do [F2-08](F2-08-VAT-SETTLEMENT.md). **Fecha o
épico.** Guarda o apuramento, encadeia o excesso a reportar entre períodos, e
torna-o um input explícito.

## Regras

- **`Domain/Accounting/VatReturnFiling`** — um apuramento guardado por
  `(empresa, período)`: `PriorPeriodCredit` (o excesso a reportar do período
  anterior, indicado pelo utilizador) e `SnapshotJson` (o `VatReturn` calculado
  ao guardar). Índice único `(CompanyId, PeriodFrom, PeriodTo)`. Auditável
  (F2-06A), no `record-info` (`apuramentos-iva`) e no `DataDictionaryText`.
  Tabela `accounting.VatReturnFiling`, migração aditiva
  `20260909185631_VatReturnFiling`.
- **`IVatReturnService.BuildAsync`** ganha um `decimal? priorPeriodCredit` — com
  valor, é o excesso a usar (e `PriorCreditAssumed` fica `false`); a `null`,
  mantém-se o assumido do saldo de `2437*`.
- **`VatReturnFilingService`** (`Infrastructure.SqlServer`):
  - `GetAsync(from, to)` → recalcula com o excesso do apuramento guardado, ou —
    se não houver — com o **excesso a reportar do apuramento do período
    imediatamente anterior** (`PeriodTo == from − 1 dia`), ou, faltando esse, o
    assumido do razão. Devolve `{ Saved, SavedAtUtc, PriorPeriodCredit,
    SuggestedPriorCredit, PriorFromPreviousFiling, Return }`.
  - `SaveAsync(from, to, priorPeriodCredit)` → recalcula com esse valor, faz
    *upsert* do `VatReturnFiling` (guarda a fotografia), devolve a vista.
  - `ListAsync` → os últimos 24 apuramentos (período, a entregar, excesso a
    reportar, data).
- **API:** `GET/PUT .../companies/{companyId}/vat-return/saved?from=&to=` (o
  corpo do PUT é `{ priorPeriodCredit }`) e
  `GET .../companies/{companyId}/vat-return/history`. O
  `GET .../vat-return?from=&to=` de F2-08A mantém-se (cálculo rápido, sem
  gravar).
- **PWA:** `/apuramento-iva` passa a usar o `vat-return/saved`. O excesso a
  reportar do período anterior é um campo editável (pré-preenchido pela
  sugestão); a posição final recalcula-se de imediato ao editá-lo; «Guardar
  apuramento» persiste. Uma nota diz de onde veio a sugestão (apuramento
  anterior ou saldo de `2437`). Um painel «Apuramentos guardados» lista o
  histórico, com «Abrir» a repor o período.

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `VatReturnFilingServiceTests` — antes de
  guardar, não guardado e excesso assumido; guardar com excesso 30 grava a
  fotografia (`CreditToCarryForward` 130), aparece no histórico; o período
  seguinte propõe 130 como excesso anterior (`PriorFromPreviousFiling`); sem
  acesso devolve `null`. `SqlServerMigrationTests` inclui a migração nova.
  `VatReturnServiceTests` passou a `BuildAsync(..., null, ct)`.
- `NexGenAccountingAI.Api.Tests`: `VatReturnIsComputedForTheClosedPeriod` acrescenta
  a leitura de `vat-return/saved` (`"saved":false`, `"suggestedPriorCredit"`,
  `"return"`) e de `vat-return/history` (lista). A escrita fica na integração.
- `NexGenAccountingAI.Web`: `App.test.tsx` (o ecrã e o gate de demonstração), `tsc`,
  `eslint` e os oitenta testes de interface verdes.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI`.

## Épico F2-08 concluído

F2-08A (cálculo do apuramento) · F2-08B (ecrã) · F2-08C (persistência, histórico
e encadeamento do excesso a reportar). Fica em aberto, para épicos próprios: a
declaração periódica **oficial** da AT com os campos numerados (precisa de uma
configuração de IVA por conta), a submissão pelo webservice (precisa de
certificação), e as restantes obrigações (IES, IRC).
