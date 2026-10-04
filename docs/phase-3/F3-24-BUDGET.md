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

- **Sem sazonalidade**: os meses são iguais (resolvido no F3-24B).
- Só gastos e rendimentos; não há orçamento de tesouraria nem de
  investimento.
- Os códigos são os do plano de contas: mudar o código de uma conta deixa a
  linha do orçamento a apontar para o código antigo.

### F3-24B — Valores por mês e cópia de outro exercício (feito)

Sem migração: a tabela já guardava um valor por mês.

| Parte | Regra |
|---|---|
| Valores por mês | Uma linha pode levar doze valores em vez de um valor anual; o total anual é a soma. Meses em branco ficam a zero; um total zero retira a linha. O orçamentado «até ao mês» passa a refletir a sazonalidade. |
| Cópia do orçamento | As mesmas linhas (conta, centro de custo) do exercício de origem, mês a mês. |
| Cópia do realizado | Uma linha por **grupo de dois dígitos** (62, 63, 72…) quando o grupo existe no plano de contas, senão pela própria conta; o valor de cada mês é o realizado desse mês, sem centro de custo. Um grupo com um mês de saldo contrário (nota de crédito, regularização) fica com o total do exercício repartido por igual; um grupo com total nulo ou contrário não é copiado. |
| Ajuste | Uma percentagem aplicada a todos os valores (por exemplo +3 ou -5), acima de -100 %. |
| Destino | Só um exercício **ainda sem orçamento** — a cópia nunca escreve por cima. |

- **`BudgetService`**: `SetAsync` aceita os doze valores; `CopyAsync`
  (origem `Budget` ou `Actual`); cada linha do relatório traz
  `MonthlyBudget`.
- **API:** `PUT .../budget` aceita `monthlyAmounts` (doze valores);
  `POST .../budget/copy?year=&month=` com `source` (`orcamento` ou
  `realizado`), `sourceYear` e `adjustmentPercent`; devolve o relatório.
- **PWA:** no ecrã «Orçamento», a opção **«Valores por mês»** abre os doze
  campos com o total anual; cada linha da tabela tem um botão para editar os
  meses; num exercício sem orçamento aparece **«Copiar de outro
  exercício»**; o CSV leva o orçamento de cada mês.

**Validação efetuada**

- `BudgetServiceTests`: os doze valores e o orçamentado até ao mês; doze
  valores em falta ou negativos recusados; cópia do orçamento com +10 %
  (incluindo a linha por centro de custo); recusa de um destino com
  orçamento; cópia do realizado por grupo e por conta; origem vazia, origem
  igual ao destino e ajuste de -100 % recusados.
- `AccountingAI.Api.Tests`: a recusa de uma origem desconhecida.
- `AccountingAI.Web`: `tsc`, `eslint`, 103 testes, `vite build`.
- A suite completa: 256 testes .NET.

**Por verificar**

- O ecrã com a base real — os campos por mês e a cópia não foram vistos no
  navegador.

**Limitações**

- A cópia do realizado não reparte por centro de custo.
- A cópia do orçamento não confere se as contas e os centros de custo de
  origem ainda existem.

### A seguir

- **F3-24C** — o orçamento no mapa de exploração.
