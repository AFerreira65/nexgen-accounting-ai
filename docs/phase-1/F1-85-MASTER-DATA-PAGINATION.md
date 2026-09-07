# F1-85 — Paginação do plano de contas e das taxonomias

## Objetivo

As últimas listas grandes por paginar. O plano de contas e o mapa de
taxonomias de uma empresa importada de SAF-T têm dezenas de milhares de
linhas — abrir qualquer um dos dois ecrãs bloqueava o browser.

## Regras

- `AccountsPage` e `TaxonomiesPage` paginam a lista já filtrada pelo
  tamanho de página das preferências da empresa (hook partilhado
  `usePagedList` + componente `Pager` neste ficheiro, sobre o `paginate`
  de F1-75), voltando à primeira página quando a pesquisa muda.
- A pesquisa, a contagem no cabeçalho e as ações (editar, aplicar
  sugestão) continuam a ser sobre o conjunto inteiro.

Sem alteração de esquema nem de API — puramente do lado do cliente, sobre
dados já carregados. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Num plano de contas ou mapa de taxonomias com mais contas do que o tamanho
de página, o ecrã mostra uma página de cada vez com navegação Anterior/
Seguinte e abre de imediato.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e seis testes de interface (a
  paginação já está coberta pelos testes unitários de `paginate`);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: o plano de contas (44 693 contas) e as taxonomias abrem de
  imediato, com «Página 1 de N»; pesquisar volta à primeira página.

## Fora deste incremento

- paginação do lado do servidor para o plano de contas (hoje carrega-se o
  plano inteiro no contexto de trabalho).
