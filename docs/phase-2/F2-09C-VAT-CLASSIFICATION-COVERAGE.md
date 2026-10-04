# F2-09C — Cobertura da classificação de IVA

Terceiro sub-incremento do [F2-09](F2-09-ACCOUNT-VAT-CLASSIFICATION.md).
Antes de mapear o resumo aos campos numerados da declaração oficial
([F2-09D](F2-09-ACCOUNT-VAT-CLASSIFICATION.md), que precisa da confirmação do
utilizador sobre a numeração em vigor — [`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md)),
este incremento dá visibilidade sobre o que falta classificar: sem isto, o
[resumo (F2-09B)](F2-09B-VAT-SUMMARY.md) pode parecer completo estando, na
verdade, a omitir contas por classificar.

## Regras

- **`VatSummaryService.BuildAsync`** ganha uma segunda consulta: as contas
  relevantes (`AllowsPosting`, código `6*`/`7*`/`2432*`/`2433*`/`2434*`) com
  movimento no período mas **sem** `AccountVatClassification` — mesmo critério
  de relevância do `AccountVatClassificationService`, por *left join*
  (`GroupJoin` + `SelectMany`/`DefaultIfEmpty`, sintaxe fluente por causa do
  parâmetro `from` colidir com a palavra-chave de sintaxe de consulta). Devolve
  `VatSummaryUnclassifiedRow { AccountCode, AccountName, Debit, Credit, Net }`,
  ordenadas por código.
- `VatSummary` ganha o campo `Unclassified`; a mesma chamada a
  `GET .../vat-summary?from=&to=` devolve as duas listas — sem *endpoint* novo.
- **PWA:** painel **«Contas por classificar»** em `/apuramento-iva`, logo a
  seguir ao resumo, só visível quando há alguma — conta, descrição,
  débito/crédito/saldo, ligado à ficha da conta (`/contas/{código}`) e com uma
  nota a apontar para `/config-iva`. Incluído na exportação CSV.

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `VatSummaryServiceTests` — a conta de gasto
  não classificada com movimento no período (`6222`) aparece em `Unclassified`
  com os valores certos; a conta classificada e a sem movimento continuam de
  fora, tal como antes.
- `NexGenAccountingAI.Api.Tests`: `VatSummaryReturnsRowsGroupedByNatureRateAndRegion`
  passa a verificar também a chave `unclassified` na resposta.
- `NexGenAccountingAI.Web`: `tsc`, `eslint` e os oitenta e dois testes de interface
  verdes.
- `dotnet build`/`dotnet test` na solução (173 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia, 4.º trimestre
  de 2025, sem nenhuma conta classificada): `vat-summary` devolveu 19 contas em
  `unclassified` com os valores certos (confirmados na chamada anterior por
  `sqlcmd`); classificar uma delas (`24331131`) moveu-a de `unclassified` para
  `rows` na chamada seguinte (18 restantes); classificação removida a seguir.
  O painel na PWA mostrou a lista completa das 19 contas (confirmado por
  `get_page_text`), sem erros de consola.

## Fora deste incremento

- o mapeamento para os campos numerados do modelo oficial da declaração
  periódica — F2-09D, que precisa da confirmação do utilizador sobre a
  numeração em vigor antes de avançar (não se fabricam números de campo).
