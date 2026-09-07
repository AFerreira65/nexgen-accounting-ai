# F1-82 — Fechar os menus da barra de topo ao clicar fora

## Objetivo

O seletor de empresa e o seletor de exercício ficavam abertos ao clicar
noutro sítio ou ao carregar em Escape — só fechavam ao clicar de novo no
próprio botão ou ao escolher uma opção. Papercut antigo do seletor de
empresa, herdado pelo de exercício (F1-80).

## Regras

- Novo hook `useDismiss(active, ref, onClose)` (`src/app/useDismiss.ts`):
  enquanto `active`, fecha ao `pointerdown` fora do contentor referido ou
  ao Escape; não escuta quando fechado.
- `AppLayout` usa-o nos dois menus da barra de topo. Carregar no próprio
  botão continua a alternar (o `pointerdown` é dentro do contentor, só o
  `click` a seguir alterna); escolher uma opção continua a fechar.

Sem alteração de esquema nem de API. A fase do `/foundation` mantém-se em
F1-71.

## Superfície de validação local

Abrir o seletor de empresa ou de exercício e clicar fora dele, ou carregar
em Escape: o menu fecha.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e cinco testes de interface
  (o teste do seletor de exercício passa também por abrir o menu e fechá-lo
  com Escape sem escolher nada);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser: abrir cada um dos dois menus e clicar no corpo
  da página fecha-o; Escape também.

## Fora deste incremento

- aplicar o mesmo a outros menus/popovers da app que ainda não fechem ao
  clicar fora.
