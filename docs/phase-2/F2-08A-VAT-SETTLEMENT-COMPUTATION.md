# F2-08A — Cálculo do apuramento

Primeiro sub-incremento do [F2-08](F2-08-VAT-SETTLEMENT.md). O cálculo
determinístico do apuramento periódico de IVA a partir das contas 24.3
([`ADR-0007`](../architecture-decisions/ADR-0007-VAT-SETTLEMENT.md)). Sem
persistência nem ecrã.

## Regras

- **`Application/Vat/`** — `VatReturnAccountLine` (`AccountCode`, `AccountName`,
  `Role` ∈ `liquidado|dedutivel|regularizacoes|reporte|outras`, `Debit`,
  `Credit`, `Net`), `VatReturn` (o apuramento calculado + as linhas),
  `IVatReturnService.BuildAsync(userId, companyId, from, to)`.
- **`VatReturnService`** (`Infrastructure.SqlServer`, `IVatReturnService`) — só
  lê, exclui os anulados, `null` sem acesso:
  1. agrupa por conta os movimentos das contas `243*` no período (débito,
     crédito);
  2. lê o **excesso a reportar do período anterior** — o saldo devedor das
     contas `2437*` para lançamentos anteriores a `from` (positivo → assumido;
     `PriorCreditAssumed = true`, até F2-08C ser um input);
  3. **IVA liquidado** = Σ (crédito − débito) nas `2433*`;
     **IVA dedutível** = Σ (débito − crédito) nas `2432*`;
     **regularizações a favor do Estado / do sujeito passivo** = Σ crédito /
     Σ débito nas `2434*` (em bruto);
  4. **resultado do período** = (liquidado + reg. a favor do Estado) −
     (dedutível + reg. a favor do sujeito passivo);
  5. **a entregar** = resultado − excesso anterior, quando positivo;
     **excesso a reportar** = o simétrico, quando negativo.
  A desagregação por conta (todas as `243*` que se moveram, ordenadas pelo
  código, com o papel de cada uma) acompanha o resultado.
- `apuramento-iva` entra em `ReportTemplateKeys.Known` (para o `ReportChrome`
  de F2-08B).
- **API:** `GET .../companies/{companyId}/vat-return?from=&to=` — `400` sem
  datas, `404` sem acesso.

Sem alteração de esquema.

## Superfície de validação local

```
GET /api/v1/development/users/{userId}/companies/{companyId}/vat-return?from=2026-01-01&to=2026-03-31
```

## Validação efetuada

- `AccountingAI.IntegrationTests`: `VatReturnServiceTests` — empresa construída
  com IVA liquidado 230, dedutível 100, uma regularização a favor do Estado de
  20 e um excesso de 50 em `2437` do exercício anterior: resultado do período
  150, a entregar 100 (150 − 50), sem excesso a reportar; um período só com IVA
  dedutível devolve excesso a reportar 150 e nada a entregar; sem acesso
  devolve `null`.
- `AccountingAI.Api.Tests`: `VatReturnIsComputedForTheClosedPeriod` — o
  apuramento traz `outputVat`, `inputVat`, `netPayable`,
  `creditToCarryForward` e `lines`; sem datas `400`; empresa sem acesso `404`.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- o ecrã «Apuramento de IVA» na PWA — F2-08B;
- a persistência (`accounting.VatReturn`), o histórico e o encadeamento do
  excesso a reportar como input explícito — F2-08C;
- a declaração periódica oficial da AT e a sua submissão — épicos próprios.
