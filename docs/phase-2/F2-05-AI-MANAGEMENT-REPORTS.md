# F2-05 — Relatórios de gestão assistidos por IA

Épico da [Fase 2](README.md), ponto 8 da visão: relatórios de gestão redigidos
por IA sobre dados **já reconciliados**, com um interruptor de configuração,
modos **pré-definido** e **a pedido**, e cada afirmação ligada ao lançamento ou
conta de origem. A decisão de fornecedor e as fronteiras estão na
[`ADR-0006`](../architecture-decisions/ADR-0006-AI-REPORT-PROVIDER.md).

## Contexto

- A F2-01 deu confiança formal ao que entra (validação XSD + analítica). A F2-03
  reconcilia vendas SAF-T × compras e-Fatura × contabilidade. A F2-07 deu um
  motor de mapas configuráveis. Falta a leitura **redigida** desses dados: uma
  síntese de gestão que um contabilista poria à frente do cliente.
- Os ecrãs fixos (balancete, resultados, balanço, IVA) e o cruzamento já
  existem, mas cada um é uma tabela. Não há nada que os junte numa narrativa
  com contexto (variação homóloga, rácios, alertas).
- O risco de uma funcionalidade destas é dar a um modelo acesso ao razão inteiro
  ou a SQL, ou apresentar texto gerado como peça revista. A ADR-0006 fecha
  ambos: um **dossiê determinístico e limitado** com proveniência, saída
  estruturada, e um aviso fixo do nosso lado.

## Objetivo

- **Dossiê** — um serviço determinístico monta, para uma empresa e um período,
  um retrato estruturado e limitado a partir dos serviços de leitura que já
  existem: balancete do período e do homólogo, resumo de resultados e de
  posição, rácios, resumo do cruzamento, maiores movimentos. Cada valor traz a
  conta ou a referência de origem.
- **Narrador** — a fronteira `IManagementReportNarrator` (no `Application`),
  implementada em `NexGenAccountingAI.Infrastructure.Ai` com o SDK oficial do Claude,
  transforma o dossiê num `ManagementReportResult` estruturado. O `model` é
  configurável; sem chave, um narrador nulo devolve «IA não configurada».
- **Modos** — pré-definidos (`sintese-mensal`, `analise-rentabilidade`,
  `posicao-financeira`, `revisao-iva`, `alertas-de-fecho`) e a pedido (pergunta
  livre sobre o mesmo dossiê).
- **Interruptor** — por empresa (`reporting.CompanyAiSettings`). Desligado ou
  sem chave → geração responde `409`; o ecrã mostra só o interruptor e o
  histórico.
- **Persistência** — cada relatório guarda o dossiê, o resultado e a contagem de
  *tokens*; é reproduzível e inspecionável, e auditável (F2-06A).

Fora do âmbito desta fase: envio por email (fronteira de F2-07E); *fine-tuning*
ou memória do lado do modelo; geração automática agendada além do esboço
opcional em F2-05E; qualquer automatismo de submissão à AT.

## Desenho

### Application

```
Application/ManagementReports/
  ManagementReportKind.cs        // enum dos tipos pré-definidos
  ManagementReportRequest.cs     // { Kind?, Question?, From, To }
  ManagementReportDossier.cs     // dossiê: secções + proveniência + limites aplicados
  ManagementReportResult.cs      // { Resumo, Seccoes[], Alertas[], Indicadores[], AvisoIa }
  ManagementReportCatalog.cs     // tipos + rótulos pt-PT + secções que cada tipo usa
  IManagementReportNarrator.cs   // fronteira do modelo (dossiê -> resultado)
  IManagementReportDataService.cs// fronteira do dossiê (não expõe EF)
```

- `ManagementReportDossier` — `{ CompanyName, Period, ComparativePeriod,
  TrialBalanceDelta:[{AccountCode, AccountName, Class, Current, Comparative, Delta}],
  IncomeSummary, PositionSummary, Ratios:[{Key, Label, Value, Formula, Accounts[]}],
  CrossCheckSummary, TopMovements:[{Reference, Date, Description, Amount, AccountCodes[]}],
  Limits:[…] }`. Serializa para JSON e é guardado com o relatório.
- `ManagementReportResult` — o contrato de saída estruturada. `AvisoIa` é uma
  constante pt-PT, preenchida do nosso lado, nunca pelo modelo.

### Infrastructure.SqlServer

- `ManagementReportDataService : IManagementReportDataService` — monta o dossiê.
  Chama `AccountLedgerService.TrialBalanceAsync` duas vezes (período e homólogo),
  deriva os resumos de resultados/posição das classes 1–8 (a mesma lógica que os
  ecrãs fixos), calcula os rácios, chama `ICrossCheckService`, e ordena os
  movimentos por valor absoluto (topo 25). Limites: classes agregam abaixo de N
  contas, listas de divergência limitadas, movimentos limitados.
- `ManagementReportService` — orquestra: lê `CompanyAiSettings` (409 se
  desligado / sem chave) → monta o dossiê → chama `IManagementReportNarrator` →
  persiste `reporting.ManagementReport` → devolve. `CompanyAiSettingsService` —
  CRUD do interruptor.
- Esquema `reporting`: `reporting.ManagementReport` (`Id, CompanyId, Kind,
  Question?, PeriodFrom, PeriodTo, Model, DossierJson, ResultJson, PromptTokens,
  OutputTokens` + colunas de auditoria de F2-06A) e `reporting.CompanyAiSettings`
  (`Id, CompanyId` único, `Enabled, AutoMensal` + auditoria). Migrações aditivas.

### Infrastructure.Ai (projeto novo)

- `NexGenAccountingAI.Infrastructure.Ai` — referência ao pacote `Anthropic` e ao
  `Application`. `ClaudeManagementReportNarrator : IManagementReportNarrator`
  monta o pedido (sistema pt-PT por tipo em `ManagementReportPrompts`, o dossiê
  em JSON como mensagem do utilizador, `output_config` com o esquema de
  `ManagementReportResult`, *adaptive thinking*, `max_tokens` limitado), chama
  `client.Messages.Create`, e mapeia a resposta. `NullManagementReportNarrator`
  devolve um resultado «IA não configurada». `AiOptions` (`Model`, `ApiKey`,
  `MaxOutputTokens`) ligado de `Ai:*`. `AddAccountingAi(configuration)` regista
  o narrador conforme haja chave.
- O teste de arquitetura (`ModuleDependencyTests`) passa a incluir
  `NexGenAccountingAI.Infrastructure.Ai` (só referencia `Application`) e a permiti-lo
  na lista da API.

### API

- `GET .../companies/{companyId}/ai-settings` · `PUT` — o interruptor.
- `GET .../management-reports/catalog` — os tipos e rótulos (sem empresa).
- `GET .../companies/{companyId}/management-reports/dossier?kind=&from=&to=` — o
  dossiê, para inspeção (não chama o modelo).
- `POST .../companies/{companyId}/management-reports/generate?from=&to=` — corpo
  `{ kind }` ou `{ question }`; `409` se desligado / sem chave, `400` se período
  aberto, `404` sem acesso.
- `GET .../companies/{companyId}/management-reports` — histórico (últimos 20).
- `GET .../companies/{companyId}/management-reports/{id}` — um relatório
  (resultado + dossiê guardado).

### PWA

- Ecrã **«Relatórios de gestão»** (`/relatorios-gestao`) — um painel de
  configuração (interruptor `Enabled`/`AutoMensal`, escondido em modo demo), um
  seletor de tipo + período + «Gerar», uma caixa de pergunta livre, o resultado
  redigido (resumo, secções com ligações à origem, alertas, indicadores, aviso),
  e o histórico. Impressão / PDF (folha `@media print` como em F2-06E) e CSV dos
  indicadores.
- Entrada no menu lateral e no menu «Opções». Em modo demo/teste, o catálogo e o
  histórico leem-se; «Gerar» fica desativado com uma nota.

## Sub-incrementos

### F2-05A — Dossiê determinístico

- **Regras:** `Application/ManagementReports/` (records + `ManagementReportKind`
  + `ManagementReportCatalog` + as duas fronteiras);
  `ManagementReportDataService` (`Infrastructure.SqlServer`) monta o dossiê a
  partir do balancete (período + homólogo), dos resumos de resultados/posição,
  dos rácios, do cruzamento e dos maiores movimentos, com proveniência e
  limites; `GET .../management-reports/catalog` e
  `GET .../management-reports/dossier`. Sem IA.
- **Validação:** testes do serviço na suíte de integração (empresa construída:
  variação homóloga, um rácio, limite de movimentos); teste de API para o
  catálogo e o dossiê.

### F2-05B — Narrador (projeto `Infrastructure.Ai` + Claude)

- **Regras:** projeto `NexGenAccountingAI.Infrastructure.Ai` com o pacote `Anthropic`;
  `IManagementReportNarrator` + `ClaudeManagementReportNarrator` +
  `NullManagementReportNarrator` + `AiOptions` + `AddAccountingAi`;
  `ManagementReportPrompts` (pt-PT, por tipo) e a constante do aviso;
  `ModuleDependencyTests` atualizado.
- **Validação:** testes da montagem do pedido e do mapeamento da resposta (com
  um cliente falso / o narrador nulo); teste de arquitetura verde.

### F2-05C — Persistência e *endpoints* de geração

- **Regras:** `reporting.ManagementReport` + `reporting.CompanyAiSettings` +
  migrações; `ManagementReportService` (interruptor → dossiê → narrador →
  persistir) e `CompanyAiSettingsService`; `GET/PUT .../ai-settings`,
  `POST .../management-reports/generate`, `GET .../management-reports[/{id}]`.
  Auditável (F2-06A), no `record-info` (F2-06B) e no `DataDictionaryText`
  (F2-06C).
- **Validação:** testes da API de leitura (interruptor, histórico); integração
  para a escrita com um narrador falso; teste do `409` com a IA desligada.

### F2-05D — Ecrã «Relatórios de gestão» na PWA

- **Regras:** `/relatorios-gestao` + painel de configuração + seletor de tipo +
  pergunta livre + resultado redigido com ligações à origem + histórico +
  impressão/CSV; `WorkContext`; menu lateral e «Opções»; degradação em modo
  demo. Rótulos pt-PT.
- **Validação:** testes de interface (render do catálogo, interruptor desativado
  em demo, render de um resultado de exemplo).

### F2-05E — (opcional) Geração mensal automática

- **Regras:** quando `AutoMensal` está ligado, o `Worker` gera a `sintese-mensal`
  do período anterior uma vez por mês; sinal nas Tarefas. Ou documento de
  fronteira se ficar para outra fase.
- **Validação:** teste do agendador com um narrador falso.

## Decisões em aberto

- **Rácios a incluir** — começar por margem bruta, resultado líquido sobre
  rendimentos, liquidez geral, fundo de maneio, peso de FSE e de gastos com
  pessoal; afinar com uso real.
- **Período homólogo** — assumir o mesmo intervalo do ano anterior; quando não há
  exercício anterior carregado, o dossiê di-lo e o narrador omite a variação.
- **`model` por omissão** — `claude-opus-5`; rever quando houver medição de
  custo/qualidade sobre dossiês reais.
- **Geração automática (F2-05E)** — fazer agora ou deixar como fronteira.

## Referências

- [`ADR-0006`](../architecture-decisions/ADR-0006-AI-REPORT-PROVIDER.md) — a
  decisão de fornecedor e as fronteiras.
- [`ADR-0005`](../architecture-decisions/ADR-0005-REPORT-ENGINE.md) — o motor de
  mapas; o dossiê reaproveita as mesmas consultas.
- `AccountLedgerService`, `JournalReportService`, `ICrossCheckService` — as
  fontes do dossiê.
- F2-06A (auditoria), F2-03 (cruzamento) — a base.
