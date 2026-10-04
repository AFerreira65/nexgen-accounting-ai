# F3-22 — Gráficos

Primeiro épico do Bloco E da [Fase 3](README.md), feito em paralelo com o
Bloco B por ser independente e visível. A aplicação não tinha nenhum gráfico:
os mapas e os relatórios de gestão eram só tabelas e texto.

## Sub-incrementos

### F3-22A — Componente de gráficos e evolução mensal (feito)

- **`app/charts.tsx`** — componente `Chart` partilhado, em SVG e **sem
  dependências novas**: barras agrupadas, linhas e áreas no mesmo eixo, escala
  com divisões «redondas», valores negativos, linhas interrompidas onde não há
  valor, legenda, tema escuro. Cada ponto tem um `<title>` com o valor (aparece
  ao passar o rato e é lido pelos leitores de ecrã) e o gráfico tem
  `role="img"` com o título.
- **`FinancialStatementsService.MonthlyEvolutionAsync`** — a evolução de um
  exercício, só de leitura:

| Série | Regra |
|---|---|
| Rendimentos e gastos do mês | Contas de rendimentos e de gastos pela taxonomia (sem taxonomia, classes 7 e 6), **sem o apuramento** — o marcado pela aplicação e o importado de um SAF-T (contas 811 ou 818). |
| Exercício anterior | Os mesmos valores um ano antes, mês a mês. |
| Tesouraria | Saldo de caixa e bancos no fim de cada mês, contando com a abertura. |
| Clientes / fornecedores | Saldo das contas que a taxonomia põe em «Clientes» / «Fornecedores» (sem taxonomia, 21 / 22) no fim de cada mês. |
| Último mês | O último mês com movimentos: depois dele não se desenha nada. |

- **API:** `GET .../management/monthly-evolution?year=`.
- **PWA:** ecrã **«Evolução mensal»** (`/evolucao`, grupo «Gestão») — quatro
  indicadores com a variação face ao período homólogo e quatro gráficos:
  rendimentos, gastos e resultado; resultado acumulado contra o exercício
  anterior; tesouraria; clientes e fornecedores. Por baixo, a tabela com os
  valores e a exportação CSV. Em modo de demonstração mostra **dados de
  exemplo**, identificados como tal.

**Validação efetuada**

- `AccountingAI.IntegrationTests` (`CashFlowAndEquityStatementsTests`):
  movimentos do mês sem o apuramento, saldos no fim do mês, exercício anterior,
  soma dos resultados mensais igual ao resultado do exercício.
- `AccountingAI.Api.Tests`: o endpoint e a recusa de um exercício inválido.
- `AccountingAI.Web`: `tsc`, `eslint`, 93 testes (o ecrã desenha os gráficos
  em modo de demonstração), `vite build`; os gráficos foram vistos no
  navegador.
- A suite completa: 236 testes .NET.

**Por verificar**

- Os gráficos com uma **empresa real** e o tempo de resposta com dezenas de
  milhares de movimentos (três consultas agregadas por conta e mês).
- O saldo de clientes e de fornecedores é o líquido das contas — um
  adiantamento abate ao saldo, ao contrário do balanço.

### F3-22B — Gráficos de estrutura e gráfico na visão geral (feito)

- **`BarList`** (`app/charts.tsx`) — lista de barras horizontais para
  comparar parcelas com nomes compridos (rubricas, terceiros), com o valor e
  o peso de cada uma.
- **`FinancialStatementsService.StructureAsync`** — a estrutura de um
  exercício, só de leitura:

| Estrutura | Regra |
|---|---|
| Gastos por natureza | As rubricas da demonstração de resultados com valor negativo, do maior para o menor, com o peso no total. |
| Rendimentos por natureza | As rubricas com valor positivo. |
| Maiores clientes | Os dez terceiros com mais débitos nas contas 21 no exercício (faturação lançada, **com IVA**), sem a abertura. |
| Maiores fornecedores | Os dez com mais créditos nas contas 22 (compras lançadas, com IVA). |

- **API:** `GET .../management/structure?year=`.
- **PWA:** o ecrã «Evolução mensal» ganha quatro painéis de estrutura (em
  modo de demonstração, com dados de exemplo); a **«Visão geral»** passa a
  mostrar o gráfico de rendimentos, gastos e resultado do exercício, com
  ligação à evolução mensal — só com a base ligada e só se houver movimentos.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: a estrutura dos gastos e dos rendimentos
  (ordem, valores e pesos) no cenário das demonstrações.
- `AccountingAI.Api.Tests`: o endpoint.
- `AccountingAI.Web`: `tsc`, `eslint`, 102 testes (as listas de barras em
  modo de demonstração), `vite build`; os painéis foram vistos no navegador
  com os dados de exemplo.
- A suite completa: 254 testes .NET.

**Por verificar**

- Tudo com uma **empresa real**, incluindo o gráfico da visão geral, que não
  existe em modo de demonstração.
- Os maiores clientes e fornecedores só veem lançamentos com terceiro; uma
  nota de crédito não abate à faturação do cliente.

### A seguir

- **F3-22C** — gráficos nos relatórios de gestão gerados com IA (F2-05).
