# F1-68 — Corrigir e duplicar a partir do detalhe do movimento

## Objetivo

F1-65 trouxe Conferir/Reabrir/Anular para o detalhe do movimento, mas Corrigir e
Duplicar continuavam só na lista, porque o editor vivia dentro de
`MovementsPage.tsx`.

## Regras

- `MovementEditor` (e o seu `AccountField`) foi extraído para
  `src/pages/MovementEditor.tsx`, sem alteração de comportamento; a lista e o
  detalhe importam-no.
- O detalhe do movimento ganha **Corrigir** (só quando por conferir, com a mesma
  guarda de exercício apurado/período bloqueado) e **Duplicar** (sempre, exceto
  para `AP`/`AB`), abrindo o mesmo editor da lista.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-66.

## Superfície de validação local

No detalhe de um movimento por conferir, «Corrigir» abre o editor a corrigi-lo;
«Duplicar» abre-o a partir de qualquer movimento não `AP`/`AB` para criar um novo.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build (bundle idêntico depois da extração, confirmando
  que não há duplicação nem perda de código) e trinta e sete testes de interface
  (um novo: abrir «Duplicar» e «Corrigir» a partir do detalhe mostra o título certo
  do editor);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa do Continente
  (exercício de 2026 apurado), «Corrigir» aparece desativado com o motivo
  «Exercício apurado» e «Duplicar» abre o editor; na empresa da ilha (exercício
  aberto), «Corrigir» abre o editor a corrigir o lançamento; os editores foram
  fechados sem gravar, sem alterações na base.

## Fora deste incremento

- pré-visualizar a duplicação sem abrir o formulário completo;
- editor mais compacto para o detalhe (usa o mesmo painel da lista).
