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

- `NexGenAccountingAI.Domain.Tests`: `LineCanCarryAPartyButNotAnEmptyOne`.
- `NexGenAccountingAI.IntegrationTests`: `JournalPostingPartyTests` — lançar com
  terceiro (NIF com espaços), corrigir para um terceiro estrangeiro, conferir
  devolve o terceiro lido da base; desconhecido, inativo e o mesmo NIF noutro
  país são recusados sem criar lançamento. `SqlServerMigrationTests` inclui a
  migração.
- `NexGenAccountingAI.Api.Tests`: `MovementWithUnknownPartyIsRejectedBeforeWriting`.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, os 85 testes; a coluna foi confirmada no
  navegador em modo de demonstração (sem sugestões — essas vêm do servidor).
- **Por verificar em direto:** as sugestões e a gravação com terceiro contra a
  base real.

**Fora deste incremento**

- A importação de movimentos do SAF-T ainda não preenche o terceiro
  (`CustomerID` / `SupplierID` da transação) — precisa de guardar o
  identificador de origem do terceiro; fica para o F3-02C.
- Propor o terceiro a partir da subconta 21/22 escolhida — F3-01C.
- Filtrar movimentos e razão por terceiro — F3-01C.

### F3-02B — Data do documento (feito)

O número do documento de origem já existia (`DocumentReference`, o campo
«Documento» do editor); faltava a **data do documento**, que pode diferir da
data do lançamento — uma fatura de fevereiro lançada em março.

- **Domínio:** `JournalEntry.DocumentDate` (opcional), definido na criação.
- **`accounting.JournalEntries.DocumentDate`** — coluna `date` anulável;
  migração aditiva `20261003175412_JournalEntryDocumentDate`.
- **`JournalPostingService`** — `PostAsync` e `ReviseAsync` aceitam
  `documentDate`; `PostedMovement` devolve-a. Corrigir substitui o lançamento,
  por isso corrigir sem a indicar limpa-a.
- **API:** `documentDate` no corpo de `POST`/`PUT .../accounting/movements` e
  nas leituras de movimentos.
- **PWA:** campo **«Data do documento»** (opcional) no editor, fora da
  navegação por Enter; o detalhe do movimento mostra-a junto ao documento de
  origem.

Sem regra de coerência entre as duas datas: a lei não impede lançar um
documento com data posterior, e a validação fica para as regras analíticas.

**Validação efetuada**

- `NexGenAccountingAI.Domain.Tests`: `DocumentDateIsOptionalAndIndependentFromTheEntryDate`.
- `NexGenAccountingAI.IntegrationTests`: `JournalPostingPartyTests` — lançar com
  data do documento e corrigir sem ela; `SqlServerMigrationTests` inclui a
  migração.
- `NexGenAccountingAI.Web`: `tsc`, `eslint` e os 85 testes.
- **Por verificar em direto:** o campo no editor contra a base real.

### F3-02C — Terceiro na importação de movimentos do SAF-T (feito)

O SAF-T indica o cliente e/ou o fornecedor **ao nível da transação**
(`CustomerID` / `SupplierID`), não da linha. A importação de movimentos passa
a ligá-lo às linhas certas.

- **Leitor:** `SaftTransaction` ganha `CustomerId` e `SupplierId`, e
  `ReadTransactionsAsync` recolhe também os `Customer` / `Supplier` de
  `MasterFiles` (`SaftTransactions.Parties`) na mesma passagem — o corpo do
  pedido não se pode ler duas vezes.
- **Do identificador do ficheiro ao terceiro da empresa:** o identificador
  leva ao registo de `MasterFiles`, e este à chave país + contribuinte pelas
  **mesmas regras** da importação de terceiros (`PartyImportService.KeyOf`).
- **A que linhas se atribui:** às linhas da conta a que o ficheiro liga esse
  terceiro (`AccountID` do registo); na falta delas, às linhas das contas 21
  (cliente) ou 22 (fornecedor). Sem nenhuma, o lançamento fica sem terceiro.
- **Não cria terceiros.** Se o terceiro ainda não existe na empresa, a linha
  fica sem ele e a transação é contada (`TransactionsWithUnknownParty`). O
  consumidor final e os registos que a importação de terceiros deixaria de
  fora não contam.
- **Resultado:** `LinesWithParty` e `TransactionsWithUnknownParty` na resposta
  de `saft/entries-import`; o ecrã de importação mostra-os, com a indicação de
  importar os terceiros antes dos movimentos.

**A ordem importa.** Reimportar um ficheiro não altera os lançamentos que já
existem (a referência já está usada), por isso os terceiros têm de ser
importados **antes** dos movimentos: plano de contas → terceiros → movimentos.
Completar os terceiros de lançamentos já importados fica para um incremento
próprio.

**A data do documento não é preenchida.** No SAF-T, `TransactionDate` é a data
do movimento (já usada como data do lançamento) e `GLPostingDate` é a data de
gravação no sistema de origem; nenhuma é a data do documento, e não se inventa.

**Validação efetuada**

- `NexGenAccountingAI.Infrastructure.Files.Tests`: `SaftEntriesReaderPartyTests` — a
  transação traz o cliente ou o fornecedor e os terceiros de `MasterFiles` são
  recolhidos.
- `NexGenAccountingAI.IntegrationTests`: `SaftEntriesImportPartyTests` — terceiro
  pela conta do ficheiro, pelo prefixo 21, fornecedor ainda inexistente
  (contado, sem terceiro), consumidor final (não contado) e transação sem
  terceiro.
- O leitor foi corrido contra os dois SAF-T de contabilidade de `SAFTs Exemplo`:
  em 2024, das 18 300 referências a terceiro, 18 290 ligam pela conta do
  terceiro, 3 pelo prefixo e 7 não têm linha de terceiros; em 2025, 22 132 de
  22 308 pela conta, 130 pelo prefixo e 46 sem linha.
- A suite completa: 215 testes .NET, 87 de interface.
- **Por verificar em direto:** uma importação real com os terceiros já criados.
