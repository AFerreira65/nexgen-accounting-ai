# F1-40 — Livro diário filtrado por conta

## Objetivo

Poder isolar no livro diário os lançamentos que movimentam uma conta — vendo o
lançamento inteiro e equilibrado, não só a linha dessa conta como na razão.

## Regras

`JournalReportService.BuildAsync` recebe `accountCode` opcional; quando indicado,
mantém apenas os lançamentos com pelo menos uma linha nessa conta (comparação
exata do código, maiúsculas). As linhas devolvidas continuam a ser todas as do
lançamento e os totais somam essas linhas.

O endpoint `GET .../accounting/journal-report` aceita `account=<código>`.

Na PWA, o **Livro diário** ganha um campo **Conta** (com sugestões das contas de
movimento); em modo de demonstração o filtro é aplicado em memória. A barra de
filtros passa a poder quebrar para uma segunda linha em vez de comprimir os
campos.

Sem alteração de esquema.

## Superfície de validação local

`GET .../accounting/journal-report?account=6221` devolve só os lançamentos que
tocam `6221`; um código inexistente devolve `"entries":[]`.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: `BuildAsync(..., accountCode: "6221")` devolve apenas lançamentos com uma linha em `6221` (incluindo `COMP-2026-00842`); `"NAO-EXISTE"` devolve zero;
- teste de API: `/foundation` indica a fase `F1-40`; `journal-report?account=6221` inclui `COMP-2026-00842`; `account=NAO-EXISTE` devolve `"entries":[]`;
- verificação no browser contra a base `accountingAI`: no Livro diário, filtrar pela conta `2211001` reduz de 5 para 3 lançamentos (abertura, pagamento e compra), todos com uma linha nessa conta;
- teste de interface: filtrar por `7211` deixa `VEND-2026-00327` e remove `COMP-2026-00842`;
- suite .NET completa (59 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- filtro por conta e subcontas (prefixo do código);
- filtro por conta na exportação do balancete ou do razão consolidado;
- realce da linha da conta filtrada dentro de cada lançamento.
