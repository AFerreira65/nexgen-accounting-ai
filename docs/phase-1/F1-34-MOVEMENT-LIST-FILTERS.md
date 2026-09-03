# F1-34 — Filtros na lista de movimentos

## Objetivo

Tornar útil a barra de pesquisa da lista de movimentos, que até aqui não fazia
nada, e acrescentar filtro por diário e por estado.

## Regras

No ecrã **Movimentos contabilísticos**:

- a caixa de pesquisa filtra por referência, descrição ou documento;
- um seletor **Diário** filtra pelo diário do lançamento;
- um seletor **Estado** filtra por «Por conferir», «Conferido» ou «Anulado»;
- um contador mostra «N de M» e, quando os filtros não deixam nada, aparece
  «Nenhum movimento corresponde aos filtros».

Os filtros são acumulativos e aplicados no cliente sobre a lista já carregada.
Sem alteração de API, domínio nem esquema.

## Validação efetuada

- lint, vinte e oito testes de interface (novo: filtrar por estado «Anulado» esvazia a lista e por «Por conferir» deixa só o lançamento pendente) e build de produção da PWA;
- suite .NET completa (57 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- filtro por intervalo de datas e por tipo de documento;
- persistência dos filtros entre visitas;
- paginação da lista.
