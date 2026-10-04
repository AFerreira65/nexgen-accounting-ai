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

- `NexGenAccountingAI.IntegrationTests`: `FinancialStatementsServiceTests` — o
  catálogo (rubricas diferentes por modelo); e um exercício com abertura,
  imobilizado e depreciações, cliente devedor e cliente credor, compras,
  conta sem taxonomia, lançamento anulado e apuramento: rubricas, totais,
  comparativo, diferença explicada pelas contas de fora, resultado igual no
  balanço e na demonstração de resultados.
- `NexGenAccountingAI.Api.Tests`: `FinancialStatementsAreBuiltFromTheTaxonomies`
  (só leitura).
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 92 testes, `vite build`; o ecrã foi
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

### F3-07B — Fluxos de caixa e alterações no capital próprio (feito)

Ao contrário do balanço e da demonstração de resultados, **a AT não publica
uma tabela** que ligue as contas a estas duas demonstrações. A classificação
é um **critério da aplicação**, feito pelas contas de contrapartida de cada
lançamento segundo os códigos do plano de contas do SNC — e por isso cada
rubrica dos fluxos de caixa mostra as contas que a formam.

**Demonstração dos fluxos de caixa — método direto**
(`FinancialStatementsService.CashFlowsAsync`)

| Parte | Regra |
|---|---|
| Caixa | As contas cuja taxonomia cai em «Caixa e depósitos bancários»; sem taxonomia, as de código 11, 12 e 13. |
| Fluxo | Cada lançamento não anulado que mexe em caixa é repartido pelas suas linhas de contrapartida (crédito − débito): a soma é exatamente a variação de caixa. As transferências entre contas de caixa anulam-se; os lançamentos de abertura não são fluxos. |
| Operacionais | 21, 71, 72 → recebimentos de clientes; 22, 31, 62 → pagamentos a fornecedores; 23, 63 → pagamentos ao pessoal; 241 → imposto sobre o rendimento; **tudo o resto** → outros recebimentos / pagamentos. |
| Investimento | 43, 453, 2711, 6871, 7871 → ativos fixos tangíveis; 44, 454 → intangíveis; 41, 451 → investimentos financeiros; 42, 452, 455, 46 → outros ativos; 593 → subsídios ao investimento; 79 → juros; 792 → dividendos. O sinal decide entre pagamentos e recebimentos. |
| Financiamento | 25 → financiamentos obtidos; 51–54, 261, 262 → realizações (ou reduções) de capital; 69 → juros e gastos similares; 264, 265 → dividendos; 263, 266, 268 → outras operações. |
| IVA | A linha de IVA (243) de uma compra ou venda a pronto segue a rubrica da maior linha do lançamento; um pagamento de IVA ao Estado fica em «outros». |
| Caixa no fim | O saldo das contas de caixa à data — o mesmo do balanço; o do início é o do fim menos a variação. |

**Demonstração das alterações no capital próprio**
(`FinancialStatementsService.EquityChangesAsync`) — uma coluna por rubrica do
capital próprio do balanço, e as linhas:

| Linha | O que entra |
|---|---|
| Posição no início | Lançamentos de abertura e, num período que não começa em janeiro, tudo o que aconteceu antes. |
| Aplicação de resultados e transferências | Lançamentos só entre contas de capital próprio (total zero). Numa aplicação com dividendos, o que vai para reservas e transitados fica aqui. |
| Outras alterações reconhecidas | Movimentos de reservas, excedentes e ajustamentos contra outras contas. |
| Resultado líquido do período | Calculado como na demonstração de resultados (o apuramento não conta duas vezes). |
| Resultado integral | As duas anteriores. |
| Realizações de capital e de prémios | Aumentos em capital, outros instrumentos e prémios de emissão. |
| Distribuições | O que sai do capital próprio em lançamentos com as contas 264 ou 265, e os dividendos antecipados. |
| Outras operações com detentores de capital | Reduções de capital e ações próprias. |
| Posição no fim | A soma — **igual ao capital próprio do balanço** à mesma data. |

- **API:** `GET .../statements/cash-flows?from=&to=` e
  `GET .../statements/equity-changes?from=&to=` (dentro do mesmo exercício).
- **PWA:** o ecrã «Demonstrações financeiras» ganha os separadores **Fluxos
  de caixa** (cada rubrica abre as contas que a formam) e **Alterações no
  capital próprio**.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `CashFlowAndEquityStatementsTests` — venda
  a pronto com IVA, pagamento a fornecedor, empréstimo, compra de equipamento,
  juros, dividendos, aumento de capital, transferência entre caixa e banco,
  aplicação de resultados e apuramento: rubricas, comparativo, caixa inicial e
  final; a posição final do capital próprio e a caixa final batem com o
  balanço, que fecha; período a começar a meio do ano.
- `NexGenAccountingAI.Api.Tests`: os dois endpoints e a recusa de um período em dois
  exercícios (só leitura).
- A suite completa: 236 testes .NET, 92 de interface.

**Por verificar e pressupostos a confirmar com um contabilista**

- Tudo com uma **empresa real**; em particular quanto fica em «Outros
  recebimentos / pagamentos», que recebe tudo o que não tem regra.
- As retenções e contribuições (242, 245) ficam em «outros», não em
  «pagamentos ao pessoal».
- Os fornecedores de investimentos (2711) contam todos como ativos fixos
  tangíveis.
- Os descobertos bancários contam como caixa negativa; não há linha de
  diferenças de câmbio.
- A demonstração das alterações no capital próprio é apresentada num só
  período (sem o quadro do período anterior).

### A seguir

- **F3-07C** — demonstração de resultados por funções e anexo.
