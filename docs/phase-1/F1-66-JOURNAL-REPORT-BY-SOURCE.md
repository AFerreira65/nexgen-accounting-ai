# F1-66 — Filtro de origem no livro diário

## Objetivo

A lista de movimentos já filtra por origem (F1-63); o livro diário não. Falta
poder isolar no livro diário os lançamentos manuais, importados do SAF-T, de
abertura ou de apuramento.

## Regras

- `JournalReportService.BuildAsync` aceita `source` (nome do valor `EntrySource`,
  sem distinção de maiúsculas; um valor desconhecido é ignorado) e filtra os
  lançamentos por essa origem. Cada `JournalReportEntry` passa a levar `Source`.
- O endpoint `GET .../accounting/journal-report` aceita `?source=` e devolve
  `source` em cada lançamento.
- No ecrã «Livro diário», um filtro «Origem» na barra (Manual / Importado do
  SAF-T / Abertura / Apuramento) e uma coluna «Origem» na exportação CSV; o
  cálculo em modo de demonstração filtra do mesmo modo.

Sem alteração de esquema. A fase do `/foundation` passa a **F1-66**.

## Superfície de validação local

No «Livro diário», o filtro «Origem» reduz os lançamentos aos dessa proveniência;
o CSV inclui a coluna «Origem».

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `NexGenAccountingAI.IntegrationTests` (LocalDB descartável): `BuildAsync` com
  `source = "Manual"` devolve os mesmos lançamentos que sem filtro (todos manuais
  nessa fase), `"SaftImport"` devolve zero e um valor inválido é ignorado; cada
  lançamento traz `Source`;
- `NexGenAccountingAI.Api.Tests`: o `/foundation` indica a fase `F1-66`;
- `NexGenAccountingAI.Web`: lint, build e trinta e seis testes de interface (o teste do
  livro diário passa também pelo filtro «Origem»);
- verificação no browser contra a base `accountingAI`: `?source=Opening` devolve os
  dois `AB-*` e `?source=Closing` o `AP-2026-00001`; no ecrã, «Origem = Abertura»
  passa o livro diário de 6 para 2 lançamentos (os dois `AB`);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos.

## Fora deste incremento

- filtro de origem na razão de conta;
- mostrar a origem numa coluna do livro diário no ecrã (só vai no CSV).
