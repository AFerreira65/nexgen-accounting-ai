# F1-51 — SAF-T: pré-visualização dos movimentos

## Objetivo

Antes de importar os movimentos de um SAF-T, mostrar um resumo do bloco
`GeneralLedgerEntries`: quantas transações, totais declarados, se estão
equilibrados, o período e os diários — sem carregar as linhas.

## Regras

`ISaftEntriesReader` / `SaftEntriesReader` (em `Infrastructure.Files`) percorre o
bloco `GeneralLedgerEntries` totalmente em streaming com `XmlReader` (DTD
proibido, sem resolução externa, `Windows-1252` suportado): lê `NumberOfEntries`,
`TotalDebit`, `TotalCredit`, conta as `Transaction` por `JournalID`, capta a
descrição de cada diário e o intervalo de `TransactionDate`. Não materializa as
`Lines`, pelo que suporta ficheiros grandes. Um ficheiro sem o bloco devolve
`HasEntries = false`; XML ilegível lança `SaftFileFormatException`.

`SaftEntriesPreviewService` (em `Infrastructure.SqlServer`) confirma o acesso do
utilizador à empresa, lê o resumo e marca, por diário, se o código já existe no
diário da empresa. `Balanced` compara `TotalDebit` e `TotalCredit` (2 casas).

## Superfície de validação local

`POST .../companies/{companyId}/saft/entries-preview` (corpo = o XML) devolve o
resumo (`hasEntries`, `transactionCount`, `declaredEntryCount`, `totalDebit`,
`totalCredit`, `balanced`, `fromDate`, `toDate`, `journals[]`), `404` sem acesso e
`400` para XML ilegível. No ecrã **Importar SAF-T**, com ligação ao SQL Server, o
painel **Movimentos do ficheiro** mostra este resumo e a tabela de diários.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- testes de `Infrastructure.Files` (16): o leitor resume o ficheiro sem materializar as linhas, conta as transações por diário, deteta um ficheiro desequilibrado, devolve vazio sem bloco, lê `Windows-1252` e rejeita conteúdo não-XML;
- teste de integração SQL Server LocalDB: `PreviewAsync` marca o diário `GERAL` como existente e um diário só do ficheiro como novo; sem acesso devolve `null`;
- teste de API: `/foundation` indica a fase `F1-51`; `entries-preview` devolve `"hasEntries":true`, `"transactionCount":2`, `"balanced":true`, o diário `COMPRAS` com `"exists":true` e o diário `NOVO` com `"exists":false`;
- verificação no browser contra a base `accountingAI`: um SAF-T com 2 transações (350/350) e dois diários mostra o resumo, o aviso de equilíbrio e a tabela com `COMPRAS` (já existe) e `OUTRO` (novo);
- suite .NET completa (70 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- importar de facto os movimentos (criar diários em falta e os lançamentos);
- validar o equilíbrio linha a linha e assinalar contas em falta;
- ler os documentos de origem (`SourceDocuments`).
