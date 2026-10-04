# F1-63 — Origem do lançamento

## Objetivo

Depois de importar movimentos de um SAF-T ou de correr a abertura/apuramento, não
havia como distinguir esses lançamentos dos que foram feitos à mão. Falta marcar a
origem de cada lançamento.

## Alteração de esquema

Nova coluna `Source` (`int`, não nula, por omissão `0` = Manual) em
`accounting.JournalEntries`. A migração `20260904102947_JournalEntrySource`
acrescenta a coluna e retroage os lançamentos existentes: os do tipo de documento
`AB` passam a `Opening` (2) e os `AP` a `Closing` (3); os restantes ficam `Manual`
(0). Não há marca para os lançamentos já importados de SAF-T, que ficam `Manual`.

## Regras

- `JournalEntry` ganha `Source` (`EntrySource`: `Manual`, `SaftImport`, `Opening`,
  `Closing`), por omissão `Manual`, e `SetSource(...)` (definido antes de gravar).
- `JournalPostingService.PostAsync` aceita um parâmetro `source` (por omissão
  `Manual`); `ReviseAsync` preserva a origem do lançamento corrigido.
- `SaftEntriesImportService` marca `SaftImport`; `YearOpeningService` e
  `SaftOpeningBalancesImportService` marcam `Opening`; `YearEndClosingService`
  marca `Closing`.
- `GET .../accounting/movements` devolve `source` em cada lançamento e aceita
  `?source=` (nome do valor, sem distinção de maiúsculas; um valor desconhecido é
  ignorado).
- Na lista de movimentos, um filtro «Origem» (Manual / Importado do SAF-T /
  Abertura / Apuramento) e, na linha, a origem a seguir ao documento quando não é
  manual. A exportação CSV ganha a coluna «Origem». Os rótulos pt-PT estão em
  `src/app/entrySources.ts`.

## Superfície de validação local

Na lista de movimentos, o filtro «Origem» isola os lançamentos de abertura,
apuramento ou importados; a linha mostra a origem e o CSV inclui-a.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `NexGenAccountingAI.Domain.Tests`: um lançamento novo tem `Source = Manual` e
  `SetSource` altera-o;
- `NexGenAccountingAI.IntegrationTests` (LocalDB descartável): a migração
  `20260904102947_JournalEntrySource` é aplicada; os movimentos importados de um
  SAF-T ficam `SaftImport`, o apuramento fica `Closing` e a abertura `Opening`;
- `NexGenAccountingAI.Api.Tests`: a lista de movimentos traz `"source":"Manual"` e
  `?source=SaftImport` não devolve os lançamentos manuais;
- `NexGenAccountingAI.Web`: lint, build e trinta e seis testes de interface (o filtro da
  lista passa também pela «Origem»: `Manual` mantém tudo, `SaftImport` esvazia);
- verificação no browser contra a base `accountingAI`: ao arrancar, a migração
  retroage `AB-*`/`AB-2026-00001` para `Opening` e `AP-2026-00001` para `Closing`;
  importar um SAF-T na empresa da ilha cria um lançamento `SaftImport`;
  `?source=Opening`/`Closing` filtram corretamente e um valor inválido é ignorado;
  na lista, o filtro «Origem» = Abertura mostra os dois `AB` e o CSV traz a coluna
  «Origem»; o lançamento de teste foi removido da base no fim;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos.

## Fora deste incremento

- registar quem lançou/alterou e quando (rasto completo);
- marcar retroativamente como `SaftImport` os movimentos já importados;
- filtrar por origem no livro diário e na razão.
