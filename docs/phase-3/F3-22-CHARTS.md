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

### A seguir

- **F3-22B** — gráficos na visão geral e nos relatórios de gestão; gráficos de
  estrutura (gastos por natureza, maiores clientes e fornecedores).
