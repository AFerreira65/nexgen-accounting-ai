# ADR-0009 — Classificação de IVA por conta

- Estado: proposto para a F2-09
- Data: 2026-09-10

## Contexto

O apuramento de IVA da [F2-08](../phase-2/F2-08-VAT-SETTLEMENT.md) fica-se pelo
movimento das contas 24.3 (liquidado, dedutível, regularizações, posição). A
**declaração periódica oficial** da AT precisa da desagregação por **taxa**
(reduzida / intermédia / normal / isento), por **natureza da operação**
(transmissões internas, prestações de serviços, aquisições intracomunitárias,
autoliquidação, operações isentas, IVA dedutível por tipo, regularizações) e por
**região** (Continente / Açores / Madeira) — informação que o plano de contas
carrega no nome/subcódigo mas que o código não sabe ler de forma fiável.

O risco é reproduzir os campos numerados do modelo oficial sem uma base de
classificação — o mesmo risco que a F2-08 evitou. A decisão é dar primeiro a
**classificação**, e depois um **resumo por taxa e natureza** (a desagregação
que se transcreve para a declaração), deixando o mapeamento para os campos
numerados como um passo à parte, best-effort e com aviso.

## Decisão

- **`accounting.AccountVatClassification`** — uma linha opcional por conta
  (`AccountId` único, `CompanyId` para o filtro), com `Rate`
  (`Isento`/`Reduzida`/`Intermedia`/`Normal`), `Nature` (lista plana das
  naturezas comuns) e `Region` (`Continente`/`Acores`/`Madeira`). Não se toca no
  `LedgerAccount` — é metadados, à imagem de `CompanyAiSettings` /
  `VatReturnFiling`. Auditável (F2-06A), no `record-info` e no
  `DataDictionaryText`. Migração aditiva.
- Só as contas relevantes se classificam: as de **movimento** das classes 6 e 7
  (a base) e das contas **24.3** (o imposto). O `AccountVatClassificationService`
  lista essas contas com a classificação (ou vazia), permite definir por conta e
  **em lote por prefixo**.
- O **resumo por taxa e natureza** (F2-09B) soma, para um período, o movimento
  líquido das contas classificadas, agrupado por `(Nature, Rate, Region)`,
  separando base (classes 6/7) de imposto (24.3). É a desagregação que se
  transcreve para a declaração periódica.
- Antes do mapeamento aos campos numerados, a **cobertura da classificação**
  (F2-09C) — que contas relevantes (6/7/24.3) se moveram no período mas não
  estão classificadas, e o valor que ficam de fora do resumo. Sem isto, um
  mapeamento aos campos numerados poderia parecer completo estando, na
  verdade, a omitir contas por classificar — o mesmo risco de sobre-alegar
  conformidade que este ADR quer evitar.
- O **mapeamento para os campos numerados** do modelo oficial (F2-09D, opcional,
  renumerado de F2-09C) é um catálogo em código, explicitamente best-effort, com
  o aviso de que não substitui o preenchimento nem a validação oficial. **Fica
  em aberto até o utilizador confirmar a numeração exata do modelo em vigor** —
  não se deve fabricar ou assumir números de campo sem essa confirmação; o
  risco de uma declaração errada por um campo mal mapeado é real.
- **API:** `GET .../vat-classification`, `PUT .../vat-classification/{código}`,
  `POST .../vat-classification/bulk`; e (F2-09B) `GET .../vat-summary?from=&to=`.
- **PWA:** ecrã **«Configuração de IVA»** (`/config-iva`) — a grelha das contas
  com a taxa / natureza / região editáveis e a aplicação em lote por prefixo.

## Consequências

- Fica a base para a declaração periódica oficial e para o SAF-T de faturação
  (SVAT) sem inventar a numeração: a classificação é dados estruturados sobre as
  contas, não uma peça fiscal.
- Há um custo de preenchimento inicial (classificar as contas de IVA e as de
  base); a aplicação em lote por prefixo (`24331` = liquidado normal, etc.)
  reduz esse custo.
- O resumo por taxa/natureza é imediatamente útil para preencher a declaração à
  mão; o passo dos campos numerados fica isolado e claramente marcado como
  apoio.
