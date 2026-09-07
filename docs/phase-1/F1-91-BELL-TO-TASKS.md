# F1-91 — O sino da barra de topo leva às Tarefas

## Objetivo

O sino de notificações na barra de topo não fazia nada e tinha sempre o
ponto vermelho aceso — o último controlo decorativo. Não há um sistema de
notificações; o que há de pendente está nas Tarefas.

## Regras

- O sino passa a ser uma ligação a `/tarefas`.
- O ponto acende só quando há trabalho pendente na empresa ativa —
  movimentos por conferir ou contas de movimento por classificar (os
  mesmos itens acionáveis do ecrã de Tarefas; o lembrete de IVA, sendo
  informativo e dispensável, fica de fora).
- O nome acessível passa a «Tarefas» e, com pendências, «Tarefas — N
  pendentes».

Sem alteração de esquema nem de API — sobre dados já carregados. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Com movimentos por conferir ou contas por classificar, o sino tem o ponto
aceso; clicar nele abre as Tarefas. Sem nada pendente, o ponto apaga-se.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e nove testes de interface (um
  novo: clicar no sino abre as Tarefas);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: na empresa Vidreira
  Algarvia (contas por classificar) o sino tem o ponto e abre as Tarefas.

## Fora deste incremento

- um centro de notificações a sério (sem eventos para mostrar, por agora).
