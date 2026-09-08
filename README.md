# Accounting AI

Monitor Windows em C#/.NET para leitura e análise de ficheiros SAF-T (PT).

## Nova plataforma contabilística — F2-03B ecrã de compras e-Fatura na PWA

Sobre a fundação F1-01 foi criada a primeira experiência contabilística local:

- monólito modular .NET 10 (`Domain`, `Application`, `Infrastructure.SqlServer`, `Api` e `Worker`);
- PWA React/TypeScript;
- SQL Server com migrações versionadas;
- health checks, correlação e logs estruturados;
- cadastro base de gabinete e empresa em SQL Server local;
- base `accountingAI` em SQL Server Express, criada e atualizada por migrações no arranque;
- utilizadores associados a uma ou mais empresas, com função e preferências próprias em cada empresa;
- seletor de empresa ligado à API e ecrã para editar preferências próprias do utilizador na empresa ativa (idioma, fuso, tema, densidade, linhas por página e tamanho do texto); uma empresa nova ainda sem preferências gravadas abre com as preferências por omissão em vez de deixar o ecrã em branco; seletor de exercício na barra de topo que grava a preferência e acerta logo os atalhos de período e os valores por omissão de todos os ecrãs; os menus da barra de topo fecham ao clicar fora ou com Escape; pesquisa rápida na barra de topo (Ctrl+K) que salta para uma conta, diário ou movimento a partir dos dados já carregados;
- plano de contas, diários, movimentos e linhas contabilísticas persistidos no schema `accounting`;
- norma contabilística escolhida por empresa, com plano de contas base versionado onde as contas de 1 e 2 dígitos são de integração e as de 3+ dígitos de movimento (exceto 56 e 81);
- provisionamento transacional e idempotente de contas, diários e tipos de documento ao criar a empresa;
- ecrãs pesquisáveis de plano e abertura de contas, diários, documentos contabilísticos e taxonomias, com o plano de contas e as taxonomias paginados pelo tamanho de página das preferências da empresa;
- criação de contas, diários e tipos de documento próprios da empresa, renomeação e ativação/desativação, com guardas contra desativar registos com movimentos, subcontas ativas ou usados como predefinidos;
- atribuição, limpeza e aplicação em bloco do código de taxonomia nas contas de movimento, com mapa de revisão de cobertura;
- catálogo completo de taxonomias por referencial (Taxonomia S e M, transcritas da Portaria 302/2016), com classificação automática apenas das contas inequívocas;
- controlo de cobertura `TaxonomyReference`/`TaxonomyCode` por norma e por conta de movimento;
- dados sintéticos idempotentes e distintos para as empresas do Continente e dos Açores;
- leitura do cabeçalho de um ficheiro SAF-T (PT) na nova plataforma e correspondência da empresa pelo NIF;
- criação da empresa a partir do SAF-T quando o NIF não corresponde a nenhuma, com provisionamento dos cadastros na mesma transação, recusa de duplicados e o exercício do ficheiro definido logo como o exercício de quem a criou; criada a empresa, o ecrã seleciona-a e relê o ficheiro em memória, seguindo direto para importar o plano de contas e os movimentos sem recarregar;
- leitura do plano geral de contas do SAF-T (`GeneralLedgerAccounts`), pré-visualização no ecrã de importação das contas que já existem e das que seriam novas, e criação das novas na empresa (ligadas à conta-mãe pelo prefixo, integração para 1-2 dígitos e movimento para 3+); os saldos de abertura das contas de movimento entram como um lançamento no diário de abertura a 1 de janeiro, uma vez por exercício;
- pré-visualização dos movimentos do SAF-T (`GeneralLedgerEntries`, em streaming): contagem de transações, totais declarados e equilíbrio, período e diários com indicação de quais já existem na empresa;
- importação dos movimentos do SAF-T: cria os lançamentos com as linhas nos diários certos (criando os que faltam), usa o `TransactionID` como referência (reimportar não duplica) e salta, com o motivo, as transações inválidas ou de exercícios apurados/bloqueados; no fim, reconcilia os débitos e créditos da empresa no período do ficheiro com os totais declarados no SAF-T; uma análise prévia (sem escrever) prevê o que a importação faria — quantos lançamentos, que diários novos, quais saltados (linha a linha até 50, resumidos por motivo acima disso) e se os totais reconciliariam; importar o plano de contas ou os movimentos marca também o exercício do ficheiro como o exercício do utilizador nesta empresa, para os atalhos de período não caírem no ano corrente do sistema por a empresa ainda não ter nenhum exercício definido;
- ecrã de organização para criar e editar empresas e utilizadores e gerir associações; a empresa criada fica associada ao utilizador atual e aparece de imediato no seletor de empresa; inclui uma data de bloqueio de lançamentos por empresa que recusa registar, corrigir ou anular movimentos até essa data (inclusive), sem afetar o apuramento e a abertura, com aviso no editor de movimentos quando a data cai no período bloqueado;
- ecrã de lançamento de movimentos com linhas repetíveis compactas, totais e validação de equilíbrio, campo de conta com sugestões de contas de movimento e recusa de contas de integração, navegação por Enter entre campos (e criação de linha no último campo da última linha), filtros por pesquisa, diário, estado, origem (manual, importado do SAF-T, abertura, apuramento) e intervalo de datas (a arrancar no exercício da empresa, com atalhos de mês, trimestre e ano e «Limpar datas» para abrir tudo) na lista, paginada pelo tamanho de página das preferências da empresa, exportação CSV do que está filtrado (a lista inteira, não só a página visível), e ações para conferir, reabrir (devolve um lançamento conferido a "por conferir" para poder ser corrigido, exceto em exercício apurado, período bloqueado ou lançamentos de apuramento/abertura), corrigir, anular e duplicar (novo lançamento pré-preenchido a partir de um existente); campo de observações internas por lançamento, editável e visível no detalhe do movimento, assinalado na lista de movimentos e na razão de conta e pesquisável na lista;
- balancete, razão de conta e livro diário, filtrados por empresa, diário, tipo de documento, exercício, intervalo de datas e (no livro diário e na razão) conta movimentada e origem, sem os lançamentos anulados, com saldo de abertura por conta no balancete e na razão quando há data inicial, atalhos de período (mês, trimestre, ano) nos relatórios, razão das contas de integração a somar as subcontas de movimento, agrupamento opcional do balancete por classe SNC ou por código de taxonomia com subtotais, comparação opcional do balancete, da demonstração de resultados e do balanço com o período homólogo, e exportação CSV do que está no ecrã; na razão, o filtro de origem só restringe as linhas mostradas — o saldo de abertura e o saldo atual continuam a somar todas as origens; a razão de conta, o livro diário e o balancete são paginados pelo tamanho de página das preferências da empresa, mantendo os totais e saldos do conjunto filtrado inteiro (o balancete agrupado por classe ou taxonomia, e o balanço, mostram só os subtotais quando há contas a mais; no balancete, cada grupo pode ser expandido para ver as suas contas, paginadas dentro do grupo); a lista de movimentos, a razão de conta, a demonstração de resultados e o resumo de IVA arrancam no exercício da empresa (como o livro diário), com «Limpar datas» para ver tudo;
- numeração dos lançamentos por tipo de documento, além da numeração por diário, com o tipo persistido no lançamento e filtro de movimentos por tipo, e um contador persistido por empresa, série e exercício com largura de preenchimento configurável;
- emissão simultânea protegida no SQL Server, sem referências duplicadas, e ecrã de controlo das séries com último número, próximo número e avanço seguro sem recuos;
- demonstração de resultados sintética por naturezas (classe 7 menos classe 6, por subclasse, com resultado líquido do período e comparação opcional com o período homólogo) e balanço sintético (ativo, passivo e capital próprio por subclasse, com o controlo Ativo = Passivo + Capital próprio), ambos a partir do balancete; resumo de IVA por período (contas 24.3: liquidado, dedutível e posição a entregar/a recuperar) e contas correntes de clientes (21) e fornecedores (22) com data de corte e cada painel paginado, também a partir do balancete;
- apuramento de resultados do exercício (fecha as classes 6 e 7 contra a conta 81) e abertura do exercício seguinte (transporta os saldos de balanço e leva o resultado a 56), cada um uma vez por exercício e no respetivo diário, com reabertura (anulação do lançamento de fecho) das camadas de dentro para fora, e recusa de lançar, corrigir ou anular movimentos num exercício apurado, com aviso no editor de movimentos e botões de corrigir/anular desativados na lista de movimentos para os lançamentos de um exercício apurado ou período bloqueado;
- dashboard com resultado, total do ativo e posição financeira (caixa e bancos, saldo de clientes e de fornecedores, posição de IVA, com ligação ao ecrã respetivo) da empresa, além dos movimentos; razão de conta (com saldo de abertura e movimentos da API) e diário alimentados pela API e filtrados pela empresa ativa; uma data de referência opcional (com os mesmos atalhos de período dos relatórios, guardada por empresa no navegador) recua esses indicadores de posição e o resultado para o que existia até essa data, sem afetar as contagens de movimentos e por conferir;
- ecrã de Tarefas com os movimentos por conferir, as contas de movimento por classificar e, quando há, a posição de IVA a entregar da empresa ativa, com ligação direta a cada um; o lembrete de IVA pode ser dispensado até a posição mudar, guardado por empresa no navegador; o sino da barra de topo abre as Tarefas e acende quando há movimentos por conferir ou contas por classificar;
- painel de trabalho apelativo e responsivo;
- navegação contextual entre movimento, conta e diário, com o detalhe da conta e do diário paginados; o detalhe do movimento mostra a origem, o tipo de documento e o estado, permite copiar a ligação e conferir, reabrir, corrigir, duplicar ou anular o lançamento (com as mesmas guardas de exercício apurado e período bloqueado da lista, no mesmo editor);
- testes de arquitetura, domínio, API, migrações e frontend.

Esta evolução não altera o comportamento do monitor WinForms descrito abaixo. Os movimentos sintéticos de desenvolvimento continuam presentes; além disso, a aplicação regista, confere, corrige e anula lançamentos. O plano de contas, os diários, os tipos de documento e as taxonomias já são editáveis. A importação de SAF-T lê e valida o cabeçalho, faz a correspondência da empresa e, quando o NIF não corresponde a nenhuma, cria a empresa e provisiona os cadastros; lê o plano geral de contas do ficheiro, mostra-o comparado com o da empresa, cria as contas novas e lança os saldos de abertura, e resume, analisa, importa e reconcilia os movimentos (`GeneralLedgerEntries`) com os totais declarados. Consulte [F1-02A](docs/phase-1/F1-02A-LOCAL-EXPERIENCE.md), [F1-02B](docs/phase-1/F1-02B-ENGINEERING-GATES.md), [F1-02C](docs/phase-1/F1-02C-MULTI-COMPANY-DATABASE.md), [F1-03A](docs/phase-1/F1-03A-WORK-CONTEXT.md), [F1-03B](docs/phase-1/F1-03B-ACCOUNTING-TEST-DATA.md), [F1-03C](docs/phase-1/F1-03C-ACCOUNTING-TEMPLATES.md), [F1-04A](docs/phase-1/F1-04A-ACCOUNTING-MASTER-DATA.md), [F1-04B](docs/phase-1/F1-04B-ACCOUNTING-MASTER-DATA-MAINTENANCE.md), [F1-05A](docs/phase-1/F1-05A-SAFT-HEADER-INSPECTION.md), [F1-05B](docs/phase-1/F1-05B-SAFT-COMPANY-CREATION.md), [F1-06A](docs/phase-1/F1-06A-JOURNAL-DOCUMENT-MAINTENANCE.md), [F1-06B](docs/phase-1/F1-06B-TAXONOMY-CATALOG.md), [F1-06C](docs/phase-1/F1-06C-ORGANIZATION-ADMIN.md), [F1-07](docs/phase-1/F1-07-MOVEMENT-POSTING.md), [F1-08](docs/phase-1/F1-08-MOVEMENT-REVIEW-VOID.md), [F1-09](docs/phase-1/F1-09-TRIAL-BALANCE-LEDGER.md), [F1-10](docs/phase-1/F1-10-DOCUMENT-TYPE-NUMBERING.md), [F1-11](docs/phase-1/F1-11-JOURNAL-ENTRY-DOCUMENT-TYPE.md), [F1-12](docs/phase-1/F1-12-JOURNAL-REPORT.md), [F1-13](docs/phase-1/F1-13-PERIOD-REPORTS.md), [F1-14](docs/phase-1/F1-14-ACCOUNT-LEDGER-SCREEN.md), [F1-15](docs/phase-1/F1-15-REPORT-CSV-EXPORT.md), [F1-16](docs/phase-1/F1-16-TRIAL-BALANCE-OPENING.md), [F1-17](docs/phase-1/F1-17-DOCUMENT-NUMBER-SERIES.md), [F1-18](docs/phase-1/F1-18-DOCUMENT-SERIES-CONTROL.md), [F1-19](docs/phase-1/F1-19-TRIAL-BALANCE-BY-CLASS.md), [F1-20](docs/phase-1/F1-20-INCOME-STATEMENT.md), [F1-21](docs/phase-1/F1-21-BALANCE-SHEET.md), [F1-22](docs/phase-1/F1-22-YEAR-END-CLOSING.md), [F1-23](docs/phase-1/F1-23-YEAR-OPENING.md), [F1-24](docs/phase-1/F1-24-MOVEMENT-KEYBOARD-FLOW.md), [F1-25](docs/phase-1/F1-25-YEAR-REOPEN.md), [F1-26](docs/phase-1/F1-26-CLOSED-YEAR-LOCK.md), [F1-27](docs/phase-1/F1-27-CLOSED-YEAR-VOID-LOCK.md), [F1-28](docs/phase-1/F1-28-CLOSED-YEAR-EDITOR-HINT.md), [F1-29](docs/phase-1/F1-29-INCOME-STATEMENT-PRIOR-PERIOD.md), [F1-30](docs/phase-1/F1-30-TRIAL-BALANCE-PRIOR-PERIOD.md), [F1-31](docs/phase-1/F1-31-BALANCE-SHEET-PRIOR-PERIOD.md), [F1-32](docs/phase-1/F1-32-DASHBOARD-ACCOUNTING-KPIS.md), [F1-33](docs/phase-1/F1-33-DOCUMENT-SERIES-WIDTH.md), [F1-34](docs/phase-1/F1-34-MOVEMENT-LIST-FILTERS.md), [F1-35](docs/phase-1/F1-35-COMPANY-POSTING-LOCK-DATE.md), [F1-36](docs/phase-1/F1-36-POSTING-LOCK-EDITOR-HINT.md), [F1-37](docs/phase-1/F1-37-MOVEMENT-LIST-PERIOD-GUARD.md), [F1-38](docs/phase-1/F1-38-MOVEMENT-NOTES.md), [F1-39](docs/phase-1/F1-39-NOTES-IN-LIST-AND-LEDGER.md), [F1-40](docs/phase-1/F1-40-JOURNAL-REPORT-BY-ACCOUNT.md), [F1-41](docs/phase-1/F1-41-DUPLICATE-MOVEMENT.md), [F1-42](docs/phase-1/F1-42-SAFT-ACCOUNTS-PREVIEW.md), [F1-43](docs/phase-1/F1-43-SAFT-ACCOUNTS-PREVIEW-SCREEN.md), [F1-44](docs/phase-1/F1-44-MOVEMENT-EDITOR-ACCOUNT-FIELD.md), [F1-45](docs/phase-1/F1-45-INTERFACE-TEXT-SIZE.md), [F1-46](docs/phase-1/F1-46-AGGREGATE-ACCOUNT-LEDGER.md), [F1-47](docs/phase-1/F1-47-SHORT-ACCOUNT-CODES-INTEGRATION.md), [F1-48](docs/phase-1/F1-48-SAFT-ACCOUNTS-IMPORT.md), [F1-49](docs/phase-1/F1-49-REPORT-PERIOD-SHORTCUTS.md), [F1-50](docs/phase-1/F1-50-NEW-COMPANY-IN-SWITCHER.md), [F1-51](docs/phase-1/F1-51-SAFT-ENTRIES-PREVIEW.md), [F1-52](docs/phase-1/F1-52-EMPTY-COMPANY-SETTINGS.md), [F1-53](docs/phase-1/F1-53-SAFT-ENTRIES-IMPORT.md), [F1-54](docs/phase-1/F1-54-SAFT-IMPORT-RECONCILIATION.md), [F1-55](docs/phase-1/F1-55-SAFT-ENTRIES-DRY-RUN.md), [F1-56](docs/phase-1/F1-56-SAFT-OPENING-BALANCES.md), [F1-57](docs/phase-1/F1-57-MOVEMENT-LIST-DATE-RANGE.md), [F1-58](docs/phase-1/F1-58-MOVEMENT-LIST-CSV-EXPORT.md), [F1-59](docs/phase-1/F1-59-TRIAL-BALANCE-BY-TAXONOMY.md), [F1-60](docs/phase-1/F1-60-REOPEN-REVIEWED-MOVEMENT.md), [F1-61](docs/phase-1/F1-61-VAT-SUMMARY.md), [F1-62](docs/phase-1/F1-62-PARTY-BALANCES.md), [F1-63](docs/phase-1/F1-63-ENTRY-SOURCE.md), [F1-64](docs/phase-1/F1-64-MOVEMENT-DETAIL-POLISH.md), [F1-65](docs/phase-1/F1-65-MOVEMENT-DETAIL-ACTIONS.md), [F1-66](docs/phase-1/F1-66-JOURNAL-REPORT-BY-SOURCE.md), [F1-67](docs/phase-1/F1-67-DASHBOARD-FINANCIAL-POSITION.md), [F1-68](docs/phase-1/F1-68-MOVEMENT-DETAIL-EDITOR.md), [F1-69](docs/phase-1/F1-69-DASHBOARD-VAT-POSITION.md), [F1-70](docs/phase-1/F1-70-TASKS-SCREEN.md), [F1-71](docs/phase-1/F1-71-ACCOUNT-LEDGER-BY-SOURCE.md), [F1-72](docs/phase-1/F1-72-DASHBOARD-PERIOD-FILTER.md), [F1-73](docs/phase-1/F1-73-TASKS-VAT-REMINDER.md), [F1-74](docs/phase-1/F1-74-TASKS-VAT-REMINDER-DISMISS.md), [F1-75](docs/phase-1/F1-75-MOVEMENTS-LIST-PAGINATION.md), [F1-76](docs/phase-1/F1-76-SAFT-IMPORT-FISCAL-YEAR.md), [F1-77](docs/phase-1/F1-77-SAFT-COMPANY-CREATION-FISCAL-YEAR.md), [F1-78](docs/phase-1/F1-78-LEDGER-JOURNAL-REPORT-PAGINATION.md), [F1-79](docs/phase-1/F1-79-TRIAL-BALANCE-PAGINATION.md), [F1-80](docs/phase-1/F1-80-EXERCISE-SELECTOR.md), [F1-81](docs/phase-1/F1-81-SAFT-CREATE-CONTINUE.md), [F1-82](docs/phase-1/F1-82-TOPBAR-MENU-DISMISS.md), [F1-83](docs/phase-1/F1-83-GLOBAL-SEARCH.md), [F1-84](docs/phase-1/F1-84-BALANCE-SHEET-SUBCLASS-ONLY.md), [F1-85](docs/phase-1/F1-85-MASTER-DATA-PAGINATION.md), [F1-86](docs/phase-1/F1-86-PARTY-BALANCES-PAGINATION.md), [F1-87](docs/phase-1/F1-87-TRIAL-BALANCE-EXPAND-GROUP.md), [F1-88](docs/phase-1/F1-88-JOURNAL-DETAIL-PAGINATION.md), [F1-89](docs/phase-1/F1-89-SAFT-SKIPPED-SUMMARY.md), [F1-90](docs/phase-1/F1-90-DASHBOARD-REFERENCE-DATE-PERSIST.md), [F1-91](docs/phase-1/F1-91-BELL-TO-TASKS.md), [F1-92](docs/phase-1/F1-92-MOVEMENTS-LIST-EXERCISE-DEFAULT.md), [F1-93](docs/phase-1/F1-93-LEDGER-EXERCISE-DEFAULT.md), [F1-94](docs/phase-1/F1-94-STATEMENTS-EXERCISE-DEFAULT.md), [F1-95](docs/phase-1/F1-95-PERIOD-HELPERS-TESTS.md), [F1-96](docs/phase-1/F1-96-ACCOUNT-CLASSES-TESTS.md), [F1-97](docs/phase-1/F1-97-MOVEMENTS-LIST-SERVER-PAGING.md), [F1-98](docs/phase-1/F1-98-MOVEMENTS-LIST-CONSUMES-SERVER-PAGE.md), [F1-99](docs/phase-1/F1-99-MOVEMENTS-SUMMARY-ENDPOINT.md), [F1-100](docs/phase-1/F1-100-MOVEMENT-DETAIL-BY-REFERENCE.md), [F1-101](docs/phase-1/F1-101-JOURNAL-DETAIL-SERVER-PAGE.md), [F1-102](docs/phase-1/F1-102-GLOBAL-SEARCH-SERVER-MOVEMENTS.md), [F1-103](docs/phase-1/F1-103-DASHBOARD-OFF-MOVEMENTS-ARRAY.md), [F1-104](docs/phase-1/F1-104-JOURNAL-MOVEMENT-COUNTS-ENDPOINT.md), [F1-105](docs/phase-1/F1-105-WORK-CONTEXT-BOUNDED-MOVEMENTS.md) e [F1-106](docs/phase-1/F1-106-SHARED-MOVEMENTS-SUMMARY.md).

A Fase 2 — conformidade (validação XSD e analítica do SAF-T), cruzamento vendas SAF-T × compras e-Fatura × contabilidade, consulta à AT em produção, adaptador PHC alargado e relatórios com IA — está especificada em [docs/phase-2/](docs/phase-2/README.md), a começar pela [validação XSD/SVAT](docs/phase-2/F2-01-SAFT-XSD-VALIDATION.md) ([ADR-0004](docs/architecture-decisions/ADR-0004-SAFT-LAYERED-VALIDATION.md)). Estão feitos o [validador de esquema em streaming](docs/phase-2/F2-01A-XSD-STREAMING-VALIDATOR.md) (F2-01A), o [endpoint `saft/validate`](docs/phase-2/F2-01B-SAFT-VALIDATE-ENDPOINT.md) (F2-01B) o [leitor do modelo de validação](docs/phase-2/F2-01C-VALIDATION-MODEL-READER.md) (F2-01C), o [motor de regras analíticas](docs/phase-2/F2-01D-ANALYTICAL-RULE-ENGINE.md) (F2-01D) o [passo de validação no ecrã de importação](docs/phase-2/F2-01E-PWA-VALIDATION-STEP.md) (F2-01E) a [persistência e histórico das validações](docs/phase-2/F2-01F-VALIDATION-REPORT-PERSISTENCE.md) (F2-01F) e o [inventário SVAT](docs/phase-2/F2-01G-SVAT-GAP-ANALYSIS.md) (F2-01G, que fecha o épico); os esquemas oficiais da AT são um drop-in — ver [`Schemas/README.md`](src/AccountingAI.Infrastructure.Files/Schemas/README.md). O épico seguinte, o [cruzamento vendas SAF-T × compras e-Fatura × contabilidade](docs/phase-2/F2-03-CROSS-CHECK.md), está especificado; feitas a [ingestão do CSV do e-Fatura](docs/phase-2/F2-03A-EFATURA-CSV-INGESTION.md) (F2-03A) e o [ecrã de compras e-Fatura](docs/phase-2/F2-03B-EFATURA-PWA-SCREEN.md) (F2-03B).

### Abrir a aplicação web local

Execute `Abrir Accounting AI.cmd` na pasta principal, ou:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-local.ps1
```

A aplicação abre em `http://127.0.0.1:5173/`. O ficheiro `src/AccountingAI.Web/index.html` não deve ser usado como aplicação através de `file://`; se for aberto diretamente, apresenta agora a instrução correta em vez de uma página branca.

### SQL Server Express local

No perfil de desenvolvimento, a aplicação aplica automaticamente as migrações em falta, cria as tabelas na base `accountingAI` e repõe de forma idempotente os dados sintéticos em falta. A configuração versionada usa autenticação integrada. Quando for necessário login SQL, guarde a ligação apenas no secret store local:

```powershell
dotnet user-secrets set "ConnectionStrings:Accounting" "<connection-string-local>" --project .\src\AccountingAI.Api\AccountingAI.Api.csproj
```

Não coloque utilizadores, passwords ou connection strings com credenciais em `appsettings*.json`, Git ou scripts.

## Estado atual

O MVP lê ficheiros `1.03_01` e `1.04_01` pelos nomes dos elementos XML, sem depender de um namespace fixo. Suporta ficheiros de faturação (`F`), parciais (`P`), contabilidade (`C`) e integrados (`I`), bem como as secções específicas de recibos, transporte e documentos de conferência.

Funcionalidades disponíveis:

- abertura de um ou vários ficheiros XML;
- abertura de todos os XML de uma pasta;
- arrastar e largar ficheiros ou pastas;
- leitura segura, com DTD e resolução externa desativadas;
- suporte da codificação oficial Windows-1252;
- grelhas pesquisáveis e painel de detalhe para todos os campos encontrados;
- redimensionamento manual das colunas das grelhas, com reposição automática das larguras na abertura seguinte;
- relação mestre-detalhe: as linhas mudam com o documento onde está o cursor;
- cabeçalhos e campos apresentados em Português Europeu, mantendo internamente as chaves SAF-T para relações e validação;
- tabelas mestre: clientes, fornecedores, produtos e impostos;
- contabilidade: plano de contas, diários, documentos e lançamentos;
- importação simultânea de vários CSV do e-Fatura, identificando cada lote e o limite de 300 registos;
- acesso assistido ao Portal e-Fatura numa janela Edge integrada, com sessão isolada, autenticação com as credenciais guardadas, consultas diárias e importação automática de cada CSV descarregado;
- consulta direta das compras à AT, dia a dia e com páginas de 300 documentos;
- menu de configuração para AT, instalação PHC, empresas e credenciais ODBC;
- atualização das empresas PHC a partir do `EP.DBF` em cada arranque;
- associação inicial entre o contribuinte do SAF-T e a empresa PHC correspondente;
- configuração por empresa da senha do e-Fatura, criação de clientes/artigos, movimentação de stocks e cliente genérico PHC;
- correspondência por empresa entre séries de faturação/recebimentos do SAF-T e documentos PHC de `TD`/`TSRE` (com fallback `RETD`);
- modo de lançamento de faturas por empresa e correspondência entre taxas de IVA do SAF-T e artigos PHC de `ST`;
- lançamento transacional de um documento PHC agregado no cliente genérico, com pré-visualização e proteção contra duplicados;
- teste obrigatório do primeiro utilizador e palavra-passe ODBC, com indicação explícita de sucesso ou erro;
- leitura dos clientes `CL` (`no`, `nome`, `ncont`), privilegiando o NIF genérico `999999990` quando existe;
- configuração integralmente encriptada pela proteção do Windows para o utilizador atual;
- leitura conjunta de vários períodos;
- diagnóstico básico de cabeçalho e conteúdo detetado.

O separador **Validações** ainda não é um validador XSD/SVAT completo. Distingue erros de leitura, avisos e informação, mas não substitui o validador oficial da AT.

O enquadramento dos certificados para webservices, da certificação de programas de faturação e do SVAT está descrito em [docs/CERTIFICACAO_AT.md](docs/CERTIFICACAO_AT.md).

## Executar

```powershell
dotnet run --project .\src\AccountingAI.WinForms\AccountingAI.WinForms.csproj
```

Também é possível passar ficheiros ou uma pasta na linha de comandos:

```powershell
dotnet run --project .\src\AccountingAI.WinForms\AccountingAI.WinForms.csproj -- ".\SAFTs Exemplo"
```

## Compilar e validar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

O comando compila a solução completa, executa os testes da fundação, os smoke tests existentes e valida a PWA. O smoke test legado usa os cinco SAF-T reais existentes em `SAFTs Exemplo`, uma amostra sintética integrada e um `EP.DBF` sintético. Quando `C:\PHC_LOC\EP.DBF` está disponível, valida também a leitura desse catálogo real sem o alterar.

## Compras do e-Fatura

A aplicação importa um ou vários CSV obtidos no e-Fatura. Cada ficheiro fica identificado como lote próprio; lotes acima de 300 registos originam um aviso sem impedir a leitura.

Existem três vias independentes:

1. **Importar CSV** — seleciona um ou vários ficheiros já descarregados.
2. **Portal e-Fatura (assistido)** — pede a data inicial e final, abre o portal numa janela Edge/WebView2 com um perfil exclusivo do Accounting AI, seleciona automaticamente o método de autenticação **NIF** e introduz/submete o NIF e a palavra-passe guardados para a empresa identificada pelo SAF-T. A simples presença da alternativa `CC/CMD` no seletor inicial não é tratada como um desafio. A introdução usa eventos reais do navegador, necessários no formulário moderno de autenticação da AT. Depois consulta e exporta cada dia isoladamente (início e fim iguais). Para evitar que o calendário transforme as datas segundo a localização do Windows, a pesquisa submete diretamente no formulário os campos `dataInicioFilter` e `dataFimFilter` em `yyyy-MM-dd`, aguarda a navegação concluída e só então exporta/importa o CSV. Se um dia tiver exatamente 300 registos, assinala-o para confirmação, pois a divisão diária não consegue garantir a completude quando o próprio dia atinge o limite do Portal.
3. **Consulta de compras na AT (webservice)** — consulta SOAP direta como adquirente, com paginação e certificados autorizados; não envia nem altera documentos.

O perfil do browser e os CSV descarregados são guardados em `%LOCALAPPDATA%\Accounting AI\e-Fatura`. A janela permite cancelar a extração e apagar os cookies da sessão. O programa desativa o armazenamento de palavras-passe pelo WebView2 e não tenta contornar CMD, autenticação multifator ou CAPTCHA; se a AT pedir uma dessas etapas, a operação é interrompida com uma indicação ao utilizador.

A automatização assistida usa a interface Web atual do Portal, que não é uma API pública e pode ser alterada pela AT sem aviso. Por isso, os seletores da página e o fluxo completo devem ser novamente validados numa conta autorizada sempre que a AT modificar o Portal. As palavras-passe não são escritas nos registos da aplicação.

Em **Configuração > Configuração geral e empresas PHC** podem ser guardados os valores usados por defeito. A configuração fica em `%LOCALAPPDATA%\Accounting AI\config.dat`, integralmente encriptada para o utilizador atual do Windows. A senha normal do Portal fica associada a cada empresa; a senha do PFX não é gravada e continua a ser pedida em cada consulta oficial.

A consulta direta usa a operação `InvoicesRequest` do serviço oficial `fatshareFaturas`, no modo adquirente (`CustomerTaxID`). O intervalo inicial é sugerido a partir dos SAF-T carregados e pode ser alterado. O cliente divide o intervalo em dias e percorre todas as páginas de 300 documentos de cada dia. A AT exige:

- credenciais do sujeito passivo;
- certificado digital de produtor de software assinado pela AT;
- cabeçalho SOAP de segurança e cifra definidos pela AT;
- paginação e tratamento dos ambientes de teste e produção.

As senhas são introduzidas na janela da consulta, usadas apenas em memória e não são gravadas. O PFX e a chave pública da autenticação são carregados dos caminhos escolhidos pelo utilizador. Certificados, chaves e credenciais estão excluídos do controlo de versões.

O fluxo SOAP, AES/RSA, certificado cliente, segmentação diária, paginação e interpretação das respostas estão implementados e testados localmente com respostas sintéticas. A ligação ao ambiente da AT permanece **por validar ao vivo**, porque requer um certificado de produtor e credenciais WFA autorizadas. Desde 2025, os novos certificados de produtor devem observar a política RSA de 4096 bits publicada pela AT.

O Accounting AI utiliza estes serviços exclusivamente para consulta como adquirente. Não contém operações de registo, alteração, eliminação ou comunicação de documentos à AT.

## Empresas PHC

No arranque, a aplicação lê o `EP.DBF` da pasta PHC configurada. Na instalação de desenvolvimento é usada inicialmente `C:\PHC_LOC`; noutras instalações a localização pode ser alterada. O leitor aceita ainda uma pasta que contenha diretamente `EP.DBF` ou uma raiz com o ficheiro numa subpasta `_loc`.

Do `EP.DBF` são obtidos:

- nome da empresa (`NOME`);
- base de dados (`DATAB`);
- origem de dados ODBC (`ODBC`).

O `EP.DBF` não contém o contribuinte. Na primeira abertura de um SAF-T cujo NIF ainda não esteja associado, a aplicação pede ao utilizador que escolha a empresa PHC correspondente. Nas aberturas seguintes, a empresa é detetada automaticamente pelo NIF e fica imediatamente selecionada ao abrir o separador **Empresas PHC** da configuração.

As preferências de largura das colunas são guardadas por grelha e por campo dentro do mesmo `config.dat` encriptado. As grelhas novas começam com uma largura calculada pelo conteúdo visível; depois disso, cada ajuste feito com o rato passa a ser respeitado nas sessões seguintes.

As credenciais são mantidas por ODBC, permitindo que várias empresas que usem o mesmo DSN partilhem as mesmas credenciais. Na primeira introdução, a aplicação tenta abrir a base de dados e executar uma consulta mínima, informando sempre se o login foi aceite ou recusado; credenciais inválidas não são guardadas como validadas. Utilizador e palavra-passe ficam dentro do ficheiro encriptado e não aparecem em texto simples na grelha.

Por empresa podem ainda ser definidos **Cria Clientes**, **Cria Artigos** e, quando esta última opção está ativa, **Movimenta Stocks**. A lista de cliente genérico é obtida de `CL` pelos campos `no`, `nome` e `ncont`; se existir um cliente com `ncont = '999999990'`, é selecionado inicialmente.

O botão **Configurar faturação e recebimentos** apresenta duas grelhas. As séries são extraídas de `InvoiceNo`/`InvoiceType` e `PaymentRefNo`/`PaymentType` dos SAF-T pertencentes ao NIF da empresa. Os documentos de destino são lidos de `TD` (`NDOC`, `NMDOC`, `TIPOSAFT`) e de `TSRE` (`NDOC`, `NMDOC`, `TIPOREAO`); existe fallback para uma tabela `RETD` compatível. As linhas podem ser adicionadas ou removidas e as correspondências são guardadas por empresa no ficheiro encriptado. Os recebimentos continuam apenas configurados nesta fase.

O modo de lançamento de faturação também é definido por empresa: um único documento no cliente genérico, todos os documentos com linhas agrupadas por IVA, ou os documentos tal como constam do SAF-T. Nos dois modos agrupados surge uma grelha que relaciona cada taxa de IVA encontrada no SAF-T com um artigo lido de `ST` (`ref`, `design`).

Nesta fase, o botão **Lançar no PHC** executa apenas **Agrupa 1 documento no Cliente genérico**. A aplicação:

- ignora documentos anulados e agrega os restantes por taxa de IVA;
- usa a data do último documento do SAF-T e o cliente genérico configurado;
- valida o artigo `ST`, coloca `ST.CPOC` em `FI.CPOC` e `ST.TABIVA` em `FI.TABIVA`, e obtém `FI.IVA` de `TAXASIVA.TAXA` pelo respetivo código;
- apresenta empresa, documento, cliente, artigos, taxas e totais antes da confirmação;
- cria `FT`, `FT2`, `FT3`, `FI` e `FI2` numa única transação ODBC e faz rollback integral em caso de erro;
- grava uma referência determinística do conjunto SAF-T em `FT.FREF` e recusa novo lançamento do mesmo conjunto para o mesmo tipo de documento.

Por segurança, o documento `TD` de destino tem de estar configurado como importado/externo e, nesta primeira fase, não pode movimentar stocks, conta corrente ou integração contabilística automática. Essas áreas exigem as respetivas tabelas e regras PHC e permanecem fora deste lançamento inicial.

Nesta instalação os DSN PHC estão registados no ODBC de 32 bits, pelo que a publicação Windows é gerada para `win-x86`.

## Evolução prevista

1. Validação integral pelo XSD oficial e regras analíticas/SVAT.
2. Persistência de importações SAF-T de grande dimensão e análise multiperíodo sobre o núcleo contabilístico já iniciado.
3. Validação real da consulta direta no ambiente de testes e depois em produção da AT.
4. Cruzamento vendas SAF-T vs. compras e-Fatura vs. contabilidade.
5. Alargar o adaptador PHC aos outros dois modos, conta corrente, stocks, contabilidade e recebimentos, com regras explícitas por empresa.
6. Relatórios de gestão assistidos por IA, sempre construídos sobre dados reconciliados e com rastreabilidade até ao documento/lançamento.

Antes de usar o lançamento numa base real, deve ser feito um ensaio numa cópia de testes da base PHC e confirmadas a versão, as colunas obrigatórias, os `STAMP`, o documento `TD` e as regras específicas dessa instalação.
