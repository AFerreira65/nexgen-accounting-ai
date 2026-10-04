# F2-05A — Dossiê determinístico

Primeiro sub-incremento do [F2-05](F2-05-AI-MANAGEMENT-REPORTS.md). Monta o
retrato estruturado e limitado que — e só ele — chega ao modelo
([`ADR-0006`](../architecture-decisions/ADR-0006-AI-REPORT-PROVIDER.md)). Sem IA.

## Regras

- **`Application/ManagementReports/`** — só tipos e fronteiras, sem EF:
  - `ManagementReportKind` (enum: `SinteseMensal`, `AnaliseRentabilidade`,
    `PosicaoFinanceira`, `RevisaoIva`, `AlertasDeFecho`);
  - `ManagementReportRequest` (`{ Kind?, Question?, From, To }`);
  - `ManagementReportDossier` + os *records* de secção (`DossierAccountDelta`,
    `DossierAmount`, `DossierRatio`, `DossierMovement`, `DossierCrossCheck`);
  - `ManagementReportResult` + *records* de secção e a constante do aviso
    (`ManagementReportResult.Notice`) — o contrato de saída, usado a partir de
    F2-05B;
  - `ManagementReportCatalog` — as entradas (chave kebab, rótulo pt-PT,
    descrição, secções do dossiê que cada tipo usa) e os conversores
    `KeyOf`/`Parse`/`LabelOf`;
  - `IManagementReportDataService`.
- **`ManagementReportDataService`** (`Infrastructure.SqlServer`,
  `IManagementReportDataService`) — só lê, exclui os anulados, devolve `null`
  sem acesso à empresa. Monta:
  1. **balancete do período e do homólogo** (`AccountLedgerService.TrialBalanceAsync`
     com o mesmo intervalo do ano anterior; `HasComparative` é `false` quando
     não há linhas nesse período);
  2. **`TrialBalanceDelta`** — uma linha por conta que se moveu num dos
     períodos, com o movimento líquido (débito − crédito) atual, o homólogo e a
     variação; ordenada por valor absoluto e cortada nas
     `ManagementReportDossier.MaxAccountRows` (**120**) contas de maior
     movimento, com nota nos `Limits`;
  3. **`IncomeSummary`** — rendimentos (classe 7 = crédito − débito), gastos
     (classe 6 = débito − crédito) e resultado, atual e homólogo;
  4. **`PositionSummary`** — ativo (classes 1/3/4 + classe 2 devedora), passivo
     (classe 2 credora) e capital próprio (classe 5 + resultado), pelo saldo
     acumulado ao fim do período — a mesma lógica que os ecrãs fixos derivam do
     balancete;
  5. **`Ratios`** — margem líquida, peso dos FSE (62), peso dos gastos com
     pessoal (63), liquidez geral, fundo de maneio e autonomia financeira; cada
     um com o valor (`null` se o denominador é nulo — o motor não inventa), a
     unidade, a fórmula em texto e as classes usadas;
  6. **`CrossCheck`** — resumo do cruzamento compras e-Fatura × contabilidade
     do período (`ICrossCheckService`): totais, divergências e a conferência
     débito = crédito; `Available = false` quando não há dados de compras;
  7. **`TopMovements`** — os `MaxMovements` (**25**) lançamentos de maior valor
     do período, cada um com as contas que mais pesam.
- **API:**
  - `GET /api/v1/development/management-reports/catalog` — os tipos
    pré-definidos (sem empresa);
  - `GET .../users/{userId}/companies/{companyId}/management-reports/dossier?kind=&from=&to=` —
    o dossiê; `400` sem datas, `404` sem acesso. Não chama o modelo.

Sem alteração de esquema.

## Superfície de validação local

```
GET /api/v1/development/management-reports/catalog
GET /api/v1/development/users/{userId}/companies/{companyId}/management-reports/dossier?kind=sintese-mensal&from=2026-01-01&to=2026-12-31
```

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `ManagementReportDataServiceTests` — empresa
  construída com um exercício anterior (2025) e o corrente (2026): a variação
  homóloga vem preenchida (vendas +500), a margem líquida e o peso do pessoal
  batem com o cálculo à mão, o maior movimento (1500) vem primeiro e cita a
  conta 7111, o cruzamento corre (sem faturas do e-Fatura, débito = crédito), e
  sem acesso devolve `null`.
- `NexGenAccountingAI.Api.Tests`: `ManagementReportCatalogAndDossierAreAvailable` — o
  catálogo traz `sintese-mensal` e as secções; o dossiê traz `companyName`,
  `incomeSummary`, `ratios` e `crossCheck`; sem datas `400`; empresa sem acesso
  `404`.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- o narrador e o projeto `NexGenAccountingAI.Infrastructure.Ai` — F2-05B;
- a persistência (`reporting.ManagementReport`), o interruptor
  (`reporting.CompanyAiSettings`) e os *endpoints* de geração — F2-05C;
- o ecrã «Relatórios de gestão» na PWA — F2-05D.
