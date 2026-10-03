# F3-03 — Códigos de IVA e lançamentos-tipo

Terceiro épico do Bloco A da [Fase 3](README.md). A decisão e as
consequências estão na
[`ADR-0012`](../architecture-decisions/ADR-0012-VAT-CODES-ON-ENTRY-LINES.md):
o IVA passa a poder ser indicado **na linha do lançamento** por um código, e a
classificação por conta (F2-09) fica como recurso e como valor proposto.

## Sub-incrementos

### F3-03A — Cadastro de códigos de IVA (feito)

- **`Domain/Accounting/VatCode`** — `Code` (até 12, maiúsculas, letras,
  algarismos e hífenes; único por empresa), `Name`, `Rate`, `Percentage`,
  `Nature`, `Region`, `IsActive`. Regras: taxa e natureza obrigatórias;
  percentagem entre 0 e 100; isento ⇔ percentagem zero.
- **`accounting.VatCodes`** — migração aditiva `20261003180058_VatCodes`;
  auditável, no `record-info` (`codigos-iva`) e no dicionário de dados.
- **`VatCodeService`** — `ListAsync`, `CreateAsync`, `UpdateAsync` e
  `EnsureDefaultsAsync`, que cria os códigos por omissão **em falta** para a
  região da empresa sem tocar nos existentes.
- **Códigos por omissão** (`VatCodeDefaults`): vendas (`V-`), serviços (`S-`),
  compras de existências (`C-EXI-`) e de outros bens e serviços (`C-OBS-`) nas
  três taxas; imobilizado, aquisições intracomunitárias de bens e de serviços e
  autoliquidação à taxa normal; `V-ISE` e `V-EXP` para as isentas.
- **Percentagens** — Continente 6 / 13 / 23, Açores 4 / 9 / 16, Madeira
  4 / 12 / 22. Fonte secundária (ver ADR); editáveis em cada código.
- **API:** `GET .../vat-codes?includeInactive=`, `POST .../vat-codes`,
  `PUT .../vat-codes/{código}`, `POST .../vat-codes/defaults` → `{ created }`.
- **PWA:** ecrã **«Códigos de IVA»** (`/codigos-iva`, grupo «Fiscal») — lista,
  «Criar códigos por omissão», criar e editar.

**Validação efetuada**

- `AccountingAI.Domain.Tests`: `VatCodeTests` — normalização e as regras de
  coerência.
- `AccountingAI.IntegrationTests`: `VatCodeServiceTests` — numa empresa dos
  Açores, os códigos por omissão trazem as percentagens da região e não
  substituem um código próprio com o mesmo nome; idempotência; edição,
  desativação, duplicado, isento com percentagem, sem acesso.
  `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: `VatCodesListAndValidateInput` (só leitura e
  recusas).
- `AccountingAI.Web`: `tsc`, `eslint` e o teste do ecrã.
- **Por verificar em direto:** o ecrã contra a base real.

### F3-03B — Código de IVA na linha do lançamento (feito)

- **Domínio:** `JournalEntryLine.VatCodeId` (opcional) e
  `JournalEntry.AddLine(..., vatCodeId)`.
- **`accounting.JournalEntryLines.VatCodeId`** — coluna anulável com FK a
  `VatCodes` sem cascata; migração aditiva
  `20261003180432_JournalEntryLineVatCode`.
- **`JournalPostingService`** — `JournalEntryLineInput.VatCode` (o código);
  lançar e corrigir recusam um código desconhecido ou inativo antes de gerar a
  referência. `PostedMovementLine.VatCode` devolve-o.
- **`VatCodeService.UpdateAsync`** — um código **com lançamentos** não pode
  mudar de taxa, percentagem, natureza nem região (mudaria a leitura do
  passado); o nome e o estado continuam editáveis. Sem lançamentos, tudo é
  editável.
- **API:** `vatCode` nas linhas de `POST`/`PUT .../accounting/movements` e nas
  leituras de movimentos.
- **PWA:** coluna **«IVA»** no editor de lançamentos, com os códigos ativos.
  Ao escolher a conta, o editor **propõe** o código quando a classificação
  dessa conta (F2-09) corresponde a exatamente um código ativo; nunca substitui
  um código já escolhido. O detalhe do movimento mostra o código da linha.

**Validação efetuada**

- `AccountingAI.Domain.Tests`: a linha guarda o código e recusa um
  identificador vazio.
- `AccountingAI.IntegrationTests`: `JournalPostingPartyTests` — lançar com
  código, reler depois de reabrir, corrigir sem código, código desconhecido e
  inativo recusados; o código com lançamentos não muda de percentagem mas muda
  de nome, e um código sem lançamentos muda livremente.
  `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: um `vatCode` desconhecido dá `400`.
- `AccountingAI.Web`: `tsc`, `eslint`, os 86 testes; a coluna foi confirmada no
  navegador em modo de demonstração (sem códigos — vêm do servidor).
- **Por verificar em direto:** a lista de códigos, a proposta pela conta e a
  gravação contra a base real.

**Ainda sem efeito no apuramento:** o resumo e o apuramento de IVA continuam a
ler só a classificação por conta até ao F3-03C.

### F3-03C — Resumo e apuramento a partir da linha

O resumo de IVA lê o código da linha e só recorre à classificação da conta
quando a linha não tem código.

### F3-03D — Lançamentos-tipo

Modelos de lançamento que calculam o IVA a partir do código e da base.
