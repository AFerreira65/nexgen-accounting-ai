# F2-10A — Cálculo da estimativa de IRC

Primeiro sub-incremento do [F2-10](F2-10-IRC-ESTIMATE.md). A estimativa de IRC
de um período: resultado líquido (classes 6/7) mais correções fiscais
introduzidas pelo utilizador, vezes uma taxa introduzida pelo utilizador
([`ADR-0010`](../architecture-decisions/ADR-0010-IRC-ESTIMATE.md)).

## Regras

- **`Application/Irc/IrcEstimate.cs`** — `IrcAdjustment(Description, Amount)`
  livre; `IrcEstimate(From, To, NetIncome, Adjustments, AdjustmentsTotal,
  TaxableResult, TaxRate, EstimatedTax)`.
- **`IrcEstimateService`** (`Infrastructure.SqlServer`, sem interface em
  `Application`) — `BuildAsync(userId, companyId, from, to, taxRate,
  adjustments, ct)`: reaproveita `AccountLedgerService.TrialBalanceAsync` para
  o resultado líquido (Σ classe 7 crédito−débito − Σ classe 6 débito−crédito,
  a mesma aritmética da Demonstração de Resultados), soma as correções, aplica
  a taxa ao resultado fiscal quando positivo (0 quando não é). `null` sem
  acesso à empresa. Sem persistência — cada chamada é um cálculo isolado.
- **API:** `POST .../companies/{companyId}/irc-estimate?from=&to=` (corpo
  `{ taxRate, adjustments: [{description, amount}] }`) — `400` sem datas ou
  taxa fora de `]0, 1]`; `404` sem acesso.
- **PWA:** ecrã **«Estimativa de IRC»** (`/estimativa-irc`) — período (atalhos
  mês/trimestre/ano, arranca no exercício), taxa editável (sugestão inicial
  21%, com aviso de que é só um ponto de partida), lista de correções fiscais
  editável (adicionar/remover linhas), «Calcular», o resultado com aviso fixo
  quando o resultado fiscal não é positivo, impressão/PDF e CSV. `estimativa-irc`
  em `ReportTemplateKeys.Known`. Entrada no menu lateral.

## Validação efetuada

- `AccountingAI.IntegrationTests`: `IrcEstimateServiceTests` — rendimento
  10 000 e gasto 4 000 → resultado líquido 6 000; duas correções (+1000, −500)
  → resultado fiscal 6 500 e coleta 1 365 a 21%; uma correção que torna o
  resultado fiscal negativo → coleta 0; sem correções → coleta sobre o
  resultado líquido puro; um lançamento fora do período não entra; sem acesso
  `null`.
- `AccountingAI.Api.Tests`: `IrcEstimateComputesFromNetIncomePlusAdjustments`
  — `200` com `netIncome`/`estimatedTax`; taxa `0` `400`; sem datas `400`;
  empresa sem acesso `404`.
- `AccountingAI.Web`: `App.test.tsx` (o ecrã e o gate de demonstração), `tsc`,
  `eslint` e os oitenta e três testes de interface verdes.
- `dotnet build`/`dotnet test` na solução (175 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia, ano de
  2025): o resultado líquido devolvido pelo `irc-estimate`
  (11 468 840,93 €) confere ao cêntimo com a soma direta em `sqlcmd`; uma
  correção de 5 000 € e a taxa de 21% deram a coleta certa (2 409 506,60 €);
  o mesmo cálculo repetido no ecrã, com uma correção introduzida na
  interface, mostrou os mesmos valores; confirmado o texto acentuado
  (`ç`, `ã`) a ida e volta pela API sem alteração (um teste isolado com
  `curl` tinha mostrado um erro de codificação — era do ficheiro de
  entrada do `curl` nesta shell, não da aplicação: confirmado com um pedido
  UTF-8 explícito). Sem persistência, nada para limpar.

## Fora deste incremento

- persistência de estimativas por `(empresa, período)`;
- o mapeamento aos campos numerados do Modelo 22 — precisa da confirmação do
  utilizador sobre a numeração em vigor (mesma cautela da F2-09D).
