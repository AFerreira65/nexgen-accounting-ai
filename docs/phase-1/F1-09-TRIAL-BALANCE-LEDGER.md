# F1-09 — Balancete e razão de conta, sem anulados

## Objetivo

Depois de a anulação passar a existir (F1-08), garantir que os lançamentos
anulados não entram nas somas e nos saldos, e dar as duas leituras contabilísticas
básicas: balancete e razão de conta.

Ficam disponíveis:

- balancete: somas de débito e crédito e saldo por conta de movimento;
- razão de conta: movimentos da conta por ordem cronológica com saldo acumulado;
- ambos filtrados por empresa e a excluir os lançamentos anulados.

## Regras

`AccountLedgerService` calcula as agregações diretamente sobre
`JournalEntryLines`, sempre com `JournalEntry.Status != Voided` e
`CompanyId == companyId`. O balancete agrupa por conta; a razão ordena por data,
referência e sequência e acumula o saldo.

O ecrã de razão da conta e o dashboard deixam de contar os movimentos anulados.

Não há alteração de esquema.

## Superfície de validação local

```text
GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/trial-balance
GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/accounts/{code}/ledger
```

O balancete devolve as linhas e os totais; a razão devolve as entradas com saldo
acumulado. Contas inexistentes e ausência de associação devolvem `404`.

Na PWA, o novo ecrã **Balancete** apresenta a tabela com totais; a **Razão da
conta** ganha a coluna de saldo acumulado. Sem ligação ao SQL Server, ambos são
derivados dos movimentos em memória, também sem os anulados.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: após anular um lançamento e corrigir outro, o balancete mantém débito igual a crédito e a conta `6221` soma 1245 (1200 do seed mais 45 da correção, sem os 100 anulados); a razão da conta tem duas entradas, saldo 1245 e não inclui a referência anulada; uma conta inexistente devolve nulo;
- teste de API para o balancete e a razão da empresa ativa, conta inexistente e empresa sem associação;
- lint, treze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- balancete por período e comparação entre exercícios;
- saldos de abertura e movimento de abertura do exercício;
- razão com filtro por intervalo de datas;
- exportação do balancete e da razão;
- estorno automático na anulação e numeração por tipo de documento.
