# F1-73 — Lembrete de IVA a entregar nas Tarefas

## Objetivo

F1-70 deixou de fora o lembrete de IVA a entregar por «exigir carregar o
balancete, como no dashboard». Na prática não exige: a posição de IVA usa só
as contas 24.3, e o `WorkContext` já carrega todos os movimentos da empresa
ativa — dá para calcular sem nenhum pedido adicional, mantendo a regra do
ecrã de Tarefas.

## Regras

- Nova secção «IVA a entregar» em `/tarefas`, antes de «Movimentos por
  conferir», quando a posição de IVA atual (2433 liquidado menos 2432
  dedutível, sobre os movimentos já carregados, sem os anulados — a mesma
  fórmula do dashboard e do Resumo de IVA) é positiva; com ligação a
  `/iva`. Posição nula ou a recuperar não mostra nada.
- «Tudo em dia» passa a exigir também não haver IVA a entregar.

Sem alteração de esquema nem de API — a fase do `/foundation` mantém-se em
F1-71.

## Superfície de validação local

Em «Tarefas», com a empresa ativa a ter uma posição de IVA a entregar
positiva, aparece a secção «IVA a entregar» com o valor e uma ligação ao
Resumo de IVA.

## Validação efetuada

- `AccountingAI.Web`: lint, build e trinta e oito testes de interface (o
  teste das Tarefas passa a confirmar também a secção «IVA a entregar» com
  os 299,00 € da posição sintética e a ligação a `/iva`);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: nas duas empresas de
  teste a secção aparece só quando a posição de IVA corrente é positiva, com
  o mesmo valor do cartão «Posição de IVA» do dashboard, e a ligação abre o
  Resumo de IVA.

## Fora deste incremento

- marcar o lembrete como dispensado até à próxima entrega.
