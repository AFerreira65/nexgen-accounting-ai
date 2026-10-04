# F1-95 — Testes das funções de período

## Objetivo

As funções de `src/app/periods.tsx` calculam os intervalos de datas de que
todos os relatórios dependem (atalhos de período, arranque no exercício de
F1-92/93/94) e não tinham nenhum teste. A aritmética de datas nesta base já
teve enganos subtis no passado; convém fixá-la.

## Regras

- Novo ficheiro `src/app/periods.test.ts`, só sobre as funções puras
  (sem alteração de comportamento):
  - `yearRange` — 1 de janeiro a 31 de dezembro do exercício;
  - `periodReference` — devolve hoje enquanto está dentro do exercício,
    trava em 31 de dezembro quando hoje já passou o fim do exercício, e é
    exata no último dia;
  - `monthRange` — limita o mês de calendário, incluindo os 29 dias de
    fevereiro num ano bissexto e 28 num ano comum;
  - `quarterRange` — janeiro→T1, maio→T2, dezembro→T4.

Sem alteração de esquema, API nem comportamento. A fase do `/foundation`
mantém-se em F1-71.

## Superfície de validação local

`npm run test` cobre agora as funções de período.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e sessenta testes de interface (dez
  novos, unitários, para as funções de período);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos.

## Fora deste incremento

- teste unitário do hook `useExerciseDateRange` (já coberto de forma
  indireta pelos testes dos ecrãs que o usam).
