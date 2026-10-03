# F3-23 — Mapa de exploração e rácios

Segundo épico do Bloco E da [Fase 3](README.md). Assenta nas rubricas das
demonstrações financeiras ([F3-07](F3-07-OFFICIAL-STATEMENTS.md)) e no
componente de gráficos ([F3-22](F3-22-CHARTS.md)), pelo que herda as regras e
as limitações de ambos.

## Sub-incrementos

### F3-23A — Mapa de exploração mensal e rácios (feito)

**Mapa de exploração** (`FinancialStatementsService.OperatingMapAsync`) — a
demonstração de resultados por naturezas de um exercício, **mês a mês**: as
mesmas rubricas e os mesmos subtotais, uma coluna por mês e o total, sem o
apuramento. As contas de gastos ou rendimentos que não cabem em nenhuma
rubrica são listadas à parte.

**Rácios** (`FinancialStatementsService.RatiosAsync`) — calculados sobre o
balanço à data final e a demonstração de resultados do período, com o período
homólogo:

| Grupo | Rácio | Fórmula |
|---|---|---|
| Liquidez | Liquidez geral | Ativo corrente ÷ Passivo corrente |
| | Liquidez reduzida | (Ativo corrente − Inventários) ÷ Passivo corrente |
| | Liquidez imediata | Caixa e depósitos bancários ÷ Passivo corrente |
| | Fundo de maneio | Ativo corrente − Passivo corrente |
| Estrutura financeira | Autonomia financeira | Capital próprio ÷ Ativo |
| | Solvabilidade | Capital próprio ÷ Passivo |
| | Endividamento | Passivo ÷ Ativo |
| Rendibilidade | Rendibilidade do capital próprio | Resultado líquido ÷ Capital próprio |
| | Rendibilidade do ativo | Resultado operacional ÷ Ativo |
| | Margem EBITDA | Resultado antes de depreciações, gastos de financiamento e impostos ÷ Vendas e serviços prestados |
| | Margem líquida | Resultado líquido ÷ Vendas e serviços prestados |
| Atividade | Prazo médio de recebimento | Clientes ÷ Vendas e serviços prestados × dias |
| | Prazo médio de pagamento | Fornecedores ÷ (CMVMC + FSE) × dias |
| | Duração média dos inventários | Inventários ÷ CMVMC × dias |

- Um rácio com denominador zero não tem valor (aparece «—»).
- **Não há valores de referência** nem semáforos: a aplicação não afirma o que
  é «bom» sem uma fonte; a comparação setorial (dados do Banco de Portugal)
  fica para o F3-25.
- Se o balanço de base não fecha ou há contas fora das demonstrações, o ecrã
  avisa que os rácios podem não ser fiáveis.

- **API:** `GET .../management/operating-map?year=` e
  `GET .../management/ratios?from=&to=` (dentro do mesmo exercício).
- **PWA:** ecrãs **«Mapa de exploração»** (`/mapa-exploracao`) — gráfico dos
  resultados por mês e a tabela rubrica × mês — e **«Rácios»** (`/racios`) —
  valor, período homólogo e fórmula de cada rácio. Ambos no grupo «Gestão»,
  com exportação CSV.

**Validação efetuada**

- `AccountingAI.IntegrationTests` (`CashFlowAndEquityStatementsTests`): o mapa
  (valores por mês, subtotais, total igual ao resultado do exercício, o
  apuramento de fora) e os rácios (valores, período homólogo, denominador
  zero).
- `AccountingAI.Api.Tests`: os dois endpoints e a recusa de um período em dois
  exercícios.
- `AccountingAI.Web`: `tsc`, `eslint`, 94 testes, `vite build`.
- A suite completa: 236 testes .NET.

**Por verificar**

- Os dois ecrãs com uma **empresa real** — só existem com a base ligada.

**Pressupostos a confirmar com um contabilista**

- Os resultados são os do período e **não são anualizados**: num período
  inferior ao ano, as rendibilidades saem mais baixas do que as anuais.
- Os prazos médios usam saldos com IVA contra vendas e compras sem IVA (saem
  sobrestimados) e o saldo final em vez do saldo médio; as compras são
  aproximadas por CMVMC + FSE.
- A rendibilidade do capital próprio usa o capital próprio final, que já
  inclui o resultado do período.

### A seguir

- **F3-23B** — idade de saldos de clientes e fornecedores (antiguidade por
  intervalos), que precisa de casar faturas com pagamentos.
