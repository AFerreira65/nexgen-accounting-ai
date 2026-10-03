# F3-07 — Demonstrações financeiras oficiais

Segundo épico do Bloco B da [Fase 3](README.md). A aplicação já tinha um
balanço e uma demonstração de resultados de gestão (por classes de contas);
faltavam as demonstrações nos **modelos do SNC**, que são as que se entregam e
que alimentam a IES.

## Fonte

A correspondência entre contas e rubricas não é inventada: vem do ficheiro da
AT **«Saldos e Demonstrações Financeiras por Taxonomia»**
(`SVAT_Saldos_Demonst_Fin_Taxonomia.xlsx`, versão de janeiro de 2021,
consultada em 2026-10-03), que diz, para cada código de taxonomia, a classe
(ativo corrente, passivo não corrente, capital, gasto, rendimento…), o saldo
esperado e a rubrica em que entra o saldo devedor e o saldo credor — em cada
um dos modelos: SNC base, base ajustado às pequenas entidades, base ajustado
às microentidades e taxonomia das microentidades.

A tabela foi transcrita para o recurso embebido
`Infrastructure.SqlServer/Resources/taxonomy-statement-lines.json` (647
taxonomias do referencial `S`, 334 do `M`).

## Sub-incrementos

### F3-07A — Balanço e demonstração de resultados por naturezas (feito)

- **`StatementLineCatalog`** — lê o recurso e devolve, para uma taxonomia e um
  modelo, a classe e as rubricas de débito e de crédito.
- **`FinancialStatementsService`** — só lê; exclui os lançamentos anulados.

| Parte | Regra |
|---|---|
| Modelo | Pela norma contabilística da empresa: SNC geral → base; pequenas entidades → modelo reduzido; microentidades → modelo das microentidades (a taxonomia `M` das contas tem a sua própria tabela). |
| Balanço — saldos | Movimentos do exercício até à data (de 1 de janeiro à data), contando com o lançamento de abertura para os saldos anteriores. |
| Balanço — rubrica | Saldo devedor → rubrica do campo débito (ativo); saldo credor → rubrica do campo crédito (passivo ou capital próprio). Um cliente com saldo credor passa para «Adiantamentos de clientes», um banco a descoberto para «Financiamentos obtidos», **conta a conta**. Quando a taxonomia só tem uma rubrica, a conta entra nela com o seu sinal (as depreciações acumuladas abatem ao ativo). |
| Resultado no balanço | O saldo das contas de gastos, de rendimentos e de resultados — dá o mesmo antes e depois do apuramento. |
| Demonstração de resultados | Movimentos do período **sem o apuramento**: os lançamentos marcados como apuramento e, nos importados de um SAF-T, os que movimentam as contas 811 ou 818. Rendimentos positivos, gastos negativos. |
| Subtotais | Resultado antes de depreciações, gastos de financiamento e impostos; resultado operacional; resultado antes de impostos; resultado líquido. |
| Comparativo | O mesmo cálculo um ano antes (data ou período). |
| Contas de fora | As contas com saldo sem taxonomia, ou com uma taxonomia que a AT não representa no balanço (ex.: compras por transferir para inventários), são **listadas com o motivo**, e o balanço mostra a diferença entre o ativo e o capital próprio mais o passivo. |

- **API:** `GET .../statements/balance-sheet?date=` e
  `GET .../statements/income-statement?from=&to=`.
- **PWA:** ecrã **«Demonstrações financeiras»** (`/demonstracoes`, grupo
  «Mapas») — separadores Balanço e Demonstração de resultados, período com
  atalhos, coluna do período anterior, aviso quando o balanço não fecha, lista
  das contas de fora, exportação CSV e impressão.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `FinancialStatementsServiceTests` — o
  catálogo (rubricas diferentes por modelo); e um exercício com abertura,
  imobilizado e depreciações, cliente devedor e cliente credor, compras,
  conta sem taxonomia, lançamento anulado e apuramento: rubricas, totais,
  comparativo, diferença explicada pelas contas de fora, resultado igual no
  balanço e na demonstração de resultados.
- `AccountingAI.Api.Tests`: `FinancialStatementsAreBuiltFromTheTaxonomies`
  (só leitura).
- `AccountingAI.Web`: `tsc`, `eslint`, 92 testes, `vite build`; o ecrã foi
  aberto no navegador em modo de demonstração.
- A suite completa: 235 testes .NET.

**Por verificar**

- As demonstrações de uma **empresa real**, comparadas com as do programa de
  origem — o ecrã só existe com a base real ligada.
- A **ordem das rubricas** dentro de cada secção segue os modelos da Portaria
  n.º 220/2015 mas foi escrita de memória: conferir com os modelos publicados.
  Os nomes das rubricas são os da tabela da AT.

**Pressupostos a confirmar com um contabilista**

- A separação entre ativo e passivo faz-se pelo saldo de **cada conta de
  movimento** (não pelo saldo de cada terceiro dentro de uma conta coletiva).
- A distinção corrente / não corrente é a da taxonomia; não há reclassificação
  por prazo de vencimento.
- Se o resultado do exercício anterior ainda estiver na conta 818 depois da
  abertura, soma ao «Resultado líquido do período» até ser transferido para
  resultados transitados.
- Os modelos das entidades do setor não lucrativo e das IFRS não estão
  cobertos: usam o modelo geral.

### A seguir

- **F3-07B** — demonstração dos fluxos de caixa e das alterações no capital
  próprio.
- **F3-07C** — demonstração de resultados por funções e anexo.
