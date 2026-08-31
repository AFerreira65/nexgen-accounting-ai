# F1-13 — Relatórios contabilísticos por intervalo de datas

## Objetivo

Tornar o balancete, a razão de conta e o livro diário consultáveis por período.
Os três relatórios passam a aceitar uma data inicial e uma data final, para além
dos filtros que já tinham.

## Regras

`AccountLedgerService.TrialBalanceAsync(companyId, fromDate?, toDate?)` e
`JournalReportService.BuildAsync(companyId, journalCode?, documentTypeCode?, year?, fromDate?, toDate?)`
restringem os lançamentos considerados a `EntryDate` dentro do intervalo. Cada
limite é opcional; o exercício e o intervalo compõem-se.

`AccountLedgerService.AccountLedgerAsync(companyId, accountCode, fromDate?, toDate?)`
devolve agora o **saldo de abertura** do intervalo — a soma de débito menos
crédito dos lançamentos anteriores a `fromDate` — e o saldo corrente começa nesse
valor. Sem `fromDate`, o saldo de abertura é zero. O saldo final continua a ser o
acumulado de todos os movimentos apresentados.

Todos continuam a excluir os lançamentos anulados. Não há alteração de esquema.

## Superfície de validação local

```text
GET …/accounting/trial-balance?from={AAAA-MM-DD}&to={AAAA-MM-DD}
GET …/accounting/journal-report?journal=&documentType=&year=&from=&to=
GET …/accounting/accounts/{código}/ledger?from=&to=
```

As datas seguem o formato ISO `AAAA-MM-DD`. Data mal formada ou data final
anterior à inicial devolve `400` com mensagem em pt-PT. A resposta da razão
inclui `openingBalance`.

Na PWA, o **Balancete** e o **Livro diário** ganham dois campos de data na barra
de filtros; sem ligação ao SQL Server, o intervalo é aplicado sobre os movimentos
em memória.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: o balancete a partir de agosto de 2026 só apanha o lançamento do seed, o balancete até julho só apanha a correção; a razão da conta `6221` a partir de agosto devolve saldo de abertura de 45 €, um movimento e saldo final de 1245 €; o livro diário de agosto devolve apenas os lançamentos desse mês e um intervalo futuro devolve vazio;
- teste de API: o livro diário por intervalo devolve os lançamentos, um intervalo posterior devolve `"entries":[]`, uma data inválida devolve `400`, a razão inclui `openingBalance` e o balancete até 2020 devolve `"rows":[]`;
- verificação no browser: nos ecrãs Balancete e Livro diário, os campos de data filtram contra a API (o balancete até 2020 fica vazio; o livro diário a partir de agosto perde o lançamento de abertura de julho);
- lint, quinze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- ligação do ecrã Razão da conta ao endpoint da razão (continua a derivar dos movimentos em memória);
- saldo de abertura por conta na resposta do balancete;
- exportação dos relatórios;
- diários de vendas e de compras como vistas próprias;
- séries por tipo de documento e exercício com contadores persistidos.
