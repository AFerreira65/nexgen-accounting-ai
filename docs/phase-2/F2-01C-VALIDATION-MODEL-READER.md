# F2-01C — Leitor do modelo de validação do SAF-T

Terceiro sub-incremento do épico [F2-01](F2-01-SAFT-XSD-VALIDATION.md). É a
base de dados da camada analítica: uma passagem única pelo ficheiro que
recolhe o que as regras precisam. As regras em si vêm em F2-01D.

## Objetivo

Ter um `SaftValidationModel` — campos do cabeçalho, conjunto de contas do
plano, contagens e totais do bloco de movimentos, e listas curtas das
transações problemáticas — construído sem materializar o ficheiro.

## Regras

- **Contrato** (`Application/Saft`): `ISaftValidationModelReader.ReadAsync(Stream)
  → SaftValidationModel`. Lança `SaftFileFormatException` para XML mal
  formado.
- **`SaftValidationModel`** (record) contém:
  - cabeçalho: `AuditFileVersion`, `TaxRegistrationNumber`,
    `TaxAccountingBasis`, `FiscalYear`, `StartDate`, `EndDate`, `ProductId`,
    `ProductCompanyTaxId`, `SoftwareCertificateNumber`;
  - `AccountIds` — `AccountID` de `MasterFiles/GeneralLedgerAccounts`;
  - `JournalCount`, `HasGeneralLedgerEntries`, `DeclaredNumberOfEntries`,
    `DeclaredTotalDebit`, `DeclaredTotalCredit`;
  - `TransactionCount`, `LineDebitTotal`, `LineCreditTotal` (soma de todas
    as linhas de todas as transações);
  - `UnbalancedTransactions`, `UnresolvedAccountRefs`,
    `OutOfPeriodTransactions` — listas limitadas a 50, com
    `FlaggedListsTruncated` a assinalar o corte.
- **`SaftValidationModelReader`** (`Infrastructure.Files`): uma passagem em
  streaming (`XmlReader`, DTD proibido, sem resolução externa, Windows-1252),
  com o mesmo padrão dos leitores existentes. Cada `<Transaction>` é lida
  como subárvore transitória (`XElement`), somada e descartada — não se
  guardam transações nem linhas. Para no fim de `GeneralLedgerEntries`.
- Registado em `AddSaftFileReading` como singleton.

Sem alteração de esquema de base de dados nem de API. A fase do
`/foundation` mantém-se em F2-01.

## Superfície de validação local

Os testes de `Infrastructure.Files` verificam o modelo contra ficheiros
sintéticos; o consumidor (motor de regras) chega em F2-01D.

## Validação efetuada

- `NexGenAccountingAI.Infrastructure.Files.Tests`: trinta e três testes (seis
  novos) — lê cabeçalho, contas, diários e totais; assinala uma transação
  desequilibrada (e o total de linhas do ficheiro reflete-o), uma
  referência a conta inexistente, uma transação fora do período; um
  ficheiro sem movimentos reporta zero; XML mal formado lança;
- `dotnet build`/`dotnet test` na solução inteira sem falhas (94 testes
  .NET); testes de arquitetura verdes;
- `NexGenAccountingAI.Web`: sessenta e três testes sem alteração;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos.

## Fora deste incremento

- as regras analíticas e a sua composição no endpoint `saft/validate`
  (F2-01D);
- saldos de abertura/fecho por conta e movimento por conta (para a regra
  `OPENING-CLOSING-CONSISTENT`) e os NIF de clientes/fornecedores (para
  alargar a `NIF-CHECKDIGIT`) — o modelo cresce quando essas regras
  entrarem;
- passo de validação na PWA (F2-01E) e persistência (F2-01F).
