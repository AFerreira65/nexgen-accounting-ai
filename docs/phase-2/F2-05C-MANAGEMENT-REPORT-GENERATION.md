# F2-05C — Persistência e geração

Terceiro sub-incremento do [F2-05](F2-05-AI-MANAGEMENT-REPORTS.md). Junta o
dossiê (F2-05A) e o narrador (F2-05B) numa orquestração que verifica o
interruptor, gera e persiste.

## Regras

- **Domínio (`Domain/Reporting/`):**
  - `CompanyAiSettings` — uma linha por empresa: `Enabled`, `AutoMonthly`
    (a geração mensal fica para F2-05E). `Create`/`Update`.
  - `ManagementReport` — `Kind` (chave do tipo ou `"pergunta"`), `Question?`,
    `PeriodFrom`/`PeriodTo`, `Model`, `DossierJson`, `ResultJson`,
    `PromptTokens`, `OutputTokens`. É *append-only* — o dossiê fica guardado
    para o relatório ser reproduzível.
- **Esquema `reporting`:** `reporting.CompanyAiSettings` (único por `CompanyId`)
  e `reporting.ManagementReport` (índice por `CompanyId, PeriodFrom, PeriodTo`),
  ambos com as quatro colunas de auditoria de F2-06A. Migração aditiva
  `20260909180838_ManagementReports` (dois `CreateTable`, sem `AlterColumn`).
  Ambos entram na lista de tipos auditados e no `DataDictionaryText`;
  `ManagementReport` entra no `record-info` como `relatorios-gestao`.
- **`CompanyAiSettingsService`** — `GetAsync` devolve o estado (ou o default
  desligado) mais `NarratorConfigured` e `Model` do narrador; `SaveAsync`
  faz *upsert*. `null` sem acesso.
- **`ManagementReportService`** — `GenerateAsync`:
  1. verifica o acesso (`NotFound` se não tem);
  2. lê `CompanyAiSettings`; se não está `Enabled` → `Disabled` com motivo;
  3. se o narrador não tem chave → `Disabled` com motivo;
  4. monta o dossiê (`IManagementReportDataService`);
  5. chama o narrador (`IManagementReportNarrator`);
  6. persiste o `ManagementReport` (dossiê e resultado serializados) e devolve
     a vista com o resultado.
  `ListAsync` devolve os últimos 20 (com o resultado, sem o dossiê);
  `GetAsync` devolve um relatório com o dossiê guardado.
  `IManagementReportNarrator` ganhou `Model` (para a vista das definições).
- **API:**
  - `GET/PUT .../companies/{companyId}/ai-settings` — o interruptor;
  - `POST .../companies/{companyId}/management-reports/generate?from=&to=` —
    corpo `{ kind }` ou `{ question }`; `400` sem datas ou sem tipo/pergunta,
    `404` sem acesso, `409` desligado / sem chave;
  - `GET .../companies/{companyId}/management-reports` — histórico;
  - `GET .../companies/{companyId}/management-reports/{id}` — um relatório com o
    dossiê.

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `ManagementReportServiceTests` (narrador
  falso) — desligado devolve `Disabled`; ligado gera, persiste (uma linha),
  aparece no histórico com o resultado, e o `GetAsync` traz o dossiê; a
  pergunta livre grava `Kind = "pergunta"`; sem acesso devolve `NotFound`.
  `SqlServerMigrationTests` inclui a migração nova.
- `NexGenAccountingAI.Api.Tests`:
  `ManagementReportSettingsGuardGenerationAndHistoryIsReadable` — as definições
  leem-se (desligado, sem chave); gerar com o interruptor desligado dá `409`;
  ligar dá `200` e gerar dá na mesma `409` (sem chave no servidor de teste);
  sem datas ou sem tipo/pergunta dá `400`; o histórico é uma lista; empresa sem
  acesso dá `404`. O interruptor é reposto a desligado no fim.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- o ecrã «Relatórios de gestão» na PWA — F2-05D;
- a geração mensal automática (`AutoMonthly`) no `Worker` — F2-05E;
- uma verificação em direto contra a API do Claude (precisa de uma chave).
