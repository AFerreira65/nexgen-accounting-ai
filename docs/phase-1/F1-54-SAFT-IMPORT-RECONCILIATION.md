# F1-54 — Reconciliar a importação com os totais declarados no SAF-T

## Objetivo

Depois de importar os movimentos de um SAF-T (F1-53), dizer se a contabilidade da
empresa no período do ficheiro coincide com os totais declarados no próprio
ficheiro — para o contabilista confiar que a importação ficou completa.

## Regras

- `ReadTransactionsAsync` passa a devolver, além das transações, os totais
  declarados no cabeçalho do bloco `GeneralLedgerEntries`: `NumberOfEntries`,
  `TotalDebit` e `TotalCredit` (lidos no mesmo varrimento em streaming).
- No fim da importação (fora da transação de escrita), o serviço soma os débitos e
  os créditos dos lançamentos da empresa (sem os anulados) cuja data cai no
  **período do ficheiro** — de `min` a `max` das datas das transações lidas.
- O resultado da importação ganha: `declaredEntryCount`, `declaredTotalDebit`,
  `declaredTotalCredit`, `filePeriodFrom`, `filePeriodTo`, `companyPeriodDebit`,
  `companyPeriodCredit` e `matchesDeclaredTotals` — `true` quando o ficheiro
  declara totais e a soma da empresa no período coincide (a 2 casas) com o
  `TotalDebit`/`TotalCredit` declarados; `null` quando o ficheiro não declara
  totais.
- A soma é sobre o **estado final** da empresa, não só sobre o que entrou nesta
  importação: se parte já lá estava (reimportação, lançamento manual anterior), a
  reconciliação continua a bater certo.
- O endpoint `POST .../saft/entries-import` devolve estes campos. Sem alteração de
  contrato de escrita nem de esquema.
- No ecrã de importação, a seguir à linha «N movimentos importados», o painel
  mostra uma linha verde «Os totais da empresa no período do ficheiro (X)
  coincidem com os declarados no SAF-T.» ou amarela com os dois pares de valores
  quando não coincidem; nada quando o ficheiro não declara totais.

Sem alteração de esquema.

## Superfície de validação local

Importar um SAF-T com `TotalDebit`/`TotalCredit` no bloco `GeneralLedgerEntries`.
Se a empresa ficar com esses totais no período do ficheiro, aparece a linha de
reconciliação verde; se faltarem movimentos (contas por criar, transações
saltadas), aparece a linha amarela com a diferença.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `NexGenAccountingAI.Infrastructure.Files.Tests`: `ReadTransactionsAsync` devolve
  `DeclaredEntryCount`/`DeclaredTotalDebit`/`DeclaredTotalCredit` do ficheiro de
  exemplo (3 / 150,00 / 150,00) e `null` quando o bloco não os tem;
- `NexGenAccountingAI.IntegrationTests` (LocalDB descartável): importar para uma empresa
  criada a partir do SAF-T dois lançamentos que somam 150 de débito e 150 de
  crédito, com o ficheiro a declarar 150/150 no período 2026-02-10 a 2026-03-03,
  dá `MatchesDeclaredTotals = true` e `CompanyPeriodDebit`/`CompanyPeriodCredit`
  de 150; reimportar mantém a reconciliação; um ficheiro a declarar 999/0 para o
  mesmo período dá `MatchesDeclaredTotals = false`;
- verificação no browser contra a base `accountingAI`: importar um SAF-T sintético
  para a empresa da ilha (exercício aberto) com dois movimentos válidos (160) e um
  desequilibrado (saltado), o ficheiro a declarar `TotalDebit`/`TotalCredit` de
  160 — a importação mostra «2 movimentos importados, 1 saltado» e a linha verde
  «Os totais da empresa no período do ficheiro (160,00 €) coincidem com os
  declarados no SAF-T»; os movimentos de teste foram removidos da base no fim;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- reconciliar a contagem de lançamentos (`NumberOfEntries`) com o que a empresa
  tem no período;
- reconciliar por diário ou por conta;
- mostrar a reconciliação na pré-visualização (antes de importar), sem escrever.
