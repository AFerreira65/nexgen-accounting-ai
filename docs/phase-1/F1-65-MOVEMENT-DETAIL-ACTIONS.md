# F1-65 — Ações de estado no ecrã de detalhe do movimento

## Objetivo

Conferir, reabrir ou anular um movimento obrigava a voltar à lista. O ecrã de
detalhe (`/movimentos/:id`) passa a ter essas ações.

## Regras

- No cabeçalho do detalhe, a par de «Copiar ligação»:
  - **Conferir** quando o lançamento está por conferir;
  - **Reabrir** quando está conferido e não é de apuramento/abertura (`AP`/`AB`);
  - **Anular** quando não está anulado.
- As mesmas guardas da lista: «Reabrir» e «Anular» ficam desativados, com o motivo
  no `title` e uma nota por baixo do cabeçalho, quando a data cai num exercício
  apurado ou no período bloqueado da empresa; os lançamentos `AP`/`AB` ficam
  isentos. Conferir não é guardado (como na API).
- A lógica das guardas (consulta de `closed-years` e `posting-lock-date`) foi
  extraída para o hook partilhado `useMovementGuards`
  (`src/app/movementGuards.ts`), usado pela lista e pelo detalhe.
- Ao concluir uma ação, o estado do lançamento e os botões atualizam-se.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-63.

## Superfície de validação local

No detalhe de um movimento, «Conferir» passa-o a conferido; «Reabrir» devolve-o a
por conferir; «Anular» anula-o. Num exercício apurado ou período bloqueado, os
botões Reabrir e Anular ficam desativados com o motivo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e trinta e seis testes de interface (o teste do
  detalhe passa também a confirmar os botões «Reabrir» e «Anular» num lançamento
  conferido);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: no detalhe de um lançamento
  por conferir de 2026 (exercício apurado), «Conferir» passa-o a conferido e os
  botões passam a «Reabrir»/«Anular» desativados com o `title` «Exercício
  apurado»; a alteração de teste na base foi revertida no fim.

## Fora deste incremento

- «Corrigir» e «Duplicar» no detalhe (implicam abrir o editor de movimentos, que
  vive na lista);
- histórico das ações no próprio ecrã.
