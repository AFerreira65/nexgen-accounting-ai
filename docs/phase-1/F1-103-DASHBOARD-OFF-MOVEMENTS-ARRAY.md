# F1-103 — Painel sem o array de movimentos

## Objetivo

Sétimo passo do arco. O painel lia o array completo de movimentos para
três coisas: a contagem de movimentos, a de «por conferir» e a lista dos
oito mais recentes. Numa empresa de SAF-T isso obriga a carregar o
histórico todo só para o resumo da abertura da app.

## Regras

- As contagens («Movimentos», «Por conferir») e a lista «A sua atenção»
  passam a vir de `useMovementsSummary` (o endpoint de resumo de F1-99).
  O resumo ganhou o campo `voided`, para «Movimentos» continuar a contar
  só os não anulados (`total − voided`).
- «A sua atenção» mostra a antevisão de até sete lançamentos por conferir
  do resumo, com um «Ver todos os N» quando há mais — antes desenhava
  todos, sem limite.
- «Movimentos recentes» vem de `GET .../accounting/movements?page=1&pageSize=8`
  quando a empresa está ligada à base de dados; em demonstração — ou se o
  pedido falhar — usa os oito primeiros do array do contexto.
- Os cartões de posição financeira continuam a vir do balancete
  (`trial-balance`), com o cálculo sobre os movimentos só como recurso.

Sem alteração de esquema. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir o painel numa empresa ligada à BD faz um pedido de resumo e um da
primeira página de oito movimentos, sem depender do histórico inteiro.

## Validação efetuada

- `NexGenAccountingAI.Api`: dezasseis testes (o do resumo passou a exigir o
  campo `voided`);
- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em demonstração o painel usa o array do contexto, como
  antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia: o painel mostra «Movimentos 27311», «Por conferir 0», a lista
  recente dos oito últimos lançamentos e «Todos os movimentos
  conferidos», tudo de `movements/summary` e `movements?page=1&pageSize=8`.

## Fora deste incremento

- as contagens de movimentos por diário no ecrã de diários e o
  carregamento do array completo pelo `WorkContext` — últimos passos do
  arco.
