# F2-08 — Apuramento periódico de IVA

Épico da [Fase 2](README.md), ponto 3 da visão (a metade que não depende da AT):
o **apuramento contabilístico** de IVA de um período — liquidado, dedutível,
regularizações, excesso reportado do período anterior, e a posição final — a
partir dos movimentos das contas 24.3, com desagregação por conta e histórico.
A decisão e as fronteiras estão na
[`ADR-0007`](../architecture-decisions/ADR-0007-VAT-SETTLEMENT.md).

## Contexto

- O [Resumo de IVA](F2-01-SAFT-XSD-VALIDATION.md) (F1-61) mostra o débito, o
  crédito e o saldo de cada conta 24.3 no período, derivados no cliente a partir
  do balancete. É uma leitura, não um apuramento: ignora as regularizações
  (`2434`), o excesso reportado do período anterior, e não guarda nada.
- Um contabilista faz o apuramento de IVA todos os meses ou trimestres: fecha
  o `2432` e o `2433` (com o `2434`) e determina o que há a entregar ao Estado
  ou a reportar. É trabalho de contabilidade puro — não precisa da AT.
- A declaração periódica **oficial** da AT (campos numerados) precisa de uma
  configuração de IVA por conta (taxa, tipo de operação) que a plataforma ainda
  não tem, e da certificação para submeter. Fica para épicos próprios; este
  produz o apuramento sobre o qual assentam.

## Objetivo

- **Apuramento do período** — a partir das contas `243*`, num intervalo de
  datas, calcular:
  - IVA liquidado (`2433*`), IVA dedutível (`2432*`);
  - regularizações a favor do Estado / do sujeito passivo (`2434*`, em bruto);
  - excesso a reportar do período anterior (saldo devedor de `2437*`, assumido
    do razão em F2-08A; input explícito e encadeado em F2-08C);
  - resultado do período e a posição final: **a entregar** ou **excesso a
    reportar**.
- **Rastreabilidade** — a desagregação por conta (todas as `243*` que se
  moveram) com o papel de cada uma e ligação ao razão.
- **Histórico** — um apuramento guardado por `(empresa, período)`, com o período
  seguinte a propor o excesso a reportar do anterior.

Fora do âmbito: a submissão à AT, os campos numerados do modelo oficial, a IES
e o IRC.

## Desenho

### Application

```
Application/Vat/
  VatReturn.cs            // o apuramento calculado + as linhas por conta
  IVatReturnService.cs
```

- `VatReturnAccountLine(AccountCode, AccountName, Role, Debit, Credit, Net)` —
  `Role` ∈ `liquidado | dedutivel | regularizacoes | reporte | outras`.
- `VatReturn(From, To, OutputVat, InputVat, RegularisationsForState,
  RegularisationsForTaxpayer, PriorPeriodCredit, PeriodResult, NetPayable,
  CreditToCarryForward, PriorCreditAssumed, Lines)`.
- `IVatReturnService.BuildAsync(userId, companyId, from, to, ct)` — `null` sem
  acesso.

### Infrastructure.SqlServer

- `VatReturnService : IVatReturnService` — só lê, exclui os anulados. Soma os
  movimentos das contas `243*` no período; calcula o excesso anterior do saldo
  de `2437*` antes de `from`; aplica a aritmética da ADR-0007.
- (F2-08C) `accounting.VatReturn` — um apuramento por `(CompanyId, PeriodFrom,
  PeriodTo)`, com o excesso anterior (input), a fotografia calculada e o estado.
  Auditável (F2-06A), no `record-info` e no `DataDictionaryText`. Migração
  aditiva.

### API

- `GET .../companies/{companyId}/vat-return?from=&to=` — o apuramento; `400` sem
  datas, `404` sem acesso.
- (F2-08C) `GET/PUT .../companies/{companyId}/vat-returns[/{id}]` — guardar e
  o histórico.

### PWA

- Ecrã **«Apuramento de IVA»** (`/apuramento-iva`) — seletor de período
  (atalhos mês/trimestre), o mapa do apuramento (liquidado, dedutível,
  regularizações, excesso anterior, resultado, posição final), a desagregação
  por conta com ligação ao razão, folha `@media print` (`ReportChrome`, chave
  `apuramento-iva`) e CSV.
- (F2-08C) «Guardar apuramento», o histórico, e o pré-preenchimento do excesso
  anterior a partir do apuramento do período anterior.
- Entrada no menu lateral e no menu «Opções».

## Sub-incrementos

### F2-08A — Cálculo do apuramento

- **Regras:** `Application/Vat/` (`VatReturn` + `IVatReturnService`);
  `VatReturnService` (`Infrastructure.SqlServer`) com a aritmética da ADR-0007 e
  a desagregação por conta; `GET .../vat-return?from=&to=`. `apuramento-iva`
  entra em `ReportTemplateKeys.Known`.
- **Validação:** teste de integração (empresa construída: liquidado, dedutível,
  uma regularização, um saldo de `2437` anterior → posição a entregar e o
  cenário inverso); teste de API de leitura.

### F2-08B — Ecrã «Apuramento de IVA»

- **Regras:** `/apuramento-iva` com o mapa, a desagregação por conta, impressão
  e CSV; `WorkContext.vatReturn`; menu lateral e `screenTable.ts`. Modo demo →
  «Requer ligação ao SQL Server».
- **Validação:** teste de interface (render + gate de demonstração).

### F2-08C — Persistência, histórico e encadeamento

- **Regras:** `accounting.VatReturn` + migração; `VatReturnService` guarda e lê;
  `GET/PUT .../vat-returns[/{id}]`; o excesso anterior passa a input explícito,
  pré-preenchido do apuramento anterior; auditável e no dicionário de dados.
- **Validação:** integração para a escrita e o encadeamento; teste de API de
  leitura do histórico.

## Decisões em aberto

- **Conta do excesso a reportar** — assume-se `2437*`. Se um plano usar outra
  conta, F2-08C resolve-o com o input explícito.
- **Periodicidade** — mensal ou trimestral é uma escolha do período no ecrã; a
  plataforma não a infere do volume de negócios (fica para uma configuração de
  empresa futura).
- **Declaração oficial** — os campos numerados do modelo da AT dependem de uma
  configuração de IVA por conta; épico próprio.

## Referências

- [`ADR-0007`](../architecture-decisions/ADR-0007-VAT-SETTLEMENT.md).
- `AccountLedgerService` — as somas por conta e por período a reaproveitar.
- F1-61 (Resumo de IVA), F2-06A (auditoria).
