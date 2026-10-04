# F3-10 — Modelo 22 (IRC)

Épico do Bloco B da [Fase 3](README.md). A Fase 2 deixou uma **estimativa de
IRC** (resultado líquido, ajustamentos livres, uma taxa). Faltava o apuramento
pelos campos da declaração: do resultado líquido ao lucro tributável, à
matéria coletável e ao imposto, com as derramas, as tributações autónomas e
os pagamentos por conta.

## Fonte

- **Declaração de rendimentos Modelo 22** — impresso em vigor a partir de
  janeiro de 2026 (`M22-IRC.pdf`) — e **Manual de Preenchimento do Quadro 07**
  (versão 2025, abril de 2026), da Direção de Serviços do IRC, consultados em
  2026-10-04 em
  `info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/modelos_formularios/irc/Documents/`.
- Os **100 campos do quadro 07** que o utilizador pode preencher foram
  transcritos para o recurso embebido
  `Infrastructure.SqlServer/Resources/modelo22-quadro07.json`: o número de
  cada campo vem dos cabeçalhos «Campo NNN» do manual e a designação do texto
  do impresso. Sete campos sem cabeçalho próprio no manual (706, 707, 712,
  714, 743, 752, 794) e nove designações com início ambíguo foram lidos
  diretamente do impresso.
- As **fórmulas** dos campos 708, 753, 776, 777, 778, 351, 378, 357, 358,
  361, 362, 367 e 368 são as que estão escritas no impresso.
- As **taxas** vêm das tabelas fiscais do exercício
  ([F3-04](F3-04-FISCAL-TABLES.md)): o impresso de 2026 serve o período de
  2025 e ainda mostra 20 % e 16 %; para 2026 a aplicação usa 19 % e 15 %.

## Sub-incrementos

### F3-10A — Quadros 07, 09 e 10 (feito)

- **`Domain/Accounting/IrcReturnValue`** — um valor indicado pelo utilizador
  para um exercício: um campo do impresso ou um parâmetro (`pme`,
  `derrama-municipal-taxa`). Migração aditiva `20261004073829_IrcReturnValues`
  (tabela nova `accounting.IrcReturnValues`); auditável e no dicionário de
  dados. Os campos calculados não se guardam.
- **`Modelo22Service`**:

| Quadro | Regra |
|---|---|
| 07 | Campo 701 = resultado líquido da demonstração de resultados (sem o apuramento). O utilizador indica as variações patrimoniais, os acréscimos e as deduções; a aplicação soma 708, 753 e 776 e apura o prejuízo (777) ou o lucro tributável (778). |
| 09 | Regime geral: 301/302 transportados do quadro 07; 309 (prejuízos deduzidos) e 310 (benefícios) indicados; 311 = 302 − 309 − 310, nunca negativo. |
| 10 | 347-A e 347-B pelas taxas do exercício (com `pme`, a primeira parcela à taxa reduzida); 373 derrama estadual, por escalões, sobre o lucro tributável; 378 coleta total; 357 deduções limitadas à coleta total; 358 IRC liquidado; 361/362 depois das retenções e dos pagamentos por conta; 364 derrama municipal = taxa indicada × lucro tributável; 365 tributações autónomas (indicadas); 367/368 total a pagar ou a recuperar. |

- **Sugestões da contabilidade**, nunca aplicadas sozinhas: o saldo da conta
  8121 (imposto estimado) para o campo 724, e o da 8122 (imposto diferido)
  para o 725 ou o 766. Se há imposto estimado e o campo 724 está a zero, há
  um aviso.
- **Outros avisos:** deduções do quadro 09 acima do lucro tributável;
  deduções à coleta acima da coleta total; exercício sem tabelas fiscais ou
  com taxas transportadas do último ano conhecido; contas fora da
  demonstração de resultados; derrama municipal a zero com lucro tributável.
- **Guardar** substitui os valores do exercício (o que vai a zero deixa de
  estar guardado) e recusa campos calculados, desconhecidos, repetidos ou
  negativos.
- **API:** `GET .../modelo-22?year=`, `PUT .../modelo-22?year=`,
  `GET .../modelo-22/fields` (o catálogo do quadro 07 com a fonte).
- **PWA:** ecrã **«Modelo 22»** (`/modelo-22`, grupo «Fiscal») — exercício,
  opção PME e taxa da derrama municipal; os três quadros com os campos
  calculados e os editáveis; no quadro 07 só aparecem os campos com valor, com
  uma lista para adicionar outro (ou mostrar todos); «Guardar e recalcular»,
  avisos, CSV e impressão.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `Modelo22ServiceTests` — o catálogo (100
  campos, sem repetidos, lados certos, sem campos calculados); o apuramento
  sem nada indicado (taxa geral de 2026), a sugestão do campo 724 e o seu
  aviso; com acréscimos, deduções, prejuízos, PME, derrama municipal,
  retenções, pagamentos por conta e tributações autónomas (campo a campo até
  ao total a pagar); um prejuízo fiscal; a substituição ao guardar; as
  recusas. `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: `Modelo22IsComputedFromTheLedger` (só leitura).
- `AccountingAI.Web`: `tsc`, `eslint`, 98 testes, `vite build`.
- A suite completa: 250 testes .NET.

**Por verificar**

- O ecrã com a base real (a migração aplica-se ao reiniciar a aplicação) e um
  Modelo 22 real comparado com o que foi entregue.
- Uma leitura humana do catálogo contra o impresso: a transcrição foi feita
  por extração automática do PDF.

**O que não faz (e o ecrã diz)**

- Só o regime geral de um residente: sem regimes de redução de taxa, sem as
  taxas das Regiões Autónomas (campos 350 e 370), sem imposto a outras taxas
  (349), sem grupos de sociedades, sem regime simplificado, sem anexos (A da
  derrama por município, D dos benefícios).
- **Não verifica limites legais**: o limite da dedução de prejuízos, os
  limites dos benefícios, a taxa máxima da derrama municipal.
- As tributações autónomas são um valor indicado; o seu cálculo por tipo de
  encargo (quadro 13) é um incremento próprio.
- Não gera o ficheiro da declaração nem a entrega (Bloco C).

### F3-10B — Tributações autónomas e prejuízos reportáveis (feito)

**Tributações autónomas.** Uma secção nova entre o quadro 09 e o quadro 10:
o utilizador indica os encargos do exercício por tipo (despesas não
documentadas, viaturas por escalão, despesas de representação, ajudas de
custo…) e a aplicação aplica as taxas do exercício, do artigo 88.º do CIRC
tal como estão nas tabelas fiscais ([F3-04](F3-04-FISCAL-TABLES.md)).

- Havendo encargos indicados, o **campo 365 passa a ser o total calculado**;
  um valor que lá estivesse indicado é ignorado, com aviso. Sem encargos, o
  campo 365 continua a ser indicado à mão.
- Com **prejuízo fiscal**, as taxas são agravadas em dez pontos percentuais
  (art.º 88.º, n.º 14), com um aviso para confirmar se alguma exceção se
  aplica.
- As tabelas só têm tributações autónomas a partir de 2025; antes disso a
  secção não aparece.
- Os encargos são indicados, não lidos das contas: o plano de contas não
  distingue, por exemplo, o escalão de cada viatura.

**Prejuízos fiscais reportáveis.**

| Linha do quadro 09 | Regra |
|---|---|
| Prejuízos reportáveis no início do exercício | O transportado dos exercícios anteriores: para cada um, o saldo inicial + o prejuízo do campo 777 − o deduzido no campo 309. Recua até dez exercícios, a partir do primeiro com lançamentos ou valores guardados. |
| Saldo inicial indicado (`prejuizos-iniciais`) | Substitui o transportado — para quem traz prejuízos de antes de usar a aplicação. |
| Limite da dedução | 65 % do lucro tributável — CIRC, art.º 52.º, n.º 2, na redação da Lei n.º 24-D/2022 (texto do Portal das Finanças, consultado em 2026-10-04); aplicado a partir de 2023. |
| Campo 309 | Continua a ser indicado; a aplicação **sugere** o menor entre o saldo e o limite, e avisa se o indicado exceder um ou outro. |
| Prejuízos a reportar | Saldo inicial + prejuízo do exercício − campo 309. |

**Estimativa de IRC.** O ecrã da estimativa mantém-se como conta rápida e
passa a remeter para o Modelo 22, que é onde estão a taxa das PME, as
derramas, os prejuízos e as tributações autónomas — fecha o que o F3-04B
previa.

**Validação efetuada**

- `AccountingAI.IntegrationTests` (`Modelo22ServiceTests`): um prejuízo de
  2025 transportado para 2026 e sugerido para o campo 309; dedução dentro e
  acima do saldo; saldo inicial indicado e aviso do limite de 65 %; encargos
  de tributação autónoma às taxas de 2026, a substituir o campo 365; taxas
  agravadas com prejuízo fiscal; chave de encargo desconhecida recusada.
- A suite completa: 250 testes .NET, 98 de interface.

**Pressupostos a confirmar com um contabilista**

- Os prejuízos anteriores a 2023 tinham prazo de reporte e um regime
  transitório; a aplicação trata todos os prejuízos transportados como
  reportáveis sem prazo.
- O agravamento das tributações autónomas tem exceções (por exemplo no início
  de atividade) que a aplicação não conhece.
- As reduções transitórias de taxas das tributações autónomas não estão nas
  tabelas.

### A seguir

- **F3-10C** — ficheiro e entrega pelo webservice da AT (Bloco C).
