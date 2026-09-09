# ADR-0006 — Relatórios de gestão assistidos por IA: fornecedor e fronteiras

- Estado: proposto para a F2-05
- Data: 2026-09-09

## Contexto

O ponto 8 da visão da plataforma pede relatórios de gestão assistidos por IA,
com um interruptor de configuração, relatórios **pré-definidos** e **a pedido**,
sempre sobre dados já reconciliados e com ligação ao lançamento/conta de origem
(princípios da [Fase 2](../phase-2/README.md) e da
[ADR-0005](ADR-0005-REPORT-ENGINE.md)). O utilizador pediu explicitamente uma
recomendação de fornecedor entre Gemini, ChatGPT e Claude.

Os riscos: (1) dar a um modelo acesso a SQL livre ou ao razão inteiro;
(2) apresentar texto gerado como se fosse uma peça contabilística revista;
(3) introduzir uma dependência grande num código que hoje só depende de EF Core
e do framework ASP.NET, com um gate de segurança que falha a qualquer pacote
vulnerável (`scripts/verify-security.ps1`); (4) segredos (a chave da API) no
repositório.

## Decisão

### Fornecedor — Claude

O fornecedor é o **Claude** (API da Anthropic). Face às alternativas para este
efeito:

- **rigor com dados tabulares/financeiros e instruções longas** — o dossiê é uma
  estrutura densa (balancete, homólogo, rácios, cruzamento) com um enunciado
  extenso de regras pt-PT; é o cenário em que o Claude é mais forte e o Gemini
  mais fraco;
- **SDK .NET oficial** (`Anthropic`), com saída estruturada (`output_config`) e
  *adaptive thinking*, o que encaixa no modelo síncrono da API;
- **fronteira de dados clara** — a chamada é *stateless*, sem *fine-tuning* nem
  retenção do nosso lado.

O ChatGPT seria equivalente em qualidade; a escolha do Claude é também de
coerência (um só fornecedor, um só SDK). O `model` fica **configurável**
(`Ai:Model`, por omissão `claude-opus-5`); trocar de fornecedor no futuro é
trocar a implementação de uma interface (abaixo), não reescrever o épico.

### Isolamento do SDK — projeto `AccountingAI.Infrastructure.Ai`

A dependência do SDK vive **só** num projeto novo,
`AccountingAI.Infrastructure.Ai`, à imagem do que `Infrastructure.Files` faz
com a leitura de ficheiros. O `Application` define a fronteira
`IManagementReportNarrator`; `Infrastructure.Ai` implementa-a com o SDK; a API
orquestra. `Infrastructure.SqlServer` **não** conhece o SDK. O teste de
arquitetura passa a listar o projeto e a permitir a referência da API.

### O modelo nunca toca no SQL nem no razão

Um serviço determinístico — `ManagementReportDataService`
(`Infrastructure.SqlServer`) — monta um **dossiê** limitado e estruturado a
partir dos serviços de leitura que já existem
(`AccountLedgerService.TrialBalanceAsync`, `JournalReportService`,
`ICrossCheckService`):

- balancete do período **e do período homólogo** (variação por conta/classe);
- resumo de resultados por natureza e de posição financeira (as mesmas
  contas 1–8 que os ecrãs fixos derivam do balancete);
- um punhado de **rácios** (margem, liquidez geral, fundo de maneio, peso de
  gastos com pessoal, prazo médio, …), cada um com a fórmula e as contas usadas;
- o resumo do **cruzamento** (compras e-Fatura × contabilidade, divergências);
- os **maiores movimentos** do período (topo N por valor absoluto).

Cada número do dossiê carrega a **proveniência** (o código de conta ou a
referência de lançamento de onde saiu). Há limites rígidos: no máximo algumas
centenas de linhas de balancete (as classes agregam o resto), N=25 movimentos,
listas de divergência limitadas. O dossiê é serializado e **guardado** com o
relatório — o resultado é reproduzível e inspecionável.

### Dois modos

- **Pré-definidos** — tipos nomeados (`sintese-mensal`, `analise-rentabilidade`,
  `posicao-financeira`, `revisao-iva`, `alertas-de-fecho`), cada um com um
  enunciado de sistema fixo (pt-PT, em código) e as secções do dossiê que usa.
- **A pedido** — o utilizador escreve uma pergunta; o mesmo dossiê, com um
  enunciado genérico de «responde só a partir destes dados; se não estiver no
  dossiê, di-lo».

### Saída estruturada e aviso

A resposta é **estruturada** (contrato `ManagementReportResult`):
`{ resumo, secções:[{título, corpo, referências:[{tipo: conta|movimento, código}]}],
alertas:[…], indicadores:[{nome, valor, comentário}] }`. A PWA converte as
`referências` em ligações (`/movimentos/{ref}`, `/livro-razao?conta=`). É sempre
acrescentado, do nosso lado (não do modelo), o aviso: **«Texto gerado por IA a
partir de dados reconciliados; é um apoio à análise e não substitui a revisão
nem as peças contabilísticas oficiais.»**

### Interruptor e degradação

`reporting.CompanyAiSettings` por empresa (`Enabled`, `AutoMensal`). Com a
funcionalidade desligada, ou sem `Ai:ApiKey` configurada, os *endpoints* de
geração respondem `409` e o ecrã mostra só o interruptor e o histórico. O
`NullManagementReportNarrator` (registado quando não há chave) devolve um
resultado explícito «IA não configurada» — nada rebenta.

### Segredos

`Ai:ApiKey` só de *user-secrets* / variável de ambiente
(`ANTHROPIC_API_KEY`). Nunca em `appsettings*.json`, nunca em log, nunca no
dossiê nem no resultado guardado. O `.gitleaks.toml` não precisa de exceções.

### Custo

`max_tokens` limitado; *adaptive thinking*; o dossiê é limitado à cabeça. Cada
geração é um clique explícito do utilizador, com a chave da própria organização.
O `AutoMensal` (F2-05E, opcional) corre no `Worker`, uma vez por período.

## Consequências

- O utilizador ganha relatórios de gestão redigidos, ancorados nos dados
  reconciliados e ligados à origem, sem que a plataforma exponha o esquema nem
  SQL ao modelo.
- Uma dependência nova (o SDK `Anthropic`), confinada a um projeto; se o gate de
  vulnerabilidades vier a acusá-la, o raio de ação é um projeto e a fronteira
  `IManagementReportNarrator` permite uma implementação alternativa (HTTP direto)
  sem tocar no resto.
- Há um custo de manutenção do dossiê: cada rácio ou secção nova é código no
  `ManagementReportDataService`. Fica ao lado do catálogo de tipos para não
  divergirem.
- Os relatórios são reproduzíveis (o dossiê é guardado) e auditáveis (colunas de
  F2-06A), mas o texto é de um modelo — o aviso e a ausência de qualquer
  automatismo de submissão são deliberados.
- Assenta na ADR-0005 (o motor de mapas) e na F2-03 (o cruzamento) em vez de
  inventar a sua própria recolha de dados.
