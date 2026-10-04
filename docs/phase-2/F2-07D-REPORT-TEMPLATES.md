# F2-07D — Modelos de impressão dos relatórios fixos

Quarto sub-incremento do [F2-07](F2-07-CONFIGURABLE-REPORTS.md). Um cabeçalho de
impressão configurável por empresa e por relatório — o equivalente aos IDU do
PHC.

## Regras

- **`reporting.ReportTemplate`** (`Domain/Reporting`) — por `(CompanyId,
  ReportKey)` único: `HeaderLinesJson` (linhas de cabeçalho livres, até 6),
  `ShowFirmName`, `ShowCompanyIdentity`, `Orientation`
  (`ReportOrientation` retrato/paisagem), `RowVersion` + as colunas de
  auditoria de F2-06A. Migração `20260908161754_ReportTemplate`.
- `ReportTemplateKeys` (`Application/Reporting`) — as chaves válidas:
  `balancete`, `razao`, `livro-diario`, `resultados`, `balanco`, `iva`,
  `contas-correntes`, `analises`.
- `ReportTemplateService` (`Infrastructure.SqlServer`) — `GetAsync` devolve o
  modelo guardado **ou um por omissão** (a folha impressa tem sempre um
  cabeçalho), já com o nome do gabinete e a identificação da empresa; `SaveAsync`
  faz *upsert*, apara as linhas em branco e limita a 6. `null` para chave
  desconhecida ou empresa sem acesso.
- **API:** `GET .../companies/{companyId}/report-templates/{reportKey}` e `PUT`
  (`{ headerLines, showFirmName, showCompanyIdentity, orientation }`).
- **PWA:** `ReportChrome` (`src/app/ReportChrome.tsx`) — um componente
  partilhado por todos os relatórios fixos. Mostra:
  - um **cabeçalho só de impressão** (`.report-print-head`): gabinete, empresa
    · NIF, título do relatório, período, as linhas de cabeçalho, e «Impresso
    em …»; quando a orientação é paisagem injeta `@page { size: landscape }`;
  - uma barra **só de ecrã** com «Imprimir / PDF» e «Configurar impressão» (um
    painel com as linhas de cabeçalho, os dois interruptores e a orientação,
    guardado por `PUT`).
  Ao imprimir, uma folha `@media print` esconde a navegação, a barra de topo, o
  cabeçalho do ecrã, os cartões de métricas e a barra de filtros, e dá grelha à
  tabela.
- `ReportChrome` está ligado aos sete relatórios fixos (`TrialBalancePage`,
  `JournalReportPage`, `IncomeStatementPage`, `BalanceSheetPage`,
  `VatSummaryPage`, `PartyBalancesPage`, `AccountPage`), cada um com
  `className="page-stack report-page"` e uma linha `<ReportChrome …/>`.

Sem alteração das opções de JSON globais.

## Superfície de validação local

Abrir um relatório fixo (ex.: `/balancete`), «Configurar impressão», escrever
linhas de cabeçalho, escolher a orientação, «Guardar modelo»; «Imprimir / PDF».

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: teste novo `ReportTemplateTests` — chave
  desconhecida devolve `null`; o modelo por omissão traz a identificação da
  empresa e do gabinete; guardar apara as linhas em branco, limita a 6, grava a
  orientação, faz *upsert* (uma só linha) e carimba `CreatedBy`/`ModifiedAtUtc`;
  sem acesso devolve `null`. O teste da migração aplica `ReportTemplate` duas
  vezes.
- `NexGenAccountingAI.Api.Tests`: teste novo — o `GET` devolve um modelo por omissão
  com `companyName`; o `PUT` grava as linhas e a orientação; chave desconhecida
  dá `404` (e o teste repõe o default).
- `NexGenAccountingAI.Web`: lint, build e setenta e oito testes de interface (o
  cabeçalho de impressão usa `<p>` e não `<h1>`, para não colidir com o
  cabeçalho do ecrã nos testes existentes);
- `dotnet build`/`dotnet test` na solução inteira (cento e quarenta e oito
  testes .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências;
- verificação no browser contra `accountingAI` (empresa Vidreira Algarvia): em
  `/balancete` o cabeçalho de impressão traz «Gabinete Horizonte / Vidreira
  Algarvia Lda · NIF 500584095 / Balancete»; guardar «Documento de trabalho» +
  «Confidencial», desligar o nome do gabinete e escolher paisagem actualiza o
  cabeçalho e injeta `@page { size: landscape }`, e persiste ao recarregar; o
  `/livro-diario` continua com o seu próprio modelo (por omissão). Linha de
  teste removida da base.

## Fora deste incremento

- a origem `SaldosDeConta` do runner e a fronteira do envio por email — F2-07E
  (fecha o épico);
- reordenar/esconder colunas por relatório (o modelo por agora é só o
  cabeçalho e a orientação).
