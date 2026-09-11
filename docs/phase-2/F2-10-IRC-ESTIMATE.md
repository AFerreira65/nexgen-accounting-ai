# F2-10 — Estimativa de IRC

Épico da [Fase 2](README.md), ponto 3 da visão: a estimativa do imposto sobre
o rendimento das pessoas coletivas (IRC) a partir do resultado contabilístico
de um período. A decisão e as fronteiras estão na
[`ADR-0010`](../architecture-decisions/ADR-0010-IRC-ESTIMATE.md).

## Contexto

- A [Demonstração de resultados](F2-01-SAFT-XSD-VALIDATION.md) já dá o
  resultado líquido de um período (classes 6/7). O IRC parte daí, mas o
  resultado fiscal exige **correções** que a contabilidade, por si, não revela
  (tributações autónomas, donativos, mais/menos-valias fiscais, benefícios
  fiscais, derrama, reporte de prejuízos) e uma **taxa** que muda com a lei e o
  regime da empresa.
- Tentar adivinhar essas correções ou fixar uma taxa no código seria o mesmo
  risco de sobre-alegar conformidade que a [F2-08](F2-08-VAT-SETTLEMENT.md) e a
  [F2-09](F2-09-ACCOUNT-VAT-CLASSIFICATION.md) evitaram para o IVA.

## Objetivo

- **Estimar** o IRC de um período: resultado líquido (classes 6/7) + correções
  fiscais introduzidas pelo utilizador (descrição + valor) = resultado fiscal;
  resultado fiscal × taxa introduzida pelo utilizador = coleta estimada (0
  quando o resultado fiscal não é positivo).
- Manter as correções e a taxa como **entrada do utilizador em cada cálculo**
  — nunca sugeridas, validadas contra a lei fiscal, nem fixadas no código.

Fora do âmbito: o Modelo 22 e os seus campos numerados, a submissão à AT, o
reporte de prejuízos fiscais, a derrama, as tributações autónomas, os
benefícios fiscais.

## Desenho

### Application

```
Application/Irc/
  IrcEstimate.cs   // IrcAdjustment + IrcEstimate
```

- `IrcAdjustment(Description, Amount)` — uma correção fiscal livre.
- `IrcEstimate(From, To, NetIncome, Adjustments, AdjustmentsTotal,
  TaxableResult, TaxRate, EstimatedTax)`.

### Infrastructure.SqlServer

- `IrcEstimateService` (sem interface em `Application`, à imagem do
  `AccountVatClassificationService`/`VatSummaryService`) — `BuildAsync(userId,
  companyId, from, to, taxRate, adjustments, ct)`: reaproveita
  `AccountLedgerService.TrialBalanceAsync` para o resultado líquido (mesma
  aritmética da Demonstração de Resultados: Σ classe 7 crédito−débito − Σ
  classe 6 débito−crédito), soma as correções, aplica a taxa. `null` sem
  acesso à empresa.

### API

- `POST .../companies/{companyId}/irc-estimate?from=&to=` (corpo `{ taxRate,
  adjustments: [{description, amount}] }`) — `400` sem datas ou taxa fora de
  `]0, 1]`; `404` sem acesso.

### PWA

- Ecrã **«Estimativa de IRC»** (`/estimativa-irc`) — seletor de período
  (atalhos mês/trimestre/ano, arranca no exercício), taxa editável (sugestão
  inicial 21%, claramente rotulada como a confirmar), lista de correções
  fiscais editável (adicionar/remover linhas), «Calcular», o resultado
  (resultado líquido, correções, resultado fiscal, taxa, coleta estimada) com
  aviso fixo, impressão/PDF e CSV.
- Entrada no menu lateral e no menu «Opções».

## Sub-incrementos

### F2-10A — Cálculo da estimativa

- **Regras:** `Application/Irc/` (`IrcEstimate` + `IrcAdjustment`);
  `IrcEstimateService`; `POST .../irc-estimate?from=&to=`; ecrã
  `/estimativa-irc` com o formulário de correções e o resultado; `estimativa-irc`
  em `ReportTemplateKeys.Known`. Sem persistência — cada cálculo é um «e se».
- **Validação:** teste de integração (empresa construída: classes 6/7 → o
  resultado líquido certo; correções somadas; taxa aplicada; resultado fiscal
  negativo → coleta 0); teste de API (`400` sem datas/taxa inválida, `404` sem
  acesso); teste de interface (formulário + gate de demonstração).

### F2-10B — Persistência da estimativa

- **Regras:** `accounting.IrcEstimateFiling` (taxa + correções + fotografia,
  um por período) + migração; `IrcEstimateFilingService` guarda e recalcula
  sempre contra o razão atual ao ler; `GET/PUT .../irc-estimate/saved`,
  `GET .../irc-estimate/history`; o ecrã carrega a estimativa guardada do
  período, «Guardar estimativa», histórico com «Abrir». Auditável, no
  `record-info` e no `DataDictionaryText`.
- **Validação:** integração (guardar, reler, um lançamento novo muda o
  resultado líquido lido sem perder a taxa/correções guardadas, atualizar não
  duplica, histórico); teste de API (leitura + validação, sem escrita real —
  a base de testes de API é partilhada); teste de interface.

## Decisões em aberto

- **Modelo 22** — os campos numerados dependem da confirmação do utilizador
  sobre a numeração em vigor; não se avança sem isso (mesma cautela da
  F2-09D).
- **Regime simplificado / taxas reduzidas por escalão** — fora deste
  incremento; a taxa fica de introdução livre.

## Referências

- [`ADR-0010`](../architecture-decisions/ADR-0010-IRC-ESTIMATE.md).
- `AccountLedgerService.TrialBalanceAsync` — o resultado líquido a
  reaproveitar. F2-08 (o mesmo faseamento: cálculo antes de persistência).
