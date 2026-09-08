# F1-106 — Um só pedido do resumo da fila de trabalho

## Objetivo

Consolidação deixada em aberto por F1-99/F1-103/F1-105. O sino da barra de
topo, o ecrã de Tarefas e o painel chamavam cada um o hook
`useMovementsSummary`, e cada chamada fazia o seu pedido a
`.../accounting/movements/summary` — dois a três pedidos iguais por troca
de empresa, com o risco de mostrarem contagens diferentes durante o
carregamento.

## Regras

- O `WorkContext` carrega o resumo uma vez (efeito próprio, com as mesmas
  dependências do carregamento de movimentos) e expõe-o em
  `movementsSummary`. Em demonstração calcula-o sobre o array; ligado à
  base de dados vem do endpoint, com `EMPTY_MOVEMENTS_SUMMARY` se falhar.
- `useMovementsSummary()` passa a ser um seletor fino
  (`useWorkContext().movementsSummary`) — a assinatura mantém-se, os três
  ecrãs não mudam de lógica, só o `import`.
- `src/app/movementsSummary.ts` fica sem React nem contexto: só os tipos,
  `EMPTY_MOVEMENTS_SUMMARY` e `summaryFromMovements` (o cálculo no
  cliente).

Sem alteração de esquema, API nem comportamento visível. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir Tarefas ou o painel numa empresa ligada à BD faz um único pedido de
resumo (antes eram dois ou três).

## Validação efetuada

- `AccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — o seletor mantém a assinatura);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia: as Tarefas e o painel fazem agora **um** pedido a
  `movements/summary` (antes dois/três); as contagens e a posição de IVA
  continuam corretas.

## Fora deste incremento

- nada em aberto do arco de paginação; as fases seguintes do roteiro
  (validação XSD/SVAT, cruzamento SAF-T × e-Fatura × contabilidade,
  adaptador PHC alargado, relatórios com IA) ainda não estão
  especificadas.
