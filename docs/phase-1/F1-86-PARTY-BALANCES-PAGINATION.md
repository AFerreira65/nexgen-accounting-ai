# F1-86 — Paginação das contas correntes

## Objetivo

O último ecrã de lista por limitar. As contas correntes de uma empresa
importada de SAF-T têm centenas de contas de clientes e fornecedores com
saldo — os dois painéis desenhavam-nas todas de uma vez.

## Regras

- Cada painel (clientes, fornecedores) pagina a lista já filtrada pelo
  tamanho de página das preferências da empresa, voltando à primeira
  página quando a pesquisa ou a data de corte mudam.
- Os totais, as contagens dos cartões e a exportação CSV continuam a ser
  sobre o conjunto inteiro.
- O hook `usePagedList` e o componente `Pager` passam para um ficheiro
  partilhado (`src/app/Pager.tsx`), reutilizados também pelo plano de
  contas e pelas taxonomias (F1-85).

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Num ecrã de contas correntes com mais contas do que o tamanho de página,
cada painel mostra uma página de cada vez com navegação Anterior/Seguinte.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e seis testes de interface (o
  teste das contas correntes continua válido — a empresa de demonstração
  tem uma conta de cliente e nenhuma de fornecedor);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: clientes e fornecedores abrem de imediato, com «Página 1 de N»
  em cada painel; pesquisar volta à primeira página.

## Fora deste incremento

- paginação do lado do servidor.
