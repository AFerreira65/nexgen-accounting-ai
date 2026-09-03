# F1-46 — Razão das contas de integração

## Objetivo

Ao consultar uma conta agregadora (de integração), mostrar os movimentos e o
saldo acumulado da soma das contas de movimento com o mesmo prefixo de código.

## Regras

`AccountLedgerService.AccountLedgerAsync`: se a conta pedida não permite
movimento (`AllowsPosting == false`), a razão passa a considerar as linhas de
todas as contas da empresa cujo código começa pelo código pedido (subárvore),
em vez de só as linhas dessa conta. O saldo de abertura, o intervalo de datas e
o saldo acumulado calculam-se sobre esse conjunto combinado.

`AccountLedgerEntry` ganha `AccountCode` (a conta de movimento de cada linha) e
`AccountLedger` ganha `IsAggregate`. O endpoint da razão devolve ambos.

Na PWA, a **Razão da conta** de uma conta agregadora mostra o texto «Conta de
integração: soma dos movimentos das subcontas…», uma coluna **Conta** com a
subconta de cada linha (com ligação) e inclui essa coluna na exportação CSV.

Sem alteração de esquema.

## Superfície de validação local

`GET .../accounting/accounts/{código}/ledger` de uma conta agregadora devolve
`"isAggregate": true` e uma entrada por linha das subcontas, cada uma com o seu
`accountCode`. No ecrã **Razão da conta**, abrir uma conta de 1 dígito soma as
subcontas.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: `AccountLedgerAsync("6221")` tem `IsAggregate == false` e todas as entradas com `AccountCode == "6221"`; `AccountLedgerAsync("6")` tem `IsAggregate == true`, mais entradas e todas com código a começar por `6`, incluindo `6221`;
- teste de API: `/foundation` indica a fase `F1-46`; `accounts/6221/ledger` devolve `"isAggregate":false`, `accounts/6/ledger` devolve `"isAggregate":true` e entradas com `"accountCode":"6221"`;
- verificação no browser contra a base `accountingAI`: `contas/6` mostra «Conta de integração…», a coluna **Conta** com `6221` e o saldo acumulado a fechar em 0 (classe 6 apurada);
- teste de interface: `contas/1` em modo de demonstração soma a subconta `1211` do lançamento `BANC-2026-00196` e mostra a coluna **Conta**;
- suite .NET completa (65 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- agrupar a razão agregada por subconta;
- saldos de abertura por subconta no cabeçalho;
- mesma soma no livro diário filtrado por conta agregadora (hoje o filtro é por código exato).
