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

### F3-03C — Resumo de IVA a partir da linha (feito)

- **`VatSummaryService`** — cada linha é lida pelo seu código de IVA quando o
  tem; sem código, pela classificação da sua conta, como antes. As duas origens
  somam-se na mesma combinação natureza / taxa / região.
- **Base e imposto:** uma linha com código numa conta `243*` é imposto;
  qualquer outra linha com código é **base** — incluindo as compras em classe 3
  e o imobilizado em classe 4, que a classificação por conta deixava de fora.
- **Contas por classificar:** só contam as linhas **sem código**; uma conta sem
  classificação cujas linhas trazem todas código deixa de aparecer.
- A API e a forma da resposta não mudam; o ecrã «Apuramento de IVA» só mudou os
  textos para referir os códigos.

O apuramento periódico (F2-08) assenta nos saldos das contas 24.3 e não
depende desta leitura; o mapa de conferência por documento e a ligação aos
campos oficiais ficam para a declaração periódica (F3-08).

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `VatSummaryLineCodeTests` — a mesma conta
  com uma linha sem código (taxa da conta) e outra com código de taxa reduzida
  (prevalece o código); compra em classe 3 com código entra como base; conta
  sem classificação com uma linha com código e outra sem — só a segunda fica
  por classificar. `VatSummaryServiceTests` (sem códigos) continua igual.
- A suite completa: 206 testes .NET, 86 de interface.

### F3-03D — Lançamentos-tipo (feito)

Um lançamento-tipo é o molde de um lançamento repetitivo: o diário, o tipo de
documento, a descrição proposta e as linhas, cada uma com um papel.

| Linha | O que recebe |
|---|---|
| **Base** (exatamente uma) | O valor indicado — ou, com IVA incluído, o valor sem o imposto |
| **IVA** (zero ou mais) | A base × a percentagem do código de IVA da linha |
| **Contrapartida** (exatamente uma) | O que falta para o débito igualar o crédito |

- **`Domain/Accounting/EntryTemplate`** + `EntryTemplateLine`, com
  `Compute(valor, comIvaIncluído, percentagens)`:
  - arredonda ao cêntimo, metade para cima;
  - com IVA incluído, a base é o total ÷ (1 + taxa) e **o imposto é o que
    sobra**, para o total bater ao cêntimo com o documento — só permitido com
    uma única linha de IVA do mesmo lado da base;
  - a autoliquidação modela-se com duas linhas de IVA em lados opostos: a
    contrapartida fica só com a base;
  - uma linha que fica a zero (código isento) é omitida;
  - uma contrapartida do lado errado é recusada.
- **`accounting.EntryTemplates`** e **`accounting.EntryTemplateLines`** —
  migração aditiva `20261003183508_EntryTemplates`; auditável, no
  `record-info` (`lancamentos-tipo`) e no dicionário de dados.
- **`EntryTemplateService`** — `ListAsync`, `SaveAsync` (cria ou substitui pelo
  código, com as linhas) e `ApplyAsync`, que **só calcula**: devolve o diário,
  o tipo de documento, a descrição e as linhas com o papel, a conta, o valor e
  o código de IVA. O lançamento continua a passar pelo `JournalPostingService`.
- **API:** `GET .../entry-templates?includeInactive=`,
  `PUT .../entry-templates/{código}`,
  `POST .../entry-templates/{código}/apply` com `{ amount, amountIncludesVat }`.
- **PWA:**
  - ecrã **«Lançamentos-tipo»** (`/lancamentos-tipo`, grupo «Cadastros») —
    lista e editor com as linhas (papel, lado, conta, código de IVA, descrição);
  - no **editor de lançamentos**, quando há modelos ativos, uma barra
    «Lançamento-tipo»: escolhe-se o modelo, indica-se o valor (com ou sem IVA)
    e «Preencher linhas» coloca o diário, o tipo de documento, a descrição e
    as linhas calculadas. Um terceiro já escrito passa para a contrapartida.
    As linhas ficam editáveis antes de gravar.

**Validação efetuada**

- `AccountingAI.Domain.Tests`: `EntryTemplateTests` — base + IVA + contrapartida;
  IVA incluído ao cêntimo (100,00 → 81,30 + 18,70); autoliquidação; linha
  isenta omitida; modelos e valores malformados.
- `AccountingAI.IntegrationTests`: `EntryTemplateServiceTests` — guardar,
  aplicar (sem lançar nada), IVA incluído, substituir as linhas sem duplicar,
  conta de integração / diário / código de IVA desconhecidos, inativo, sem
  acesso. `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: `EntryTemplatesListAndValidateInput`.
- `AccountingAI.Web`: `tsc`, `eslint`, os 87 testes; o ecrã foi aberto no
  navegador em modo de demonstração.
- **Por verificar em direto:** criar um modelo e preencher um lançamento com
  ele contra a base real.

**Fora deste incremento**

- Vários valores de base no mesmo modelo (uma fatura com duas taxas).
- Dedutibilidade parcial e pro rata.
- Modelos por omissão: dependem das contas de cada empresa; propô-los a partir
  do plano fica para a automação do lançamento (Bloco D).
