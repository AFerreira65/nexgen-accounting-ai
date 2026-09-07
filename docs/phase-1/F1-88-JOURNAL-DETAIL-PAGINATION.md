# F1-88 — Paginação do detalhe do diário

## Objetivo

O ecrã de um diário (`/diarios/:code`) desenhava todos os lançamentos
desse diário. Numa empresa importada de SAF-T, o diário de vendas de um
ano tem milhares de lançamentos — abrir o ecrã (a partir da lista de
movimentos, do livro diário ou da pesquisa) bloqueava o browser.

## Regras

- `JournalPage` pagina os lançamentos do diário pelo tamanho de página das
  preferências da empresa (`usePagedList`/`Pager` partilhados), voltando à
  primeira página ao mudar de diário.
- A contagem no cabeçalho continua a ser a do diário inteiro; passa a
  haver um estado «Sem movimentos neste diário» quando está vazio.

Sem alteração de esquema nem de API — puramente do lado do cliente, sobre
os movimentos já carregados. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir um diário com muitos lançamentos mostra uma página de cada vez, com
navegação Anterior/Seguinte, e abre de imediato.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e sete testes de interface
  (um novo: abrir `/diarios/COMPRAS` mostra o diário e o seu lançamento de
  demonstração);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: o diário de vendas (milhares de lançamentos) abre de imediato
  com «Página 1 de N».

## Fora deste incremento

- paginação do lado do servidor.
