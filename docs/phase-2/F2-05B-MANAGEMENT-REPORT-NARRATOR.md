# F2-05B — Narrador (projeto `Infrastructure.Ai` + Claude)

Segundo sub-incremento do [F2-05](F2-05-AI-MANAGEMENT-REPORTS.md). Introduz o
projeto que fala com o modelo e a fronteira que o esconde do resto
([`ADR-0006`](../architecture-decisions/ADR-0006-AI-REPORT-PROVIDER.md)).

## Regras

- **Fronteira (`Application/ManagementReports/`)** — `IManagementReportNarrator`:
  `IsConfigured` (há chave?) e `GenerateAsync(request, dossier)` →
  `ManagementReportNarration` (`{ Result, Model, PromptTokens, OutputTokens }`).
- **Projeto novo `AccountingAI.Infrastructure.Ai`** — só referencia
  `AccountingAI.Application` e o pacote `Anthropic` (12.46.0; a única
  dependência transitiva é `Microsoft.Extensions.AI.Abstractions`). É o único
  sítio que conhece o SDK. Adicionado ao `Accounting AI.slnx`, à referência da
  API, e ao teste de arquitetura (`ModuleDependencyTests`: `Infrastructure.Ai`
  → `Application`; a API passa a poder referenciá-lo).
- **`AiOptions`** (secção `Ai`) — `Model` (por omissão `claude-opus-5`),
  `ApiKey` (só de *user-secrets* / `ANTHROPIC_API_KEY` — nunca de
  `appsettings*.json`), `MaxOutputTokens` (12000, inclui o raciocínio).
- **`ClaudeManagementReportNarrator`** — monta o pedido: enunciado de sistema
  pt-PT por tipo (`ManagementReportPrompts`, com as regras «não inventes / cita
  a origem / sem homólogo quando não há» e a forma exata do JSON de resposta),
  o dossiê serializado como mensagem do utilizador, `ThinkingConfigAdaptive`,
  `MaxTokens`. Chama `client.Messages.Create` e mapeia a resposta com
  `ManagementReportNarrationMapper` (extrai o primeiro objeto JSON mesmo que
  venha em cercas; se nada for legível, devolve um resultado com um alerta em
  vez de rebentar). O aviso fixo (`ManagementReportResult.Notice`) é sempre
  acrescentado do nosso lado. Sem chave, comporta-se como o nulo.
- **`NullManagementReportNarrator`** — devolve sempre «IA não configurada» com
  a indicação de onde pôr a chave.
- **`AddAccountingAi(configuration)`** — liga `AiOptions`, completa a chave com
  `ANTHROPIC_API_KEY` quando a secção não a traz, e regista o narrador. Chamado
  no `Program.cs`; nenhum *endpoint* o usa ainda.
- `appsettings.json` ganha `"Ai": { "Model": "claude-opus-5", "MaxOutputTokens":
  12000 }` — sem chave.

Sem alteração de esquema nem de API.

## Validação efetuada

- `AccountingAI.Infrastructure.Files.Tests`: `ManagementReportNarratorTests` —
  o enunciado de sistema leva a tarefa do tipo e a forma do JSON; a mensagem do
  utilizador leva a pergunta e o dossiê; o *mapper* lê uma resposta bem formada
  (com cercas) e devolve as secções, referências, alertas e indicadores com o
  aviso fixo; degrada com um alerta quando a resposta é ilegível; o narrador sem
  chave diz «não configurado».
- `AccountingAI.ArchitectureTests` verde com o módulo novo.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` (inventário e auditoria NuGet, incluindo o
  pacote novo) sem ocorrências.

## Fora deste incremento

- a persistência (`reporting.ManagementReport`), o interruptor
  (`reporting.CompanyAiSettings`), o `ManagementReportService` e os *endpoints*
  de geração — F2-05C;
- o ecrã «Relatórios de gestão» na PWA — F2-05D;
- uma verificação em direto contra a API do Claude (precisa de uma chave; fica
  para a verificação manual de F2-05C).
