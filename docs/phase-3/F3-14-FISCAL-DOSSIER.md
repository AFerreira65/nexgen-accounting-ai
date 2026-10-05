# F3-14 — Dossier fiscal

Último épico das saídas sem a AT do Bloco B da [Fase 3](README.md). O
processo de documentação fiscal (artigo 130.º do CIRC) tem de estar
constituído para cada exercício e ser apresentado quando a inspeção o pedir.
A aplicação não tinha nada que dissesse o que o compõe nem em que estado
está.

## Fonte

**Portaria n.º 92-A/2011, de 28 de fevereiro, anexo I, na redação da Portaria
n.º 51/2018, de 16 de fevereiro** (períodos de tributação iniciados em ou
após 1 de janeiro de 2017): os doze elementos aplicáveis ao IRC. O texto foi
lido numa reprodução (`informador.pt`, consultada em 2026-10-04) — o Diário
da República não devolveu conteúdo — e está **por conferir na publicação
oficial**.

## Sub-incrementos

### F3-14A — Índice do dossier com o estado por exercício (feito)

- **`Domain/Accounting/FiscalDossierItem`** — o estado (por preparar, pronto,
  não aplicável) e uma nota de cada elemento, por empresa e exercício.
  Migração aditiva `20261004080629_FiscalDossierItems` (tabela nova
  `accounting.FiscalDossierItems`); auditável e no dicionário de dados.
- **`FiscalDossierService`** — para cada um dos doze elementos:

| | |
|---|---|
| Título | O texto da portaria. |
| Apoio da aplicação | «Mapa de apoio na aplicação (não é o modelo oficial)», com a ligação ao ecrã, ou «preparado fora da aplicação». |
| O que a contabilidade mostra | Um indício do exercício, sem o apuramento: perdas por imparidade, provisões e reversões (contas 65, 67 e 76) para o elemento 3; mais e menos-valias (7871 e 6871) para o 4; ativos e depreciações calculadas e lançadas para o 5; resultado, lucro tributável e prejuízo para o 8; prejuízos reportáveis, deduzidos e a reportar para o 10; gastos de financiamento líquidos (69 menos 79) para o 11. |
| Estado e nota | Os que o utilizador guardou; por omissão, «por preparar». |

- **Ligações:** elemento 2 → declaração de IVA (campo 40); 5 → mapa de
  depreciações ([F3-12](F3-12-FIXED-ASSETS.md)); 8 e 10 → Modelo 22
  ([F3-10](F3-10-MODELO-22.md)).
- **API:** `GET .../fiscal-dossier?year=` e
  `PUT .../fiscal-dossier/{element}?year=` (estado e nota).
- **PWA:** ecrã **«Dossier fiscal»** (`/dossier-fiscal`, grupo «Fiscal») — a
  lista dos elementos com o indício da contabilidade, a ligação ao ecrã de
  apoio, a nota e os botões de estado; contagem do que falta; impressão.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `FiscalDossierServiceTests` — os doze
  elementos, os indícios das contas 65, 7871 e 69, o apoio e a ligação,
  guardar e repor o estado e a nota, o estado separado por exercício, as
  recusas (elemento inexistente, estado desconhecido, nota demasiado longa).
  `SqlServerMigrationTests` inclui a migração.
- `NexGenAccountingAI.Api.Tests`: o endpoint de leitura.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 101 testes, `vite build`.
- A suite completa: 252 testes .NET.

**Por verificar**

- O ecrã com a base real (a migração aplica-se ao reiniciar a aplicação).
- A lista dos elementos contra o Diário da República.

**O que não faz**

- **Não gera os mapas de modelo oficial** (modelos 30, 31 e 32, nem os das
  reavaliações): diz onde estão os dados e deixa marcar o estado.
- Não arquiva documentos — o arquivo digital é o F3-20.
- Os indícios usam os códigos do SNC (65, 67, 69, 76, 79, 6871, 7871); um
  plano de contas que se afaste deles dá indícios errados.
- Não trata os elementos próprios do IRS nem o prazo de constituição.

### F3-14B — Modelo 32 e Modelo 31 (feito)

**Fontes.** Ambas as portarias foram lidas no Diário da República a
2026-10-05:

- **Modelo 32:** Portaria n.º 94/2013, de 4 de março (DR n.º 44, 1.ª série).
  O anexo I traz o impresso e as instruções das 16 colunas; o anexo II, a
  estrutura de dados.
- **Modelo 31:** Portaria n.º 92-A/2011, de 28 de fevereiro (DR n.º 41, 1.ª
  série, suplemento). Traz o impresso e as instruções das 13 colunas.

**Cadastro dos ativos.** Migração aditiva `20261005085547_AssetTaxData`:

- **Código e taxa máxima** das tabelas do DR n.º 25/2009, indicados pelo
  utilizador, porque as tabelas não estão na aplicação.
- **Preço de venda**, guardado ao lançar a alienação (F3-12C).
- **Coeficiente de desvalorização da moeda** (art. 47.º do CIRC), indicado à
  mão, porque a portaria anual também não está na aplicação.

**`FixedAsset.FiscalLimitFor` / `FiscalAcceptedAtEndOf`:**

- O limite fiscal de cada ano é a taxa máxima, ou sem ela a da contabilidade,
  sobre o valor de aquisição (coluna 6), com a mesma regra de duodécimos.
- O aceite de cada ano é o contabilizado até esse limite.
- A recuperação do art. 20.º do DR n.º 25/2009 não é calculada.

**`DossierMapsService`** monta os dois mapas do exercício:

| Mapa | Como se preenche |
|---|---|
| Modelo 32 | Um mapa por natureza, tirada da conta do ativo: 43 tangíveis, 44 intangíveis, 42 propriedades de investimento, 37 biológicos. O método é sempre o das quotas constantes. (1) código; (2) descrição; (3) mês, só com duodécimos; (4) ano; (5) e (6) valor de aquisição; (8) depreciação calculada do exercício; (9) aceite antes; (10) taxa; (12) limite; (15) = (8) − [(12) + (13)]. Os totalmente depreciados ficam de fora (alínea a) das instruções) e são contados. |
| Modelo 31 | As alienações e os abates lançados pela aplicação no exercício. (8) = (2) − [(5) − (6)]; (13) = (2) − [((3) − (9) − (10)) × (11)], com o coeficiente indicado ou 1. |

**Avisos do mapa:**

- falta de taxa fiscal (o limite fica igual ao gasto) ou de código;
- conta do ativo de natureza desconhecida;
- coluna 8 diferente dos gastos lançados nas contas 64;
- falta de coeficiente;
- alienação sem preço conhecido.

**O que não se calcula:** a coluna (7), as perdas por imparidade (13), as
taxas perdidas (14), a recuperação (16), as quotas decrescentes, a mais-valia
não tributada (9) e as partes de capital.

**API:**

- `GET .../fiscal-dossier/maps?year=`;
- `PUT .../fixed-assets/{código}/monetary-coefficient`;
- o coeficiente pode também ir no pedido da alienação.

**PWA:**

- **Ecrã «Mapas do dossier fiscal»** (`/mapas-dossier`, grupo «Fiscal»),
  ligado a partir do dossier fiscal: os totais (incluindo o «não aceite» a
  acrescer no quadro 07), um quadro por natureza com as colunas numeradas, o
  coeficiente editável no Modelo 31, os avisos, as fontes, CSV e impressão.
- **Ficha do ativo:** ganhou o código e a taxa máxima fiscal.
- **Painel da alienação:** ganhou o coeficiente.

**Validação:** `DossierMapsServiceTests`, em PostgreSQL, com valores feitos à
mão:

- taxa contabilística de 25 % contra fiscal de 20 %: 2 500 contabilizados,
  2 000 de limite, 500 não aceites e 2 000 aceites no ano anterior;
- alienação em julho com duodécimos;
- intangível sem taxa fiscal, com aviso;
- ativo totalmente depreciado omitido;
- aviso das contas 64;
- Modelo 31: mais-valia contabilística de 1 500 e fiscal de 900, com
  coeficiente de 1,05;
- código fiscal inválido.

A API devolve os mapas.

### F3-14C — Modelo 30 (feito)

**Fonte:** Portaria n.º 92-A/2011, impresso e instruções das 10 colunas. A
coluna (10) é igual a (2) − (5) − (6) − (7) − (8) + (9).

- **Contas:** as de perdas por imparidade acumuladas em créditos (219, 229,
  239, 269, 279) e em inventários (329 a 369), e as provisões (29). As contas
  e as reversões (76) estão confirmadas na Taxonomia S.
- **Da contabilidade:**
  - **(2) saldo inicial:** o anterior ao exercício, mais a abertura do
    próprio exercício;
  - **(9) constituição ou reforço:** os créditos do exercício;
  - **débitos do exercício:** contam como **reversão** quando o lançamento
    tem uma conta 76, e como **utilização** nos outros casos.
- **À mão** (**`ProvisionMapInput`**, auditável; migração
  `20261005090336_ProvisionMapInputs`):
  - **Partes tributadas:** a contabilidade não as separa. Indicam-se (3),
    (5) e (7); (4), (6) e (8) são as diferenças.
  - **Grupo:** as imparidades vão para o grupo I. As provisões ficam no II
    (não dedutíveis) por omissão; classificá-las como III (dedutíveis, arts.
    39.º e 40.º do CIRC) é decisão do utilizador.
  - **Validações:** uma parte tributada maior do que o valor da
    contabilidade gera aviso; uma conta fora destas é recusada.
- **Não se calculam** as informações adicionais do impresso: os créditos de
  cobrança duvidosa e o limite dos ajustamentos em inventários.
- **API:** `GET .../fiscal-dossier/modelo30?year=` e
  `PUT .../fiscal-dossier/modelo30/{conta}?year=` (`{ group, taxedOpening, taxedUsed, taxedReversed }`).
- **PWA:** a secção «Modelo 30» no ecrã dos mapas (agora «Mapas 30 a 32»),
  com as partes tributadas e o grupo editáveis por conta.

**Validação:** `ProvisionMapServiceTests`, em PostgreSQL, cobre:

- o saldo da abertura;
- o reforço, a reversão e a utilização;
- o saldo final de 1 000;
- a provisão no grupo II com aviso, e depois no III;
- as colunas (4) e (6) com as partes tributadas indicadas;
- a recusa de uma conta fora do mapa, de uma provisão no grupo I e de um
  valor negativo.

### A seguir

- O ficheiro XML «Mapas do dossier fiscal», cujo esquema
  `Mapasdodossierfiscal.xsd` é publicado pela AT e não está no projeto.
