# F1-71 — Filtro de origem na razão de conta

## Objetivo

A lista de movimentos (F1-63) e o livro diário (F1-66) já filtram por origem; a
razão de conta ficou de fora porque tem saldo acumulado — filtrar as linhas sem
cuidado desalinharia o saldo do que a conta realmente tem.

## Regras

- `AccountLedgerService.AccountLedgerAsync` aceita `source` (nome do valor
  `EntrySource`, sem distinção de maiúsculas; um valor desconhecido é ignorado).
  O saldo de abertura e o saldo acumulado de **cada linha** continuam a somar
  **todas as origens**; o filtro só restringe as linhas devolvidas, aplicado
  **depois** de calcular o saldo — para o saldo final continuar a bater certo com
  a conta mesmo a mostrar só uma parte dos movimentos.
- Cada `AccountLedgerEntry` passa a levar `Source`.
- O endpoint `GET .../accounting/accounts/{code}/ledger` aceita `?source=` e
  devolve `source` em cada linha.
- No ecrã «Razão da conta», um filtro «Origem»; quando ativo, os cartões «Saldo»
  e «Movimentos» avisam «Todas as origens» / «Só {origem}» para não se confundir
  saldo (sempre total) com contagem (filtrada). A exportação CSV ganha a coluna
  «Origem».

Sem alteração de esquema. A fase do `/foundation` passa a **F1-71**.

## Superfície de validação local

Na razão de uma conta, o filtro «Origem» reduz as linhas mostradas sem alterar o
saldo de abertura nem o saldo final.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `NexGenAccountingAI.IntegrationTests` (LocalDB descartável): filtrar a razão da conta
  `6221` por `"Manual"` mantém o mesmo saldo final da razão sem filtro; filtrar
  por `"Opening"` (sem linhas dessa origem) devolve zero linhas mas o mesmo saldo
  de abertura e final;
- `NexGenAccountingAI.Api.Tests`: o `/foundation` indica a fase `F1-71`;
- `NexGenAccountingAI.Web`: lint, build e trinta e oito testes de interface (o teste da
  razão de conta passa também pelo filtro «Origem», confirmando que o saldo não
  muda);
- verificação no browser contra a base `accountingAI`: a conta `6221` (zerada pelo
  apuramento) tem saldo 0,00 € com 2 linhas (`Manual` e `Apuramento`);
  `?source=Manual` mostra 1 linha com o mesmo saldo 0,00 €; no ecrã, filtrar por
  «Apuramento» passa de 2 para 1 movimento sem alterar o «Saldo atual»;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos.

## Fora deste incremento

- aplicar o mesmo filtro ao balancete.
