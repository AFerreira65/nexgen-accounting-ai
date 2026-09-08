# F2-01F — Persistência e histórico das validações de SAF-T

Sexto sub-incremento do [F2-01](F2-01-SAFT-XSD-VALIDATION.md). Guarda cada
execução da validação e mostra o histórico por empresa.

## Regras

- **Domínio:** `SaftValidationRun` (`Domain/Saft`) — registo de leitura de uma
  validação: `CompanyId?`, `UserId`, `FileName`, `AuditFileVersion?`,
  `SchemaAvailable`, `SchemaValid`, contagens por severidade, `Truncated`,
  `IssuesJson` (a lista de ocorrências serializada) e `CreatedAtUtc`.
- **Esquema:** tabela `accounting.SaftValidationRun`, índice
  `(CompanyId, CreatedAtUtc)`, migração EF Core
  `20260908092224_SaftValidationRun` (aditiva — não toca nas tabelas
  existentes).
- **`POST .../saft/validate`** ganha `?companyId=` e `?fileName=` opcionais.
  Depois de compor o relatório, grava um `SaftValidationRun` (com o
  `companyId` só quando o utilizador tem acesso a essa empresa; caso
  contrário nulo — a validação pode correr antes de a empresa existir).
- **`GET .../users/{userId}/companies/{companyId}/saft/validation-reports`**
  — as últimas vinte execuções dessa empresa, mais recentes primeiro, com o
  resumo e as ocorrências (relidas do JSON). 404 se o utilizador não tem
  acesso.
- **PWA:** `WorkContext.validateSaft(file, companyId?)` passa `fileName` e
  `companyId`; `saftValidationReports(companyId)` lê o histórico. O painel
  «Validação do ficheiro» mostra uma tabela **«Validações anteriores»**
  (ficheiro, data, erros, avisos, estado do esquema) e recarrega-a depois de
  cada validação nova.
- **Ferramentas:** adicionado o manifesto de ferramentas locais
  (`.config/dotnet-tools.json`) com `dotnet-ef` 10.0.11, para as migrações
  serem geráveis sem instalação global.

Sem alteração ao `saft/inspect` (o campo `schemaValid` aí não se
justifica — o painel de validação da PWA já dá o sinal, sem transformar o
`inspect` numa leitura do ficheiro inteiro; resolve a decisão em aberto
n.º 3). A fase do `/foundation` mantém-se em `F2-01`.

## Superfície de validação local

`POST .../saft/validate?companyId=…&fileName=…` grava a execução;
`GET .../saft/validation-reports` devolve-a; o painel de importação mostra
«Validações anteriores».

## Validação efetuada

- `AccountingAI.IntegrationTests`: cinco testes — o de migrações confirma
  `20260908092224_SaftValidationRun` e grava/relê um `SaftValidationRun`
  (contagens, `SchemaValid`, `CompanyId`, JSON das ocorrências,
  `CreatedAtUtc`);
- `AccountingAI.Api.Tests`: vinte testes (um novo — `validation-reports`
  devolve um array e 404 para empresa sem acesso);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (107 testes
  .NET);
- `AccountingAI.Web`: lint, build e sessenta e três testes de interface
  (sem alteração — o painel só corre ligado à base de dados);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação com a API local contra `accountingAI`: validar um ficheiro
  com `companyId` grava a execução; `validation-reports` devolve-a com as
  ocorrências relidas do JSON; no browser, o painel mostra «Validações
  anteriores» com as execuções da empresa e atualiza-as após cada nova
  validação.

## Fora deste incremento

- inventário SVAT (F2-01G).
