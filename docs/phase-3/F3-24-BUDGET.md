# F3-24 — Orçamento

Épico do Bloco E da [Fase 3](README.md). A aplicação mostrava o que
aconteceu; não havia com que o comparar a não ser o exercício anterior.

## Sub-incrementos

### F3-24A — Orçamento anual por conta e desvios (feito)

- **`Domain/Accounting/BudgetLine`** — o valor orçamentado para um mês de um
  exercício, numa conta de gastos (classe 6) ou de rendimentos (classe 7) e,
  opcionalmente, num centro de custo. O valor é sempre positivo: a classe diz
  se é gasto ou rendimento. Migração aditiva `20261004084055_BudgetLines`
  (tabela nova `accounting.BudgetLines`); auditável e no dicionário de dados.
- **`BudgetService`**:

| Parte | Regra |
|---|---|
| Linha | Uma conta **ou o grupo que a agrega** (por exemplo `62`): o realizado soma todas as contas que começam por esse código. |
| Valor | Indicado por ano e **repartido por doze meses iguais**; o acerto do arredondamento fica em dezembro. Guardar de novo substitui; zero retira a linha. |
| Sobreposição | Uma conta e uma das suas subcontas não podem ter orçamento ao mesmo tempo, no mesmo âmbito — contariam o mesmo realizado duas vezes. |
| Centro de custo | Uma linha com centro de custo só conta o realizado desse centro. Quando a mesma conta também tem orçamento geral, a linha do centro é um detalhe e não entra nos totais. |
| Realizado | De janeiro até ao mês pedido, sem os lançamentos anulados e sem o apuramento de resultados. |
| Desvio | Realizado menos orçamentado, em valor e em percentagem; **favorável** quando há mais rendimento ou menos gasto do que o previsto. |
| Fora do orçamento | Os gastos e os rendimentos realizados em contas sem linha de orçamento, para se ver o que o orçamento não cobre. |

- **API:** `GET .../budget?year=&month=` e `PUT .../budget?year=&month=`
  (conta, centro de custo opcional e valor anual; devolve o relatório).
- **PWA:** ecrã **«Orçamento»** (`/orcamento`, grupo «Gestão») — exercício e
  mês, o formulário para orçamentar uma conta, os totais, o gráfico de
  orçamento contra realizado por conta, a tabela com os desvios (verde
  quando ajudam o resultado, laranja quando o prejudicam), retirar uma linha,
  CSV e impressão.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `BudgetServiceTests` — repartição por doze
  meses; grupo a somar as subcontas; linha por centro de custo; totais sem
  contar o detalhe duas vezes; realizado fora do orçamento; o exercício
  inteiro; sobreposição, conta inexistente, centro de custo inexistente e
  conta fora das classes 6 e 7 recusados; retirar uma linha.
  `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: o relatório e a recusa de um mês inválido.
- `AccountingAI.Web`: `tsc`, `eslint`, 103 testes, `vite build`.
- A suite completa: 256 testes .NET.

**Por verificar**

- O ecrã com a base real (a migração aplica-se ao reiniciar a aplicação).

**Limitações**

- **Sem sazonalidade**: os meses são iguais. A tabela já guarda um valor por
  mês, pelo que editar mês a mês é só uma questão de ecrã.
- Só gastos e rendimentos; não há orçamento de tesouraria nem de
  investimento.
- Os códigos são os do plano de contas: mudar o código de uma conta deixa a
  linha do orçamento a apontar para o código antigo.

### A seguir

- **F3-24B** — valores por mês (sazonalidade), cópia do orçamento do
  exercício anterior ou do realizado, e o orçamento no mapa de exploração.
