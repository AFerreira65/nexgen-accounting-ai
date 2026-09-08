# F2-07 — Motor de mapas e análises configuráveis

Épico da [Fase 2](README.md), pedido pelo utilizador (pontos 5–7 da visão):
impressões e mapas configuráveis pela própria aplicação, à imagem dos IDU do
PHC CS e das **Análises Avançadas** do PHC, com saída para ecrã, impressão,
PDF e (mais tarde) email.

## Contexto

- A plataforma já tem ecrãs de relatório **fixos**: balancete, razão, livro
  diário, demonstração de resultados, balanço, resumo de IVA e contas
  correntes. Cada um exporta CSV e — desde F2-06E — tem folha de impressão.
- Não há forma de o utilizador **definir** um mapa: escolher a origem, as
  colunas, os filtros, os agrupamentos, as ordenações e os totais, guardá-lo
  com um nome e voltar a corrê-lo. É isso que as «Análises Avançadas» do PHC
  fazem.
- Não há forma de **configurar a folha impressa** de um relatório (cabeçalho
  com identificação da empresa e do gabinete, colunas visíveis, orientação) —
  o equivalente aos IDU.
- A auditoria de registo (F2-06A) e o dicionário de dados (F2-06C) dão a base:
  as definições guardadas herdam as colunas de auditoria, e o catálogo de
  campos de cada origem apoia-se no modelo já conhecido.

## Objetivo

- **Análises configuráveis** — o utilizador compõe uma análise sobre uma
  **origem** conhecida (começando pelos movimentos), guarda-a por empresa, e
  corre-a com um período; o resultado vem agrupado, com subtotais e total
  geral, e imprime-se / exporta-se.
- **Modelos de impressão** — por empresa e por relatório, um cabeçalho e um
  conjunto de colunas configuráveis, aplicados à saída impressa dos
  relatórios fixos.
- **Sem SQL livre.** As análises são **guiadas por um catálogo de campos** por
  origem (tipo, se é agrupável, se é agregável); nunca há SQL ad-hoc do
  utilizador. É a decisão registada em
  [`ADR-0005`](../architecture-decisions/ADR-0005-REPORT-ENGINE.md).

Fora do âmbito nesta fase: envio por email (precisa de SMTP e segredos —
fronteira documentada em F2-07E); um desenhador visual de layout pixel-a-pixel
(o modelo de impressão é estrutural, não um canvas); origens além de movimentos
e saldos de conta.

## Desenho

### Domínio / Application

```
Domain/Reporting/
  SavedAnalysis.cs          // definição guardada de uma análise (por empresa)
  ReportTemplate.cs         // configuração de impressão de um relatório (por empresa)
Application/Reporting/
  AnalysisDefinition.cs     // origem + colunas + filtros + agrupamentos + ordenação + agregados
  AnalysisResult.cs         // colunas resolvidas + linhas + grupos com subtotais + total geral + truncado
  AnalysisSource.cs         // enum das origens
  AnalysisFieldCatalog.cs   // por origem, os campos disponíveis (chave, rótulo pt-PT, tipo, agrupável, agregável)
  IAnalysisRunner.cs
```

- `AnalysisDefinition` — `{ Source, Columns: string[], Filters: [{ Field, Operator, Value }],
  GroupBy: string[], Sort: [{ Field, Descending }], Aggregates: [{ Field, Function }],
  IncludeGrandTotal }`. Serializa para JSON na `SavedAnalysis`.
- `AnalysisFieldCatalog` — código, não editável pelo utilizador. Para a origem
  **Movimentos**: data, diário, referência, tipo de documento, nº de documento,
  descrição, estado, origem, conta, nome da conta, descrição da linha, débito,
  crédito, valor (débito − crédito). Cada campo diz o tipo (texto / número /
  data / dinheiro) e se é agrupável / agregável.

### Infrastructure

- Esquema **`reporting`**. `reporting.SavedAnalysis`
  (`Id, CompanyId, Name, Description?, DefinitionJson` + colunas de auditoria de
  F2-06A) e `reporting.ReportTemplate`
  (`Id, CompanyId, ReportKey, HeaderLinesJson, Columns?, Orientation` + auditoria),
  única por `(CompanyId, ReportKey)`. Migração EF Core aditiva.
- `SavedAnalysisService` — CRUD por empresa, valida a definição contra o
  catálogo ao gravar (campos existem, agrupáveis são agrupáveis, etc.).
- `AnalysisRunner` (`Infrastructure.SqlServer`, fronteira `IAnalysisRunner` em
  `Application`) — para a origem Movimentos, constrói a consulta EF a partir da
  definição (filtros traduzidos, ordenação), materializa até um limite
  (**5 000 linhas**, com `Truncated`), e calcula em memória os subtotais por
  grupo e o total geral. As análises são mapas de conferência — não alteram
  nada.

### API

- `GET .../companies/{companyId}/analyses` · `POST` · `PUT .../analyses/{id}` ·
  `DELETE .../analyses/{id}` — CRUD das definições guardadas.
- `GET .../analyses/catalog` — o catálogo de campos por origem (para o
  construtor da PWA).
- `POST .../analyses/run?from=&to=` — corre uma definição **em linha**
  (pré-visualização do construtor).
- `GET .../analyses/{id}/run?from=&to=` — corre uma análise guardada.
- `GET .../companies/{companyId}/report-templates/{reportKey}` · `PUT` — o
  modelo de impressão de um relatório.

### PWA

- Ecrã **«Análises»** (`/analises`) — lista as análises guardadas; um construtor
  (origem → colunas → filtros → agrupamentos → ordenação → totais) com
  pré-visualização; o resultado é uma tabela agrupada com subtotais e total
  geral, com impressão (folha `@media print` como em F2-06E) e exportação CSV.
- Nos relatórios fixos, um painel **«Configurar impressão»** (cabeçalho:
  identificação do gabinete e da empresa, colunas visíveis, orientação),
  guardado por empresa, aplicado à folha impressa.
- Entrada no menu lateral e no menu «Opções».

## Sub-incrementos

### F2-07A — Definições guardadas e catálogo de campos

- **Regras:** esquema `reporting`; `SavedAnalysis` + tabela + migração;
  `AnalysisDefinition`/`AnalysisSource`/`AnalysisFieldCatalog` (origem
  Movimentos); `SavedAnalysisService` (CRUD + validação contra o catálogo);
  `GET/POST/PUT/DELETE .../analyses` e `GET .../analyses/catalog`. Auditável
  (F2-06A) e no `record-info` (F2-06B).
- **Validação:** testes do serviço (guardar/validar/rejeitar campo
  desconhecido); teste de API (CRUD read-only + catálogo); integração para a
  escrita.

### F2-07B — Execução das análises (origem Movimentos)

- **Regras:** `AnalysisRunner` + `POST .../analyses/run` e
  `GET .../analyses/{id}/run`; filtros, ordenação, agrupamentos com subtotais,
  total geral, limite de 5 000 linhas com `Truncated`.
- **Validação:** testes do runner com cenários construídos (filtro, grupo com
  subtotal, agregado, truncagem); teste de API.

### F2-07C — Ecrã «Análises» na PWA

- **Regras:** lista + construtor + pré-visualização + tabela agrupada +
  impressão + CSV; rótulos pt-PT; ligação no menu.
- **Validação:** testes de interface do construtor e da execução.

### F2-07D — Modelos de impressão dos relatórios fixos

- **Regras:** `ReportTemplate` + tabela + migração; `GET/PUT
  .../report-templates/{reportKey}`; painel «Configurar impressão» nos
  relatórios; cabeçalho aplicado à folha `@media print`.
- **Validação:** testes de interface; teste de API.

### F2-07E — (opcional) Segunda origem e fronteira do email

- **Regras:** origem `SaldosDeConta` no catálogo e no runner; documento de
  fronteira sobre o envio por email (SMTP/segredos — fora do âmbito local).
- **Validação:** testes do runner para a nova origem.

## Decisões em aberto

- **Origens a seguir a Movimentos** — saldos de conta, contas correntes,
  compras e-Fatura, resultado do cruzamento. Uma de cada vez.
- **PDF** — `window.print()` do navegador (sem dependência) vs. render no
  servidor. Proposta: navegador, como em F2-06E.
- **Partilha de análises entre empresas do mesmo gabinete** — por agora são
  por empresa; a partilha ao nível do gabinete fica para decisão posterior.

## Referências

- PHC CS «Análises Avançadas» e IDU — a referência funcional.
- `JournalReportService`, `AccountLedgerService` — as consultas de relatório
  existentes a reaproveitar nas origens.
- F2-06A (auditoria), F2-06C (dicionário de dados) — a base.
