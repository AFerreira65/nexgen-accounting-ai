# F1-14 — Ecrã da razão de conta ligado à API

## Objetivo

Ligar o ecrã **Razão da conta** ao endpoint da razão. Até aqui o ecrã derivava
o saldo e os movimentos apenas dos dados em memória; passa a consumir
`…/accounting/accounts/{código}/ledger`, com o mesmo intervalo de datas e o saldo
de abertura introduzidos em [F1-13](F1-13-PERIOD-REPORTS.md).

## Regras

Com ligação ao SQL Server, o ecrã lê a razão da API: código e nome da conta,
saldo de abertura do intervalo, saldo final e os movimentos por ordem
cronológica com saldo acumulado. Sem ligação — ou quando a conta não existe na
base — recorre aos movimentos em memória, aplicando o mesmo cálculo (intervalo de
datas, saldo de abertura a partir dos movimentos anteriores à data inicial, saldo
corrente).

Quando há um intervalo definido, o ecrã mostra o cartão e a linha de **saldo de
abertura**; sem intervalo, mostra apenas o saldo atual.

Não há alteração de domínio, de infraestrutura nem de API — só o cliente passa a
usar um endpoint que já existia.

## Superfície de validação local

Ecrã `/contas/{código}`: cabeçalho com código e nome, cartões de saldo e número
de movimentos, dois campos de data e a tabela de movimentos com o saldo
acumulado e ligações para o lançamento e para o diário.

## Validação efetuada

- lint, dezasseis testes de interface (novo teste: a razão de `6221` mostra o saldo acumulado e, com data inicial posterior aos movimentos, passa-os para saldo de abertura e esvazia o período) e build de produção da PWA;
- verificação no browser contra a API: a razão de `6221` lê da BD (saldo 1200,00 €, um movimento ligado ao lançamento `COMP-2026-00842`); com data inicial `2026-08-30`, o movimento de 29/08 passa a saldo de abertura e o período fica vazio;
- suite .NET completa (54 testes) e `scripts/build.ps1 -SyntheticOnly` sem ocorrências;
- `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- saldo de abertura por conta na resposta do balancete;
- exportação da razão;
- paginação dos movimentos da conta;
- gráfico de evolução do saldo.
