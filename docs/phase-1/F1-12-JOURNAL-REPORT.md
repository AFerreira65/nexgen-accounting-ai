# F1-12 — Livro diário

## Objetivo

Dar a leitura contabilística clássica que faltava: o livro diário — lançamentos
por ordem cronológica com as respetivas linhas e totais — usando os filtros
introduzidos até aqui (diário, tipo de documento, exercício).

## Regras

`JournalReportService.BuildAsync(companyId, journalCode?, documentTypeCode?, year?)`
consulta os lançamentos da empresa por ordem de data e referência, com as linhas
e as contas, e soma os totais de débito e crédito. Exclui sempre os lançamentos
anulados. Cada filtro é opcional e acumulativo.

Não há alteração de esquema.

## Superfície de validação local

```text
GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/journal-report
      ?journal={código}&documentType={código}&year={exercício}
```

Devolve os lançamentos com as linhas e os totais. Empresa sem associação devolve
`404`.

Na PWA, o novo ecrã **Livro diário** apresenta os lançamentos com as linhas por
extenso, os três filtros e o rodapé de totais; o exercício assume por defeito o
exercício predefinido do utilizador na empresa ativa. Sem ligação ao SQL Server,
o relatório é derivado dos movimentos em memória, também sem os anulados.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: o livro diário do diário `VENDAS` inclui as faturas `FTV`, mantém débito igual a crédito, o filtro por tipo `FTV` devolve exatamente as duas faturas, o filtro por exercício antigo devolve vazio e o relatório completo não inclui o lançamento anulado;
- teste de API: o livro diário devolve os lançamentos, o filtro por diário exclui os restantes e o filtro por exercício sem lançamentos devolve `"entries":[]`;
- lint, quinze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- balancete e razão por período (intervalo de datas);
- exportação do livro diário;
- somas por conta dentro do diário e por classe;
- séries por tipo de documento e exercício com contadores persistidos;
- estorno automático na anulação.
