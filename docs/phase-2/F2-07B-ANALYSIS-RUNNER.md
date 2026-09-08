# F2-07B — Execução das análises (origem Movimentos)

Segundo sub-incremento do [F2-07](F2-07-CONFIGURABLE-REPORTS.md). Corre uma
`AnalysisDefinition` e devolve o resultado com subtotais e total geral.

## Regras

- `IAnalysisRunner` / `AnalysisRunner` (`Infrastructure.SqlServer`) — para a
  origem `Movimentos`:
  1. valida a definição contra o [catálogo](F2-07A-SAVED-ANALYSES.md) (lança
     `ArgumentException` se falha);
  2. verifica o acesso à empresa (devolve `null` se não tem);
  3. no **SQL**: filtra `JournalEntryLines` da empresa **sem os anulados**, pelo
     intervalo de datas, e projeta uma linha achatada com todos os campos do
     catálogo (junção a `JournalEntry`, `Account`, `Journal`, `DocumentType`);
     ordena por data, referência e sequência;
  4. em **memória**: aplica os filtros da definição (operador conforme o tipo
     do campo), parando ao atingir `AnalysisResult.RowLimit` (**5 000**) — nesse
     caso `Truncated = true`;
  5. ordena pelos campos de agrupamento e depois pela ordenação da definição;
  6. constrói os **grupos em todos os níveis** (para `groupBy` `[a, b]`:
     grupos de nível `a` e grupos de nível `(a, b)`), cada um com os subtotais
     dos agregados e a contagem de linhas;
  7. calcula o **total geral** dos agregados.
- Agregados: `sum`, `count`, `average`, `min`, `max`. A chave de cada valor no
  resultado é `campo:função` (ex.: `debit:sum`). `count` funciona sobre
  qualquer campo.
- `AnalysisResult` (`Application/Reporting`) — `{ columns:[{key,label,type}],
  rows:[{campo→valor}], groups:[{keys:[{field,label,value}], subtotals, rowCount}],
  grandTotal, rowCount, truncated }`.
- **API:** `POST .../companies/{companyId}/analyses/run?from=&to=` (definição
  em linha, para a pré-visualização do construtor) e
  `GET .../analyses/{id}/run?from=&to=` (análise guardada). Definição inválida
  → `400`; sem acesso / análise inexistente → `404`. Saída com as opções de
  JSON do módulo (enums em texto).

Sem alteração de esquema.

## Superfície de validação local

```
POST /api/v1/development/users/{userId}/companies/{companyId}/analyses/run?from=2026-01-01&to=2026-12-31
{ "definition": { "source": "movimentos", "columns": ["accountCode","debit"], "groupBy": ["accountCode"], "aggregates": [{"field":"debit","function":"sum"}], ... } }
```

## Validação efetuada

- `AccountingAI.IntegrationTests`: teste novo `AnalysisRunnerTests` — três
  lançamentos, filtro `debit > 0`, agrupamento por conta: dois grupos
  (`6221` = 350 / 2 linhas, `6262` = 40), total geral 390 / 3; um período mais
  estreito devolve dois; definição inválida lança `ArgumentException`; sem
  acesso devolve `null`.
- `AccountingAI.Api.Tests`: teste novo — `analyses/run` devolve `columns`,
  `groups`, `grandTotal` e `debit:sum`; definição inválida `400`; empresa sem
  acesso `404`.
- `dotnet build`/`dotnet test` na solução inteira (cento e quarenta e seis
  testes .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (empresa Vidreira Algarvia,
  exercício de 2025): correr uma análise do 1.º trimestre com filtro
  `accountCode` começa por «62» e agrupamento diário × conta devolve 1 286
  linhas em 113 grupos em ~0,9 s; uma análise do ano inteiro sem filtro
  devolve 5 000 linhas com `truncated = true`.

## Fora deste incremento

- o ecrã «Análises» na PWA (construtor + tabela agrupada + impressão) — F2-07C;
- a origem `SaldosDeConta` — F2-07E;
- os modelos de impressão dos relatórios fixos — F2-07D.
