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

- `AccountingAI.IntegrationTests`: `FiscalDossierServiceTests` — os doze
  elementos, os indícios das contas 65, 7871 e 69, o apoio e a ligação,
  guardar e repor o estado e a nota, o estado separado por exercício, as
  recusas (elemento inexistente, estado desconhecido, nota demasiado longa).
  `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: o endpoint de leitura.
- `AccountingAI.Web`: `tsc`, `eslint`, 101 testes, `vite build`.
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

### A seguir

- **F3-14B** — mapa de provisões e imparidades (modelo 30) e mapa de
  mais-valias e menos-valias (modelo 31) a partir da contabilidade e do
  cadastro de ativos; mapa de depreciações no modelo 32.
