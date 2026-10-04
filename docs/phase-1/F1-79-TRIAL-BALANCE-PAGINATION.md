# F1-79 — Paginação do balancete

## Objetivo

Última lista longa por paginar. Numa empresa grande (SAF-T com dezenas de
milhares de contas), o balancete desenhava uma linha por conta de uma só
vez — e o agrupamento por classe ou taxonomia não ajudava, porque continua
a desenhar todas as contas por baixo de cada cabeçalho.

## Regras

- **Sem agrupamento**: a lista plana pagina como os outros ecrãs —
  `paginate` (`src/app/pagination.ts`), pelo tamanho de página das
  preferências da empresa, voltando à primeira página quando um filtro
  muda.
- **Agrupada (por classe ou taxonomia)**: mostram-se sempre os cabeçalhos
  e os subtotais (poucas dezenas de linhas); as contas de cada grupo só se
  desenham quando o total cabe numa página. Acima disso, cada cabeçalho
  indica quantas contas resume e um aviso remete para desligar o
  agrupamento (lista paginada) ou pesquisar. Os subtotais são sempre do
  grupo inteiro.
- Os totais no rodapé, a contagem de contas e a exportação CSV continuam a
  ser do balancete inteiro, em qualquer modo.

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Num balancete com mais contas do que o tamanho de página: sem agrupamento,
mostra uma página de cada vez com navegação Anterior/Seguinte; agrupado,
abre de imediato só com os subtotais por classe/taxonomia.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e quatro testes de interface
  (a paginação já está coberta pelos testes unitários de `paginate`);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: o balancete plano passa a mostrar «Página 1 de N» e desenha de
  imediato; agrupado por classe, abre só com os subtotais das classes.

## Fora deste incremento

- paginação do lado do servidor (a lista continua a carregar tudo de uma
  vez; só o desenho é que é por página);
- expandir uma classe/rubrica específica para ver só as contas dela quando
  o agrupamento está a mostrar só subtotais.
