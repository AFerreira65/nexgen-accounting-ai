# F2-03A — Ingestão das compras do e-Fatura (CSV)

Primeiro sub-incremento do épico [F2-03](F2-03-CROSS-CHECK.md). Traz para a
nova plataforma a leitura e a persistência das faturas de compra
conhecidas pelo Portal e-Fatura, a partir de um CSV.

## Regras

- **Domínio:** `EFaturaPurchase` (`Domain/Efatura`) — uma fatura de compra:
  `CompanyId`, `ImportBatchId`, NIF e nome do emitente, `InvoiceNo`,
  `Atcud?`, `InvoiceDate`, `InvoiceType`, `Status?`, `Sector?`, `TaxBase`,
  `TaxAmount`, `GrossTotal`, `SourceFileName`, `DocumentKey`
  (`NIF|nº|data` normalizado), `CreatedAtUtc`.
- **Esquema:** tabela `accounting.EFaturaPurchase`, índice **único**
  `(CompanyId, DocumentKey)` e índice `(CompanyId, InvoiceDate)`, migração
  aditiva `20260908120313_EFaturaPurchase`.
- **`IEFaturaCsvReader` + `EFaturaCsvReader`** (`Infrastructure.Files`):
  porta a lógica do `EfaturaCsvImporter` do monitor **sem
  `Microsoft.VisualBasic`** — deteta a codificação (BOM / UTF-8 /
  Windows-1252) e o separador (`;`, `,`, tabulação), canoniza os nomes das
  colunas (acentos, maiúsculas, `º`→`o`, pontos), separa `InvoiceNo` /
  `ATCUD`, extrai o NIF do nome quando não há coluna própria, lê valores
  pt-PT (vírgula decimal, ponto de milhares) e datas em vários formatos.
  Marca a linha sem número, com NIF inválido ou data ilegível. Avisa aos
  300 registos.
- **`EFaturaPurchaseImportService`** (`Infrastructure.SqlServer`):
  importa numa transação, **idempotente** pela `DocumentKey` — reimportar
  o mesmo CSV devolve `alreadyExisted`, não duplica; `dryRun` não escreve.
- **API:**
  - `POST .../companies/{companyId}/efatura/purchases-import?dryRun=&fileName=`
    (corpo = CSV, `text/csv`) → `{ batchId?, imported, alreadyExisted,
    invalid, invalidRows[], warnings[] }`; CSV ilegível → 400; sem acesso
    → 404.
  - `GET .../companies/{companyId}/efatura/purchases?from=&to=&page=&pageSize=`
    → envelope paginado, mais recentes primeiro.

Sem alteração ao SAF-T. A fase do `/foundation` mantém-se em `F2-01`.

## Superfície de validação local

`POST .../efatura/purchases-import` com um CSV do Portal cria as faturas;
reimportar não duplica; `GET .../efatura/purchases` lista-as.

## Validação efetuada

- `AccountingAI.Infrastructure.Files.Tests`: cinquenta e um testes (sete
  novos) — layout padrão do Portal (`;`, vírgula decimal, milhares),
  ficheiro com `,` e BOM e `ATCUD` entre aspas, Windows-1252, NIF extraído
  do nome, linhas sem número / NIF inválido assinaladas, aviso aos 300,
  ficheiro vazio a lançar;
- `AccountingAI.IntegrationTests`: cinco testes — o de migrações confirma
  `20260908120313_EFaturaPurchase` e a unicidade de `(CompanyId,
  DocumentKey)`;
- `AccountingAI.Api.Tests`: vinte e um testes (um novo — pré-visualização
  `dryRun` devolve `imported`/`invalid`, CSV vazio → 400, lista devolve o
  envelope, empresa sem acesso → 404);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (115 testes
  .NET); testes de arquitetura verdes;
- `AccountingAI.Web`: sessenta e três testes sem alteração (só backend);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação com a API local contra `accountingAI`: importar um CSV de
  duas faturas cria 2; reimportar devolve `alreadyExisted: 2`; a lista
  devolve as duas com os valores certos. As linhas de teste foram
  removidas da base a seguir.

## Fora deste incremento

- ecrã de importação/lista na PWA (F2-03B);
- o relatório de cruzamento (F2-03C).
