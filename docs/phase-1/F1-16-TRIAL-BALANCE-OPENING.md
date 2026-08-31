# F1-16 — Saldo de abertura por conta no balancete

## Objetivo

Completar o balancete por período: quando há data inicial, cada conta passa a
mostrar o saldo de abertura (movimentos anteriores a essa data), os movimentos do
período e o saldo final. Sem data inicial, o balancete fica igual ao que era.

## Regras

`AccountLedgerService.TrialBalanceAsync(companyId, fromDate?, toDate?)` calcula,
quando `fromDate` está definido, o saldo de abertura de cada conta a partir da
soma de débito menos crédito dos lançamentos não anulados anteriores a essa data.
`TrialBalanceRow` ganha `OpeningBalance`; `Balance` passa a ser
`OpeningBalance + Débito − Crédito` e `TrialBalance` ganha `TotalOpeningBalance`.

Uma conta que só tenha movimento anterior ao período aparece na mesma, apenas com
o saldo de abertura. Uma conta sem abertura e sem movimento no período não
aparece. O saldo de abertura também equilibra (débito = crédito), pelo que
`TotalOpeningBalance` é zero num plano consistente.

Não há alteração de esquema.

## Superfície de validação local

```text
GET …/accounting/trial-balance?from={AAAA-MM-DD}&to={AAAA-MM-DD}
```

A resposta ganha `openingBalance` por linha e `totalOpeningBalance` no topo.

No ecrã **Balancete**, quando há data inicial, aparece a coluna **Abertura**
(linha e rodapé) e a exportação CSV inclui-a. Sem data inicial, a coluna fica
escondida. Sem ligação ao SQL Server, o mesmo cálculo é feito sobre os movimentos
em memória.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: com data inicial em agosto de 2026, a conta `6221` tem abertura 45 €, débito 1200 € e saldo 1245 €; a conta `11`, sem movimento em agosto, aparece só com abertura −45 €; `TotalOpeningBalance` é zero; sem data inicial, todas as aberturas são zero;
- teste de API: o balancete traz `totalOpeningBalance` e `openingBalance` por linha; o balancete com data inicial tem linhas e inclui `openingBalance`;
- teste de interface: a coluna `Abertura` só surge depois de preencher a data inicial;
- lint, vinte e um testes de interface e build de produção da PWA;
- verificação no browser contra a API: com data inicial `2026-08-28`, o balancete mostra a abertura por conta (Caixa 1000 €, banco −1476 €), os totais equilibram e as contas sem movimento no período aparecem só com abertura;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- balancete por classe/subtotais e por grau de conta;
- exportação em XLSX/PDF;
- séries por tipo de documento e exercício com contadores persistidos.
