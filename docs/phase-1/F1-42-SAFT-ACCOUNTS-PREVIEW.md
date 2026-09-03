# F1-42 — SAF-T: leitura do plano de contas (backend)

## Objetivo

Primeiro passo para importar contabilidade de um SAF-T além do cabeçalho: ler o
plano geral de contas do ficheiro e dizer, para uma empresa, quais os códigos que
já existem e quais seriam novos. Só lê e compara — não cria nem altera contas.

## Regras

`ISaftAccountsReader` / `SaftAccountsReader` (em `Infrastructure.Files`) lê o bloco
`MasterFiles/GeneralLedgerAccounts` com `XmlReader` — DTD proibido, sem resolução
externa, `Windows-1252` suportado. Materializa só esse subárvore e para se chegar
a `GeneralLedgerEntries` (os movimentos vêm depois). Cada `Account` dá um
`SaftAccount(Code, Description)` a partir de `AccountID`/`AccountDescription`;
códigos repetidos são ignorados. Um ficheiro sem o bloco devolve lista vazia (um
SAF-T de faturação não tem contas); XML ilegível lança `SaftFileFormatException`.

`SaftAccountsPreviewService` (em `Infrastructure.SqlServer`) confirma que o
utilizador tem acesso ativo à empresa, lê as contas do ficheiro e classifica cada
uma como `Existing` ou `New` face aos códigos do plano da empresa (comparação sem
distinção de maiúsculas). Devolve `SaftAccountsReconciliation` com as contagens
(total, existentes, novas, contas de movimento da empresa) e a lista ordenada.

## Superfície de validação local

`POST .../users/{userId}/companies/{companyId}/saft/accounts-preview` (corpo = o
XML, como em `/saft/inspect`) devolve a reconciliação, `404` se o utilizador não
tiver acesso à empresa e `400` para XML ilegível.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- testes de `Infrastructure.Files` (11): o leitor extrai as contas, ignora os movimentos e os códigos repetidos, lê `Windows-1252`, devolve vazio sem bloco e rejeita conteúdo não-XML;
- teste de API: `/foundation` indica a fase `F1-42`; `accounts-preview` com contas `11`, `6221` e `ZZZ999` devolve `fileAccountCount:3`, `existingCount:2`, `newCount:1` com os estados `Existing`/`New`; XML partido devolve `400`; um SAF-T sem contas devolve `fileAccountCount:0`; uma empresa sem acesso devolve `404`;
- verificação contra a base `accountingAI`: `accounts-preview` da empresa de demonstração classifica `11` e `6221` como existentes e `4210001` como nova (44 contas de movimento no plano);
- suite .NET completa (64 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e nove testes de interface e build de produção da PWA.

## Fora deste incremento

- ecrã de importação a mostrar a pré-visualização (F1-43);
- criar as contas novas na empresa a partir do ficheiro;
- ler saldos de abertura, categoria/agrupamento da taxonomia e contas de terceiros (`Customer`/`Supplier`);
- importar os movimentos (`GeneralLedgerEntries`).
