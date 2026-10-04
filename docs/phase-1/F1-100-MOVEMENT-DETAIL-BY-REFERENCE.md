# F1-100 — Detalhe do movimento por referência

## Objetivo

Quarto passo do arco (F1-97…F1-99). O ecrã de detalhe de um movimento
procurava-o no array completo (`movements.find`). Numa empresa importada
de SAF-T esse array não carrega — e abrir um lançamento pelo link (ou por
URL partilhado) dava «Movimento não encontrado». Passa a um pedido por
referência.

## Regras

- Novo `GET .../accounting/movements/{reference}` — um lançamento com as
  linhas, na mesma forma do endpoint da lista (`ToMovementPayload`), ou
  404. O segmento literal `summary` do endpoint de F1-99 tem precedência
  sobre `{reference}`, por isso não há ambiguidade.
- `MovementPage` busca esse endpoint quando a empresa está ligada à base
  de dados (`encodeURIComponent` na referência — as referências de SAF-T
  têm espaços); mostra «A carregar o movimento…» enquanto espera e, se o
  pedido falhar, recorre ao array do contexto. Em demonstração continua a
  ler o contexto.
- Depois de conferir, reabrir, anular ou corrigir/duplicar, o ecrã volta
  a pedir o lançamento.

Sem alteração de esquema. Endpoint de `/development`. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir `/movimentos/{referência}` numa empresa ligada à BD carrega o
lançamento por pedido próprio; numa empresa cujo histórico completo não
chega, o detalhe passa a abrir na mesma.

## Validação efetuada

- `NexGenAccountingAI.Api`: dezasseis testes (um novo — leitura por referência
  com as linhas e o 404 de referência inexistente);
- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em demonstração o ecrã lê o contexto, como antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: na Almeida & Costa
  o detalhe de COMP-2026-00842 carrega por `GET .../movements/COMP-2026-00842`;
  na Vidreira Algarvia um lançamento cuja referência tem espaços
  (`2025-12-31 63 00630063120073`) abre em ~0,15 s pelo pedido codificado
  — antes dava «não encontrado».

## Fora deste incremento

- os restantes leitores do array do contexto — painel, pesquisa rápida,
  lista do diário, contagens por diário — cada um no seu incremento.
