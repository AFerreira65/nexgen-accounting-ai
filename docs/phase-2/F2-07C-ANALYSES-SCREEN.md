# F2-07C — Ecrã «Análises» na PWA

Terceiro sub-incremento do [F2-07](F2-07-CONFIGURABLE-REPORTS.md). O construtor
e a execução das análises na interface — o equivalente às Análises Avançadas do
PHC.

## Regras

- Nova rota `/analises` (`AnalysesPage`) e entrada no menu lateral («Análises»,
  a seguir a «Livro diário»).
- `WorkContext`: `analysisCatalog()`, `savedAnalyses(companyId)`,
  `saveAnalysis` / `updateAnalysis` / `deleteAnalysis`, e
  `runAnalysis(companyId, { definition } | { savedId }, { from, to })`.
- **Guardadas** — lista das análises da empresa; carregar traz a definição
  para o construtor; apagar remove. Sem ligação ao SQL Server a lista fica
  vazia e o botão «Guardar» desativado (compor e correr funcionam à mesma).
- **Construtor** — origem fixa «Movimentos»; secções:
  - **Colunas** e **Agrupar por** — «chips» com caixa de seleção (agrupar só os
    campos agrupáveis); a ordem é a de seleção.
  - **Filtros** — linhas `campo · operador (conforme o tipo) · valor`.
  - **Ordenar por** — linhas `campo · ascendente/descendente`.
  - **Totais** — linhas `campo · função` (soma, contagem, média, mínimo,
    máximo) e a opção «Mostrar total geral».
  - Nome e descrição da análise; intervalo de datas com atalhos de período.
- **Correr** — chama `analyses/run` com a definição em linha e mostra o
  resultado; desativado sem colunas.
- **Resultado** — uma tabela com as colunas escolhidas, **linhas de subtotal
  intercaladas em todos os níveis de agrupamento** (ex.: subtotal da conta e
  depois subtotal do diário) e a linha de **total geral**. Aviso quando o
  resultado é truncado às 5000 linhas. «Imprimir / PDF» (`window.print()` +
  folha `@media print`) e «Exportar CSV».
- **Guardar / Guardar como nova** — cria ou actualiza a `SavedAnalysis`.

Sem alteração de esquema nem de API. Entrada no `screenTable.ts`
(`reporting/SavedAnalysis`) para o menu «Opções» abrir o dicionário na tabela
certa.

## Superfície de validação local

Abrir `/analises`, escolher colunas e agrupamentos, definir um período, correr;
guardar e voltar a carregar a análise.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e setenta e oito testes de interface (um
  novo — a rota `/analises` mostra o cabeçalho);
- `dotnet build`/`dotnet test` na solução inteira (cento e quarenta e seis
  testes .NET, sem alteração — incremento de frontend);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação no browser contra `accountingAI` (empresa Vidreira Algarvia):
  compor uma análise com colunas Diário/Conta/Nome/Débito/Crédito, agrupar por
  Diário e Conta, somar Débito e Crédito, período de 2025 → o resultado mostra
  as linhas com subtotais por conta e por diário e o total geral (6 103 141,07
  € / 6 102 891,73 €), com o aviso de truncagem às 5000; acrescentar um filtro
  `Conta começa por «62»` baixa para 1 844 linhas; «Guardar» cria a análise e
  ela aparece em «Guardadas». Linha de teste removida da base.

## Fora deste incremento

- os modelos de impressão dos relatórios fixos — F2-07D;
- a origem `SaldosDeConta` — F2-07E;
- reordenar as colunas escolhidas com o rato (por agora a ordem é a de
  seleção).
