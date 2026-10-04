# F2-06 — Dicionário de dados e «Sobre o Registo»

Épico transversal da [Fase 2](README.md), pedido pelo utilizador. Dá à
plataforma duas capacidades à imagem do PHC CS:

1. Um **dicionário de dados** por tabela — a estrutura da base de dados
   (esquema, colunas, tipos, chaves, índices) com uma descrição em pt-PT,
   visível no ecrã e imprimível / exportável para PDF.
2. Um menu **«Opções»** em **todos os ecrãs**, com «Dicionário de dados» (a
   estrutura da tabela do ecrã em que se está) e «Sobre o Registo» (quem criou
   e quem alterou, e quando, o registo em foco).

## Contexto

- O domínio são entidades ricas (`sealed class`, setters privados, fábricas
  `Create`) em `NexGenAccountingAI.Domain`; o EF Core mapeia-as para os esquemas
  `organization` / `identity` / `accounting` / `infrastructure`.
- **Nenhuma entidade tem hoje colunas de auditoria** de quem/quando. Só
  `EFaturaPurchase` e `SaftValidationRun` guardam um `CreatedAtUtc`, e nenhuma
  guarda o autor.
- Não há noção de «utilizador atual» na stack (a API de desenvolvimento recebe
  o `userId` na rota; não há autenticação).
- A PWA tem uma barra de topo partilhada (`AppLayout`) onde já vivem o seletor
  de empresa, o seletor de exercício e o sino das Tarefas — o sítio natural
  para o menu «Opções».
- Já existe um caminho de exportação CSV (`src/app/csv.ts`); não há PDF.

## Objetivo

- **Auditoria de registo** — cada registo editável pela plataforma passa a
  saber quem o criou e quem o alterou pela última vez, e quando.
- **Dicionário de dados** — uma descrição navegável e imprimível da estrutura
  de cada tabela, gerada do modelo do EF Core e enriquecida com texto pt-PT.
- **«Opções» em todo o lado** — um único componente na barra de topo, ciente do
  ecrã atual (→ tabela) e do registo em foco (→ chave), com as duas ações.

Fora do âmbito: um editor do dicionário na interface (o texto pt-PT é mantido
no código); histórico de alterações campo a campo (isto é «quem/quando», não
uma trilha de auditoria completa); autenticação real.

## Desenho

### Auditoria (`Domain` intacto; EF + interceptor)

- Marcador `IAuditable` (vazio) em `Application` (ou `Domain`), implementado
  pelas entidades editáveis: `JournalEntry`, `LedgerAccount`,
  `AccountingJournal`, `AccountingDocumentType`, `DocumentNumberSeries`,
  `Company`, `UserAccount`, `CompanyUser`, `UserCompanySettings`,
  `EFaturaPurchase`.
- **Propriedades-sombra** (`CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`,
  `ModifiedBy`) configuradas no `OnModelCreating` para cada tipo `IAuditable` —
  o domínio não muda. `CreatedAtUtc` NOT NULL com `SYSUTCDATETIME()` por
  omissão; as restantes anuláveis (registos históricos sem autor conhecido).
- `AuditStampingInterceptor : SaveChangesInterceptor` — em `Added` carimba
  `CreatedAtUtc`/`CreatedBy`; em `Modified` carimba `ModifiedAtUtc`/
  `ModifiedBy`. O «quem» vem de `ICurrentUserAccessor` (scoped, mutável); um
  filtro de endpoint da API preenche-o com o `userId` da rota. Sem utilizador
  conhecido, o carimbo do «quem» fica nulo (o «quando» é sempre gravado).
- Migração aditiva `AuditColumns` (uma, todas as tabelas).

### `record-info` (API)

- Registo estático `RecordInfoRegistry`: nome lógico → (tipo de entidade,
  seletor de chave, se é âmbito de empresa). Ex.: `movimentos` →
  (`JournalEntry`, `Reference`, sim), `contas` → (`LedgerAccount`, `Code`,
  sim), `empresas` → (`Company`, `Id`, não).
- `GET .../companies/{companyId}/record-info?table=&id=` → `{ table, id,
  createdBy: { id, name } | null, createdAtUtc, modifiedBy | null,
  modifiedAtUtc }`. Lê as propriedades-sombra por projeção e resolve os nomes
  em `UserAccounts`. `404` se a tabela não está no registo, o registo não
  existe, ou o utilizador não tem acesso à empresa.

### Dicionário de dados (API)

- `DataDictionaryService` — percorre `context.Model.GetEntityTypes()` e produz,
  por tabela: esquema, nome, e por coluna: nome, tipo .NET e SQL, anulável,
  comprimento máximo, precisão/escala, se é PK / FK (→ tabela alvo) / única /
  indexada. Junta um mapa pt-PT mantido à mão (`DataDictionaryText`,
  `tabela` → propósito, `tabela.coluna` → descrição). Colunas sem texto saem
  com descrição vazia (e um teste garante cobertura mínima).
- `GET .../data-dictionary` → tabelas (esquema, nome, propósito, nº de
  colunas). `GET .../data-dictionary/{schema}/{table}` → detalhe das colunas.
  Escopo de utilizador (não de empresa — a estrutura é global).

### PWA

- `RecordContext` (React) — um ecrã de detalhe publica `{ table, id, label }`;
  limpa ao sair. As listas podem publicar a linha selecionada (fase 2 do
  épico; ao início chega o ecrã de detalhe).
- `screenTable.ts` — `pathname` → nome lógico de tabela (para o menu saber a
  tabela mesmo sem registo em foco).
- `OptionsMenu` — na barra de topo do `AppLayout`, em todos os ecrãs. Itens:
  - **Dicionário de dados** → `/dicionario-de-dados?tabela=<schema>/<name>`
    (abre já na tabela do ecrã; a página lista todas e detalha uma).
  - **Sobre o Registo** → diálogo com quem criou / quem alterou (chama
    `record-info`); desativado quando o ecrã não tem registo em foco.
- `/dicionario-de-dados` (`DataDictionaryPage`) — lista de tabelas + detalhe de
  uma, com **«Imprimir / PDF»** (`window.print()` + folha de estilo de
  impressão que isola a tabela; o navegador oferece «Guardar como PDF») e
  exportação CSV do detalhe.

## Sub-incrementos

### F2-06A — Colunas de auditoria e carimbo

- **Regras:** `IAuditable`, propriedades-sombra, `AuditStampingInterceptor`,
  `ICurrentUserAccessor` + filtro de endpoint, migração `AuditColumns`.
- **Validação:** integração — criar carimba `CreatedBy`/`CreatedAtUtc`;
  alterar carimba `ModifiedBy`/`ModifiedAtUtc`; a migração aplica-se duas
  vezes. Teste de API a confirmar que uma escrita existente não parte.

### F2-06B — Endpoint `record-info`

- **Regras:** `RecordInfoRegistry` + `GET .../record-info`.
- **Validação:** teste de API (registo conhecido devolve autor/datas;
  tabela desconhecida `404`; empresa sem acesso `404`).

### F2-06C — Serviço e endpoints do dicionário de dados

- **Regras:** `DataDictionaryService` (modelo EF + `DataDictionaryText`) +
  `GET .../data-dictionary` e `.../data-dictionary/{schema}/{table}`.
- **Validação:** testes do serviço (todas as tabelas mapeadas têm propósito;
  as colunas trazem tipo e chaves; cobertura mínima de descrições) + teste de
  API.

### F2-06D — Menu «Opções», `RecordContext` e ecrãs

- **Regras:** `OptionsMenu` na barra de topo, `RecordContext`, `screenTable.ts`,
  diálogo «Sobre o Registo», página `/dicionario-de-dados`; rótulos pt-PT.
- **Validação:** testes de interface (o menu aparece em vários ecrãs; «Sobre o
  Registo» desativado sem registo; a página do dicionário lista e detalha).

### F2-06E — Impressão / PDF

- **Regras:** folha de estilo de impressão e botão «Imprimir / PDF» na página
  do dicionário e no diálogo «Sobre o Registo»; exportação CSV do detalhe.
- **Validação:** teste de interface do botão; verificação manual da impressão.

## Decisões em aberto

- **Propriedades-sombra vs. reais** — a proposta usa sombra para não mexer no
  domínio; se mais tarde for preciso mostrar «quem/quando» no próprio agregado,
  promove-se a reais numa migração sem perda.
- **«Sobre o Registo» nas listas** — ao início só nos ecrãs de detalhe; as
  listas entram quando houver selecção de linha persistente.
- **Fonte do texto pt-PT** — mantido no código (`DataDictionaryText`); um
  editor na interface fica para um épico próprio se for pedido.
- **PDF** — `window.print()` + folha de impressão (sem dependência nova) vs.
  uma biblioteca de PDF. Proposta: `window.print()`.

## Referências

- `src/NexGenAccountingAI.Infrastructure.SqlServer/Persistence/AccountingDbContext.cs`
  e `Persistence/Configurations/` — o modelo a percorrer.
- `src/NexGenAccountingAI.Web/src/app/AppLayout.tsx` — a barra de topo.
- `src/NexGenAccountingAI.Web/src/app/csv.ts` — o padrão de exportação.
- PHC CS «Dicionário de Dados» — a referência funcional.
