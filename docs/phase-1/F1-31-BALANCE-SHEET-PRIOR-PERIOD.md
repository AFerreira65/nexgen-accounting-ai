# F1-31 — Comparativo com o período homólogo no balanço

## Objetivo

Fechar o conjunto: o balanço passa a poder ser lido contra o período homólogo,
como já acontece no balancete (F1-30) e na demonstração de resultados (F1-29).

## Regras

No ecrã **Balanço**, o interruptor **Comparar com período homólogo** — disponível
com a data inicial e a data final preenchidas — carrega um segundo balancete para
o mesmo intervalo recuado um ano e recompõe o balanço homólogo (mesma
classificação por saldo: ativo, passivo, capital próprio e resultado).

Com a comparação ligada, cada subclasse e cada total das três secções ganha as
colunas **Homólogo** e **Variação**; os cartões do topo mostram o valor homólogo
e a exportação CSV inclui as duas colunas.

Alteração apenas no cliente (`BalanceSheetPage.tsx`); reutiliza o endpoint do
balancete. Sem alteração de API, domínio nem esquema.

## Validação efetuada

- lint, vinte e sete testes de interface (o teste do balanço liga a comparação e confirma a coluna «Homólogo») e build de produção da PWA;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- comparação ao nível da conta de movimento;
- escolha livre do período de comparação;
- balanço por rubricas oficiais da Portaria 220/2015.
