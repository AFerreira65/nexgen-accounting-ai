# F2-03C — Relatório de cruzamento

Terceiro sub-incremento do [F2-03](F2-03-CROSS-CHECK.md), sobre a ingestão de
[F2-03A](F2-03A-EFATURA-CSV-INGESTION.md) e o ecrã de
[F2-03B](F2-03B-EFATURA-PWA-SCREEN.md). Introduz o serviço e o endpoint que
produzem o relatório; o ecrã fica para F2-03D.

## Regras

- `GET .../companies/{companyId}/cross-check?from=&to=` — devolve o
  `CrossCheckReport` da empresa para um intervalo de datas **fechado** (início
  e fim obrigatórios; sem eles, `400`). `404` quando a empresa não existe ou o
  utilizador não lhe tem acesso.
- O relatório tem três perspetivas:
  1. **Compras** — as faturas do e-Fatura do período (base + IVA) face aos
     lançamentos de compra da contabilidade. Um lançamento é «de compra» quando
     tem pelo menos uma linha numa conta de gasto (classe 6) ou de IVA
     dedutível (`2432*`); a base é a soma das linhas de classe 6 e o IVA a das
     linhas `2432*`. Totais do período, desagregação por mês, e as duas listas
     de divergências: faturas do e-Fatura sem lançamento e lançamentos de
     compra sem fatura no e-Fatura.
  2. **Vendas** — os rendimentos (classe 71/72, a crédito) e o IVA liquidado
     (`2433*`) da contabilidade, por mês. É um apoio de conferência; a
     reconciliação ao nível do documento depende de importar o `SalesInvoices`
     do SAF-T de faturação (F2-03E, opcional).
  3. **Totais** — o débito e o crédito da empresa no período (sem anulados),
     para a mesma verificação «débito = crédito» que a importação do SAF-T de
     contabilidade faz (F1-54), agora visível aqui.

## Correspondência fatura ↔ lançamento

A contabilidade **não guarda o NIF do fornecedor ao nível do documento** (não
há um mestre de terceiros; a conta corrente `2211*` identifica o fornecedor
pelo código de conta, não pelo NIF). A correspondência faz-se então pelo
**número de documento normalizado** — só letras e dígitos, em maiúsculas, o que
tolera espaços, barras, pontos e prefixos de série — exigindo que um número
esteja contido no outro, e com uma **tolerância de 31 dias** entre a data da
fatura e a data do lançamento (uma compra é muitas vezes lançada no mês
seguinte). Números normalizados com menos de três caracteres não entram na
correspondência. Cada lançamento corresponde no máximo a uma fatura e
vice-versa (correspondência gananciosa por ordem de data). As listas de
divergências são limitadas a 200 linhas cada.

A tolerância entre `DocumentReference` e `InvoiceNo` continua uma decisão em
aberto do épico — precisa de ensaio com dados reais de vários softwares
emissores.

## Desenho

- `Application/CrossCheck/CrossCheckReport.cs` — os registos do relatório, com
  os desvios (`BaseDelta`, `VatDelta`, …) como propriedades calculadas.
- `Application/CrossCheck/ICrossCheckService.cs` — o contrato (devolve `null`
  quando não há acesso).
- `Application/CrossCheck/CrossCheckBuilder.cs` — a lógica **pura** de
  correspondência, agregação mensal e cálculo de desvios, sem base de dados.
- `Infrastructure.SqlServer/CrossCheckService.cs` — as consultas EF (faturas do
  período, lançamentos de compra com as linhas, somas de vendas por mês, totais
  do período) que alimentam o `CrossCheckBuilder`.
- Registo em `AddAccountingSqlServer` (`ICrossCheckService → CrossCheckService`).

Sem alteração de esquema. A fase do `/foundation` mantém-se em `F2-01`.

## Superfície de validação local

```
GET /api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/22222222-2222-2222-2222-222222222222/cross-check?from=2026-08-01&to=2026-08-31
```

Na base `accountingAI` / Almeida & Costa (dados de demonstração, sem faturas do
e-Fatura), Agosto de 2026 devolve as compras da contabilidade (base 1200, IVA
276) como um lançamento sem fatura no e-Fatura, e as vendas (rendimentos 2500,
IVA liquidado 575).

## Validação efetuada

- `NexGenAccountingAI.Infrastructure.Files.Tests`: oito testes novos do
  `CrossCheckBuilder` (correspondência dentro da tolerância, fatura só no
  e-Fatura, só na contabilidade, valores diferentes, agregação por mês, um
  lançamento não serve duas faturas, número curto de mais, passagem das vendas
  e dos totais);
- `NexGenAccountingAI.Api.Tests`: um teste novo — o relatório exige um período
  fechado (`400`), está limitado à empresa do utilizador (`404`), e devolve as
  três perspetivas;
- `dotnet build`/`dotnet test` na solução inteira sem falhas (cento e vinte e
  quatro testes .NET);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências.

## Fora deste incremento

- o ecrã «Cruzamento» na PWA e o contador nas Tarefas/painel — F2-03D;
- o leitor de `SalesInvoices` do SAF-T de faturação — F2-03E (opcional).
