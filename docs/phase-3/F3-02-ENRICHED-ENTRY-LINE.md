# F3-02 — Linha de lançamento enriquecida

Segundo épico do Bloco A da [Fase 3](README.md). A linha do lançamento só
tinha conta, descrição, débito e crédito; as declarações precisam de saber a
quem diz respeito cada valor e que IVA lhe corresponde.

## Desenho

A linha ganha campos **opcionais**: os lançamentos que já existem não mudam e
não precisam de migração de dados.

| Campo | Para quê | Incremento |
|---|---|---|
| Terceiro | Recapitulativa, Modelo 30, pendentes por entidade, lançamentos a partir do e-Fatura | F3-02A |
| Código de IVA e base tributável | Declaração periódica com mapa de conferência | Depois do F3-03 (tabela de códigos de IVA) |
| Data e número do documento externo | SAF-T (`SourceDocumentID`), cruzamento com o e-Fatura | F3-02B |

O terceiro identifica-se na API pelo **país + contribuinte**, não pelo
identificador interno: é o que o utilizador escreve, o que o SAF-T e o e-Fatura
trazem, e o que as importações futuras vão usar.

## Sub-incrementos

### F3-02A — Terceiro na linha (feito)

- **Domínio:** `JournalEntryLine.PartyId` (opcional) e
  `JournalEntry.AddLine(..., partyId)`; um identificador vazio é recusado.
- **`accounting.JournalEntryLines.PartyId`** — coluna anulável com FK a
  `Parties` sem cascata; migração aditiva `20261003174527_JournalEntryLineParty`.
- **`JournalPostingService`** — `JournalEntryLineInput` aceita
  `PartyTaxNumber` e `PartyCountryCode` (por omissão `PT`); lançar e corrigir
  resolvem o terceiro **antes** de gerar a referência, por isso um terceiro
  desconhecido ou inativo recusa o lançamento sem gastar numeração.
  `PostedMovementLine` devolve o país, o contribuinte e o nome.
- **API:** as linhas de `POST`/`PUT .../accounting/movements` aceitam
  `partyTaxNumber` e `partyCountryCode`; as leituras de movimentos devolvem
  `partyCountryCode`, `partyTaxNumber` e `partyName` por linha.
- **PWA:** coluna **«Terceiro»** no editor de lançamentos — escreve-se o NIF
  (ou `ES B12345678` para outro país) ou parte do nome, com sugestões pedidas
  ao servidor; o nome escolhido fica por baixo do campo. É opcional e fica
  fora da navegação por Enter (chega-se com Tab), para não abrandar o
  lançamento de linhas sem terceiro. O detalhe do movimento mostra o terceiro
  de cada linha.

**Validação efetuada**

- `AccountingAI.Domain.Tests`: `LineCanCarryAPartyButNotAnEmptyOne`.
- `AccountingAI.IntegrationTests`: `JournalPostingPartyTests` — lançar com
  terceiro (NIF com espaços), corrigir para um terceiro estrangeiro, conferir
  devolve o terceiro lido da base; desconhecido, inativo e o mesmo NIF noutro
  país são recusados sem criar lançamento. `SqlServerMigrationTests` inclui a
  migração.
- `AccountingAI.Api.Tests`: `MovementWithUnknownPartyIsRejectedBeforeWriting`.
- `AccountingAI.Web`: `tsc`, `eslint`, os 85 testes; a coluna foi confirmada no
  navegador em modo de demonstração (sem sugestões — essas vêm do servidor).
- **Por verificar em direto:** as sugestões e a gravação com terceiro contra a
  base real.

**Fora deste incremento**

- A importação de movimentos do SAF-T ainda não preenche o terceiro
  (`CustomerID` / `SupplierID` da transação) — precisa de guardar o
  identificador de origem do terceiro; fica para o F3-02C.
- Propor o terceiro a partir da subconta 21/22 escolhida — F3-01C.
- Filtrar movimentos e razão por terceiro — F3-01C.

### F3-02B — Documento externo (a seguir)

Número e data do documento de origem no lançamento, separados da referência
interna.
