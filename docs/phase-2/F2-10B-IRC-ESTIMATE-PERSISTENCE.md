# F2-10B — Persistência da estimativa de IRC

Segundo sub-incremento do [F2-10](F2-10-IRC-ESTIMATE.md). Guarda a estimativa
de um período — a taxa e as correções fiscais indicadas — e recalcula sempre
contra o razão atual ao reler, à imagem da persistência do apuramento de IVA
([F2-08C](F2-08C-VAT-SETTLEMENT-PERSISTENCE.md)).

## Regras

- **`Domain/Accounting/IrcEstimateFiling`** — uma estimativa guardada por
  `(empresa, período)`: `TaxRate`, `AdjustmentsJson` (as correções indicadas,
  em JSON), `SnapshotJson` (o `IrcEstimate` calculado ao guardar). Auditável
  (F2-06A). Tabela `accounting.IrcEstimateFiling`, `AccountId`… índice único
  `(CompanyId, PeriodFrom, PeriodTo)`; migração aditiva
  `20260911151602_IrcEstimateFiling`. No `record-info` (`estimativas-irc`) e no
  `DataDictionaryText`.
- **`IrcEstimateFilingService`** — `GetAsync` lê a linha guardada (se houver) e
  **recalcula sempre** com `IrcEstimateService.BuildAsync` contra o razão
  atual, usando a taxa e as correções guardadas — um lançamento novo no
  período reflete-se na leitura seguinte sem perder o que o utilizador
  indicou; sem linha guardada, devolve `saved:false` e sem estimativa (não
  força um cálculo com valores por omissão). `SaveAsync` faz *upsert* (uma
  linha por período — grava por cima). `ListAsync` — as últimas 24, mais
  recentes primeiro.
- **API:** `GET .../irc-estimate/saved?from=&to=`,
  `PUT .../irc-estimate/saved?from=&to=` (corpo `{taxRate, adjustments}`,
  mesma validação do F2-10A), `GET .../irc-estimate/history`.
- **PWA:** o ecrã `/estimativa-irc` carrega a estimativa guardada do período
  ao mudar de data (preenche taxa e correções); «Guardar estimativa» ao lado
  de «Calcular»; painel «Estimativas guardadas» com «Abrir» por linha,
  à imagem do apuramento de IVA.

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `IrcEstimateFilingServiceTests` — antes de
  guardar, acesso confirmado mas nada guardado; guardar com uma correção e
  21% dá o resultado certo e uma linha na tabela; reler devolve o guardado; um
  lançamento novo no período muda o resultado líquido lido a seguir sem
  perder a taxa guardada; guardar de novo com outra taxa atualiza a mesma
  linha (não duplica); histórico; sem acesso `null`. `SqlServerMigrationTests`
  inclui a migração nova.
- `NexGenAccountingAI.Api.Tests`: `IrcEstimateSavedAndHistoryAreExposedReadOnly` —
  leitura `200`, `PUT` com taxa inválida `400` (sem escrever), sem acesso
  `404`. Sem escrita real no teste de API (base real partilhada — ver a
  nota de memória sobre isso); o ciclo de escrita foi verificado na
  integração e em direto.
- `NexGenAccountingAI.Web`: `tsc`, `eslint` e os oitenta e três testes de interface
  verdes.
- `dotnet build`/`dotnet test` na solução (177 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia, ano de
  2025): `saved` antes de gravar → `false`; `PUT` com uma correção de 1000 €
  e 21% → coleta 2 408 666,60 € (confirmado ao cêntimo contra o cálculo do
  F2-10A); `sqlcmd` confirmou a linha com `CreatedBy` = o utilizador da rota;
  o ecrã `/estimativa-irc` carregou a estimativa guardada automaticamente ao
  abrir no período certo, com o painel «Estimativas guardadas» a mostrá-la;
  linha removida a seguir, tabela confirmada vazia.

## Fora deste incremento

- o mapeamento aos campos numerados do Modelo 22 — precisa da confirmação do
  utilizador sobre a numeração em vigor (F2-09D tem a mesma cautela para o
  IVA).
