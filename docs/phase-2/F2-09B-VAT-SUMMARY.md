# F2-09B — Resumo de IVA por taxa e natureza

Segundo sub-incremento do [F2-09](F2-09-ACCOUNT-VAT-CLASSIFICATION.md). Soma o
movimento do período das contas classificadas
([F2-09A](F2-09A-ACCOUNT-VAT-CLASSIFICATION.md)), agrupado por natureza, taxa e
região — a desagregação que se transcreve para a declaração periódica
([`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md)).

## Regras

- **`VatSummaryService`** (`Infrastructure.SqlServer`, sem interface em
  `Application`, à imagem do `AccountVatClassificationService`) — junta
  `JournalEntryLines` com `AccountVatClassification` pelo `AccountId`, filtra
  pelo período e por lançamentos não anulados, e agrupa por
  `(Kind, Nature, Rate, Region)`:
  - `Kind` é `base` para as contas `6*`/`7*`, `imposto` para as `243*` (deriva
    do código da conta, não da classificação — cobre também uma classificação
    posta diretamente numa conta fora do lote habitual);
  - cada linha soma `Debit`, `Credit`, `Net` (`Debit − Credit`) e o número de
    contas distintas que contribuíram;
  - só entram contas com **movimento no período** — uma conta classificada mas
    sem lançamentos não aparece, o que exclui naturalmente as contas agregadas
    do plano de contas importado (ex.: `24331`, `243311`) que ficam marcadas
    `AllowsPosting=true` mas não têm movimento próprio.
- **API:** `GET .../companies/{companyId}/vat-summary?from=&to=` →
  `{ from, to, rows: [{ kind, nature, rate, region, debit, credit, net,
  accountCount }] }`. `400` sem `from`/`to`; `404` sem acesso à empresa.
- **PWA:** painel **«Resumo por taxa e natureza»** no ecrã **«Apuramento de
  IVA»** (`/apuramento-iva`), com o mesmo período do apuramento — natureza,
  taxa, região, base/imposto, número de contas, débito, crédito e saldo; link
  para a `/config-iva` quando não há contas classificadas com movimento. Incluído
  na exportação CSV do apuramento.

## Validação efetuada

- `AccountingAI.IntegrationTests`: `VatSummaryServiceTests` — uma venda e uma
  compra com IVA, cada uma com a conta base e a conta 24.3 classificadas com a
  mesma natureza/taxa/região, dão as duas linhas `base`/`imposto` esperadas com
  os totais certos; uma conta de gasto não classificada e uma conta classificada
  sem movimento no período não aparecem; um lançamento fora do período não
  entra; sem acesso `null`.
- `AccountingAI.Api.Tests`: `VatSummaryReturnsRowsGroupedByNatureRateAndRegion`
  — `200` com `rows`; sem `from`/`to` `400`; empresa sem acesso `404`.
- `AccountingAI.Web`: `tsc`, `eslint` e os oitenta e dois testes de interface
  verdes (o painel só aparece com dados reais, à imagem do resto do apuramento).
- `dotnet build`/`dotnet test` na solução (173 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia): classificadas
  as contas `7121131` (vendas) e `24331131` (IVA liquidado) com a mesma
  natureza/taxa/região; `GET .../vat-summary?from=2025-10-01&to=2025-12-31`
  devolveu as duas linhas com os valores exatos confirmados por `sqlcmd`
  (base: débito 163 126,74 €, crédito 3 990 602,82 €; imposto: débito
  37 523,76 €, crédito 917 935,49 €); o painel na PWA mostrou a mesma
  desagregação; classificações removidas a seguir e confirmado o registo
  zero na tabela.

## Fora deste incremento

- o mapeamento para os campos numerados do modelo oficial da declaração
  periódica — F2-09C, best-effort e com aviso.
