# F3-12 — Ativos fixos e depreciações

Épico do Bloco B da [Fase 3](README.md). A aplicação não tinha cadastro de
ativos: as depreciações só existiam como lançamentos. Sem cadastro não há mapa
de depreciações, nem base para o quadro 07 do Modelo 22 ou para o dossier
fiscal.

## Sub-incrementos

### F3-12A — Cadastro e mapa de depreciações (feito)

- **`Domain/Accounting/FixedAsset`** — código, descrição, data de aquisição,
  data de entrada em funcionamento, valor de aquisição, valor residual, taxa
  anual, opção por duodécimos, contas (ativo, depreciações acumuladas, gasto),
  data de alienação ou abate, estado. O cálculo está no domínio
  (`DepreciationFor`, `AccumulatedAtEndOf`):

| Regra | |
|---|---|
| Método | Quotas constantes: taxa anual × (valor de aquisição − valor residual). |
| Início | Conta a partir do ano de entrada em funcionamento. |
| Sem duodécimos | O ano de início leva a quota anual inteira; o ano da alienação não leva nenhuma. |
| Com duodécimos | No ano de início contam os meses desde o de entrada em funcionamento; no da alienação, os meses até ao anterior. |
| Limite | As acumuladas nunca passam o valor depreciável; o resto do arredondamento cai no último ano. |

- **A taxa é indicada pelo utilizador.** As tabelas do Decreto Regulamentar
  n.º 25/2009 não estão na aplicação: transcrevê-las é um incremento próprio,
  com fonte.
- Migração aditiva `20261004070149_FixedAssets` (tabela nova
  `accounting.FixedAssets`); auditável, no `record-info` (`ativos-fixos`) e no
  dicionário de dados.
- **`FixedAssetService`** — listar, criar, alterar (as contas têm de existir e
  ser de movimento) e o **mapa de depreciações** de um exercício: por ativo, o
  valor de aquisição, as acumuladas no início, a depreciação do exercício, as
  acumuladas no fim e o valor líquido. Entram os ativos ativos que já tinham
  entrado em funcionamento e não foram alienados em exercícios anteriores.
- **Conferência com a contabilidade:** o mapa compara o total calculado com os
  gastos de depreciação lançados no exercício (contas 64, sem o apuramento) e
  mostra a diferença.
- **API:** `GET/POST .../fixed-assets`, `PUT .../fixed-assets/{code}`,
  `GET .../fixed-assets/schedule?year=`.
- **PWA:** ecrãs **«Ativos fixos»** (`/ativos-fixos`, grupo «Cadastros») —
  lista e editor — e **«Mapa de depreciações»** (`/mapa-depreciacoes`, grupo
  «Mapas») — o mapa do exercício, o aviso de diferença para a contabilidade,
  CSV e impressão.

**Validação efetuada**

- `NexGenAccountingAI.Domain.Tests`: `FixedAssetTests` — quota inteira no primeiro
  ano, duodécimos, valor residual e arredondamento, alienação nos dois
  regimes, dados inválidos.
- `NexGenAccountingAI.IntegrationTests`: `FixedAssetServiceTests` — criar, código
  repetido, conta agregadora ou inexistente recusada, lista com e sem
  inativos, mapa de 2026 (só os ativos em funcionamento), diferença para os
  gastos lançados, efeito da alienação. `SqlServerMigrationTests` inclui a
  migração.
- `NexGenAccountingAI.Api.Tests`: `FixedAssetsAreListedAndScheduled` (só leitura).
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 97 testes, `vite build`.
- A suite completa: 247 testes .NET.

**Por verificar**

- Os dois ecrãs com a base real ligada (a migração aplica-se ao reiniciar a
  aplicação) e um mapa real comparado com o do programa de origem.

**Pressupostos a confirmar com um contabilista**

- As regras dos duodécimos e do ano da alienação seguem o artigo 7.º do
  Decreto Regulamentar n.º 25/2009 tal como as conheço; **não as conferi no
  texto**.
- Só quotas constantes: sem quotas decrescentes, sem reavaliações, sem
  imparidades e sem a separação das depreciações não aceites fiscalmente (o
  mapa não é o modelo 32).
- Um ativo comprado antes de a empresa usar a aplicação é registado com as
  datas e o valor originais: as acumuladas no início resultam do cálculo, não
  do que foi efetivamente lançado.

### F3-12B — Lançamento das depreciações do exercício (feito)

- **`FixedAssetService.PostDepreciationAsync`** — gera um lançamento a 31 de
  dezembro com o gasto a débito e as depreciações acumuladas a crédito,
  **somados por conta**, no diário indicado. Fica **por conferir**: é o
  contabilista que o confirma nos movimentos.
- **Recusas**, para não lançar o que não deve:

| Situação | Mensagem |
|---|---|
| O exercício já foi lançado por aqui | Reconhece-se pela referência de documento «Depreciações AAAA» num lançamento não anulado; para gerar de novo, anula-se esse. |
| Sem depreciações no exercício | Nada a lançar. |
| Um ativo sem a conta do gasto ou a das acumuladas | Lista os códigos em falta. |
| Já há gastos de depreciação nas contas 64 | Só avança com confirmação explícita (`allowExisting`), para não duplicar o que veio de um SAF-T ou foi lançado à mão. |
| Período fechado, diário inexistente ou inativo | As regras gerais dos lançamentos. |

- **API:** `POST .../fixed-assets/schedule/post?year=&journal=&allowExisting=`.
- **PWA:** no «Mapa de depreciações», o painel «Lançar as depreciações» pede
  o diário; quando já há gastos lançados, o botão diz quanto e pede a
  confirmação.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`FixedAssetServiceTests`): recusa com
  gastos já lançados sem confirmação e com um ativo sem contas; lançamento
  com as duas depreciações somadas por conta, por conferir; segunda tentativa
  recusada com a referência do primeiro; o mapa passa a ver o gasto lançado.
- A suite completa: 247 testes .NET, 97 de interface.
- **Por verificar em direto:** o painel de lançamento, com a base real — é
  uma operação de escrita, pelo que não tem teste de API (os testes de API
  correm contra a base real e são só de leitura).

**Limitações**

- Um lançamento por exercício, a 31 de dezembro; não há depreciações mensais.
- Não lança só a diferença: ou o exercício inteiro, ou nada.

### A seguir

- **F3-12C** — alienação e abate com a mais ou menos-valia; mapa fiscal
  (modelo 32) e tabelas do Decreto Regulamentar n.º 25/2009.
