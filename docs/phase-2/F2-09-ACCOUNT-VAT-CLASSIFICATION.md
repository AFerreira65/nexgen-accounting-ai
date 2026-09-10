# F2-09 — Configuração de IVA por conta e resumo para a declaração

Épico da [Fase 2](README.md), continuação do ponto 3 da visão. Dá à
contabilidade a **classificação de IVA por conta** (taxa, natureza da operação,
região) e o **resumo por taxa e natureza** que se transcreve para a declaração
periódica — a peça que a [F2-08](F2-08-VAT-SETTLEMENT.md) deixou em aberto por
precisar desta configuração. A decisão está na
[`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md).

## Contexto

- O [apuramento de IVA](F2-08-VAT-SETTLEMENT.md) (F2-08) dá a posição do período
  a partir das contas 24.3, mas não a desagregação por taxa/natureza/região que
  a declaração periódica exige.
- O plano de contas carrega essa informação no nome/subcódigo (ex.:
  `24331131 IVA - Liquidado op. gerais bens serv. NAC tx. nor. C`), mas o código
  não a lê de forma fiável.
- A declaração periódica **oficial** (campos numerados) não se reproduz sem esta
  base; F2-09C mapeia-a best-effort, com aviso.

## Objetivo

- **Classificar** cada conta relevante (movimento das classes 6 e 7, e as contas
  24.3) com `Rate` (isento / reduzida / intermédia / normal), `Nature` (natureza
  da operação) e `Region` (Continente / Açores / Madeira) — por conta e em lote
  por prefixo.
- **Resumir** um período por `(Nature, Rate, Region)`, separando base (classes
  6/7) de imposto (24.3) — a desagregação que se preenche na declaração.
- **Mapear** (opcional) esse resumo para os campos numerados do modelo oficial,
  com aviso.

Fora do âmbito: a submissão da declaração à AT (F2-02C+, bloqueado); a
reprodução exaustiva de todos os campos e regras do modelo.

## Desenho

### Domínio / Application

```
Domain/Accounting/
  AccountVatClassification.cs   // Rate + Nature + Region por conta
  VatRate.cs / VatOperationNature.cs / VatRegion.cs   // enums
```

### Infrastructure.SqlServer

- `accounting.AccountVatClassification` (`AccountId` único, `CompanyId`, FK a
  `Accounts`), auditável (F2-06A). Migração aditiva.
- `AccountVatClassificationService` — `ListAsync` (contas relevantes + a
  classificação, ou vazia), `SetAsync` (por conta; apaga se ficar tudo
  «nenhuma»), `BulkSetAsync` (por prefixo, sobre as contas relevantes).
- (F2-09B) `VatSummaryService` — soma o movimento líquido das contas
  classificadas do período, agrupado por `(Nature, Rate, Region)`, base vs.
  imposto.

### API

- `GET .../companies/{companyId}/vat-classification` ·
  `PUT .../vat-classification/{código}` ·
  `POST .../vat-classification/bulk`.
- (F2-09B) `GET .../companies/{companyId}/vat-summary?from=&to=`.

### PWA

- Ecrã **«Configuração de IVA»** (`/config-iva`) — a grelha das contas com
  `<select>` de taxa / natureza / região por linha e a aplicação em lote por
  prefixo.
- (F2-09B) um painel de resumo por taxa/natureza em `/apuramento-iva` ou no
  próprio ecrã.

## Sub-incrementos

### F2-09A — Classificação de IVA por conta

- **Regras:** `AccountVatClassification` + enums; tabela + migração;
  `AccountVatClassificationService` (listar / definir / lote); os três
  *endpoints*; ecrã `/config-iva`. Auditável, no `record-info` e no
  `DataDictionaryText`.
- **Validação:** teste do serviço na integração (definir, lote por prefixo,
  apagar); teste de API de leitura; teste de interface (grelha + gate de
  demonstração).

### F2-09B — Resumo por taxa e natureza

- **Regras:** `VatSummaryService` + `GET .../vat-summary?from=&to=`; painel na
  PWA.
- **Validação:** teste do serviço (empresa construída, contas classificadas,
  agrupamento correto); teste de API; teste de interface.

### F2-09C — (opcional) Mapeamento para os campos numerados

- **Regras:** catálogo `(Nature, Rate, Region) → campo` em código, best-effort,
  com aviso; o resumo passa a mostrar o campo previsto.
- **Validação:** teste do catálogo; nota no ecrã de que é um apoio.

## Decisões em aberto

- **Lista de naturezas** — começar pelas comuns (transmissões internas,
  prestações de serviços, exportações/isentas com dedução, aquisições
  intracomunitárias de bens e de serviços, autoliquidação, isentas sem dedução,
  IVA dedutível imobilizado / existências / outros, regularizações a favor do
  sujeito passivo / do Estado); afinar com uso.
- **Contas relevantes** — movimento das classes 6 e 7 e contas `243*`. Rever se
  faltam casos (ex.: `2434` já cobre regularizações).
- **Campos numerados** — F2-09C, sempre com aviso.

## Referências

- [`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md).
- F2-08 (apuramento de IVA), F1-61 (resumo de IVA), F2-06A (auditoria).
