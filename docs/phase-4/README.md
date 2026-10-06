# Fase 4 — À altura dos melhores

Plano saído da [comparação com o mercado](../COMPARACAO-MERCADO.md)
(2026-10-05). A ordem foi aprovada pelo utilizador: fundações primeiro, depois
a AT, os mapas, os impressos oficiais, as listagens e a gestão de gabinete.

## Fundações

| # | O quê | Estado |
|---|---|---|
| F4-01 | Confiança nos números | **Feito** |
| F4-02 | Esquemas oficiais da AT | **Feito** |
| F4-03 | Histórico de alterações dos lançamentos | **Feito** |
| F4-04 | Excel e PDF gerados pelo servidor | **Feito** |
| F4-05 | Tarefas automáticas | **Feito** |
| F4-06 | Email de saída | Por fazer: falta o servidor de envio a usar |
| F4-07 | Cópias de segurança | Por fazer: falta o destino e a frequência |

### F4-01 — Confiança nos números

Defeitos encontrados ao ler o código para a comparação com o mercado.

- **Saldo inicial do balancete e da razão.** Parte do lançamento de abertura
  do exercício. Antes somava todos os anos anteriores: depois de aberto o ano
  novo, as contas de balanço contavam duas vezes, e os gastos e rendimentos
  vinham de outros exercícios. Enquanto o exercício não tiver abertura, as
  contas de balanço trazem o saldo de trás.
- **Balanço e DR sintéticos.** Pedem o balancete sem o lançamento de
  apuramento (`excludeClosing=true`). Com ele, as classes 6 e 7 ficam a zero e
  o resultado vai para a classe 8, que estes ecrãs não leem.
- **Análises.** Os subtotais e o total geral são de todas as linhas; só o
  detalhe devolvido é cortado nas 5 000. Acima de 300 000 linhas a análise é
  recusada com explicação.
- **Falha do servidor.** Oito ecrãs de mapas recalculavam em silêncio a partir
  dos movimentos em memória, que já não estão todos carregados. Agora ficam
  vazios e a moldura mostra um aviso.
- **Plano de contas do SAF-T.** As contas de movimento são as que o ficheiro
  diz (`GroupingCategory` «GM»). A procura da conta-mãe deixou de ser
  quadrática (cerca de 50 s para 44 mil contas).

### F4-02 — Esquemas oficiais da AT

- **`scripts/get-official-schemas.ps1`** descarrega os XSD oficiais para a
  pasta `Schemas`, que o git ignora. Ver
  [`Schemas/README.md`](../../src/NexGenAccountingAI.Infrastructure.Files/Schemas/README.md).
- **A validação contra o esquema oficial nunca tinha funcionado.** O SAF-T
  1.04_01 é XSD 1.1 e o .NET só compila XSD 1.0: com o esquema na pasta, a
  validação dava erro 500. O catálogo tira as 20 regras `xs:assert` e relaxa
  o grupo `xs:all` das linhas; o relatório diz que 21 regras ficam por
  verificar.
- **Taxonomias.** A importação do SAF-T guarda a taxonomia de cada conta e
  preenche a das contas que já existiam sem ela.
- **Validado com dados reais.** O ficheiro de exemplo de 61,8 MB é válido
  face ao esquema oficial; a exportação da mesma empresa pela aplicação
  (41 MB) também, com zero erros.

### F4-03 — Histórico de alterações dos lançamentos

- **`accounting.JournalEntryChanges`** (migração `JournalEntryChanges`): por
  cada alteração a um lançamento que já existia, quem, quando, o que
  aconteceu e o lançamento **como estava antes** (cabeçalho e linhas, em
  JSON). Só se acrescenta.
- **Gravado na própria gravação** (`JournalEntryHistoryInterceptor`), por
  isso cobre todos os caminhos: correção, conferência, reabertura, anulação
  e remoção por qualquer serviço. A chave é a empresa e a referência, porque
  corrigir um lançamento remove-o e recria-o.
- **Não gera histórico:** a criação de um lançamento (fica nos carimbos de
  auditoria) e as alterações em massa por SQL direto, que não mexem em
  valores.
- **API:** `GET .../accounting/movements/{referência}/history`.
- **PWA:** painel «Histórico de alterações» no detalhe do movimento, com
  «Ver versão anterior».

### F4-04 — Excel e PDF gerados pelo servidor

- **Todos os ecrãs que exportavam CSV** (31 ficheiros, 33 botões) passam a
  ter CSV, Excel e PDF, pelo componente `ExportButtons`.
- **Como funciona.** O ecrã continua a montar as linhas como para o CSV; o
  botão anuncia o formato ao módulo `app/csv.ts`, que envia as mesmas linhas
  a `POST .../exports/table?format=xlsx|pdf`.
- **Excel** (ClosedXML, licença MIT): números como números, com formato
  português; cabeçalho fixo e com filtro; totais a negrito; preparado para
  imprimir. Um texto começado por «=» nunca vira fórmula.
- **PDF** (PDFsharp-MigraDoc, licença MIT): cabeçalho com a empresa e o NIF,
  título, cabeçalho das colunas repetido em cada página, páginas numeradas,
  paisagem automática, colunas de números à medida do maior valor. Limite de
  5 000 linhas, com mensagem a remeter para o Excel.
- **Segurança.** O nome e o NIF do cabeçalho vêm da base de dados e não do
  pedido. Quem só tem «Consulta» pode exportar.
- **Limitação.** O título é o do ecrã e o período ainda não vai no
  cabeçalho; os mapas com layout próprio (balanço em painéis) saem em tabela
  simples. O motor de mapas da fase 4B trata disso.

### F4-05 — Tarefas automáticas

- **Anfitrião** (`BackgroundJobHost`) dentro do processo da API, para o
  serviço do Windows continuar a ser um só. Desliga-se com
  `BackgroundJobs:Enabled = false`.
- **Registo** (`infrastructure.BackgroundJobRuns`, migração
  `BackgroundJobRuns`): uma linha por tarefa, empresa e período. É o que
  impede a mesma unidade de correr duas vezes. Uma falha volta a ser tentada
  passada uma hora, até três vezes; uma execução parada a meio há mais de
  duas horas é retomada.
- **Tarefas:**

| Tarefa | Quando | O que faz |
|---|---|---|
| Síntese mensal com IA | A partir do dia 10, para o mês anterior | Gera o relatório nas empresas com a opção ligada. A opção já existia no ecrã mas nada a executava. Um mês sem movimentos não gera relatório. |
| Manutenção da base de dados | Todos os dias, depois das 3 h | Atualiza as estatísticas, para os mapas não ficarem lentos. |

- **API:** `GET /users/{userId}/background-jobs`.
- **PWA:** painel «Tarefas automáticas» em «Empresas e utilizadores».
- **A seguir** (dependem de outras partes): recolha diária do e-Fatura
  (fase 4A), avisos por email (F4-06), cópias de segurança (F4-07).

## Fase 4A — Comunicação com a AT

| # | O quê | Estado |
|---|---|---|
| F4A-01 | Cliente dos webservices da AT (autenticação, certificado, registo dos pedidos) | **Feito**, por testar contra a AT |
| F4A-02 | Declaração periódica de IVA: ficheiro XML oficial, validar e submeter | **Feito**, por testar contra a AT |
| F4A-03 | e-Fatura por webservice | Por fazer |
| F4A-04 | Declaração recapitulativa | Por fazer |

### O que falta para testar contra a AT

O certificado de testes e a chave pública estão em descarregamento público
no Portal das Finanças, mas **protegidos por senha**, que não está publicada
(nem na página, nem nos manuais). Vem na resposta da AT ao pedido por email
(enviado a 2026-10-03) ou na área de produtores de software do e-Fatura,
com o login do produtor. Com isso:

1. pôr o `TesteWebservices.pfx` numa pasta e indicar `At:Certificate:Path` e
   a senha (`AT_CERTIFICATE_PASSWORD` ou *user-secrets*);
2. extrair a chave pública (`.cer`) e indicar `At:PublicKey:Path`;
3. guardar o subutilizador e a senha da empresa em «Comunicação com a AT»
   (subutilizador com a autorização **WVA**);
4. carregar em «Validar na AT» no ecrã da declaração de IVA.

Até lá, tudo o que não depende da AT está testado: a cifra das credenciais
(fazendo o papel da AT, com a chave privada), o envelope SOAP, a leitura das
respostas e o ficheiro da declaração contra o esquema oficial.

### F4A-01 — Cliente dos webservices da AT

- **`AtWsSecurity`**: o cabeçalho de autenticação descrito nos manuais de
  integração (igual na DP de IVA, no Modelo 22, na IES e nas obrigações
  acessórias). Por cada autenticação, uma chave AES de 128 bits nova; a
  senha vai cifrada com ela (AES, ECB, PKCS5); a chave vai cifrada com a
  chave pública da AT (RSA); o `Digest` é o SHA-1 de chave + data + senha,
  cifrado; a data vai em claro, em UTC. Suporta o sujeito passivo e o
  contabilista certificado no mesmo pedido (versão «2»); um contribuinte sem
  senha leva só o utilizador (plenos poderes).
- **`VatReturnAtGateway`**: SOAP 1.1 sobre HTTPS com o certificado do
  produtor; testes na porta 706, produção na 406. Lê o código, a mensagem,
  os erros e alertas (anexo, quadro, campo, código) e o identificador da
  declaração; uma falha de rede ou de configuração vem explicada, sem
  exceção.
- **`infrastructure.AtExchanges`** (migração `AtExchanges`): cada pedido à
  AT fica registado — serviço, operação, ambiente, período, quem pediu,
  tempo e resposta. Não guarda senhas nem o conteúdo enviado.
- **Configuração nova:** `At:PublicKey:Path`.

### F4A-02 — Declaração periódica de IVA

- **`VatDeclarationXmlWriter`** escreve o ficheiro pelo esquema oficial
  `dpiva-06.xsd`. A correspondência entre os campos do impresso e os
  elementos do ficheiro vem da documentação do próprio esquema («Campo 3.»
  em `btTaxaNormal`, etc.).
- **O que o esquema não diz** e foi tirado de um ficheiro exportado do
  Portal, publicado por terceiros
  (`github.com/filipef101/dp-iva-anexo-fornecedores`): valores em cêntimos;
  período «01» a «12» ou «03T», «06T», «09T», «12T»; atributo
  `version="06"`; sede 1, 2 ou 3; prazo 1 ou 2; sim e não como «01» e «02».
  **A confirmar pela validação da AT.**
- **Só leva o quadro 06.** O quadro 06-A vai a zero e os anexos não são
  gerados. Um período com operações noutra região (campos 65 a 68) é
  recusado com a explicação, porque exigia o Anexo R. Um campo negativo
  também.
- **A declaração vai comprimida**: «base64 do base64 do ZIP», como diz o
  manual, com `versaoDeclaracao` «2016».
- **Autenticação:** como sujeito passivo, com o subutilizador e a senha
  guardados para a empresa. A autenticação do contabilista certificado com
  plenos poderes fica para depois (falta guardar as credenciais do CC).
- **Submeter só no ambiente de testes.** Em produção a aplicação valida mas
  recusa submeter, até aderir ao serviço com certificado próprio.
- **API:** `GET .../vat-declaration/xml`, `POST .../vat-declaration/at/validate`,
  `POST .../vat-declaration/at/submit`, `GET .../at/exchanges`.
- **PWA:** painel «Entrega na AT» no ecrã da declaração de IVA: ficheiro
  XML, «Validar na AT», «Submeter (testes)», os erros e alertas da AT por
  campo e os últimos pedidos.
- **Visto com dados reais:** os ficheiros de setembro e do 3.º trimestre de
  2025 da Vidreira são válidos face ao esquema oficial. **Saem a zeros**,
  porque as contas importadas do SAF-T não têm classificação de IVA: é
  preciso classificá-las em «Configuração de IVA» (ou a importação passar a
  propor essa classificação — por fazer).

## Fase 4B — Motor de mapas

O que a comparação de mercado pediu na secção 7: um motor para o utilizador
desenhar os seus mapas, e o balancete e o balanço a ler-se bem.

### F4B-01 — Motor de mapas

- `Application/Maps`: um mapa é o cruzamento de **linhas** com **colunas**
  (`MapDefinition`). Uma linha escolhe contas por prefixo (`61 + 62 - 6221`,
  intervalos `61..63`; `MapAccountSelector`), ou é uma fórmula sobre outras
  linhas (`L10 - L20`, `A % B` para percentagens; `MapFormula`), ou um título.
  Cada linha tem uma medida (saldo, saldo devedor, saldo credor, débito,
  crédito), um sinal (normal, invertido, absoluto, zero se negativo) e uma
  condição de exibição. Uma coluna é o período pedido, o acumulado do
  exercício, o saldo inicial, um mês fixo ou outro exercício (`YearOffset`), ou
  uma fórmula sobre outras colunas.
- `MapEngine`: valida (mensagens em português, deteta ciclos), corre, explica
  (as contas por trás de uma linha) e verifica a cobertura (contas que nenhuma
  linha apanha, ou que duas apanham — o que faz um balanço não fechar).
- `MapColumnSets`: conjuntos prontos — período; balancete clássico (saldo
  inicial, débito, crédito, saldo devedor, saldo credor); período e acumulado;
  comparativo com o ano anterior (variação e %); mensal (12 meses e total).
- `Persistence/MapDataProvider`: por conta e exercício, débito e crédito da
  abertura, de cada mês e do apuramento, numa consulta agregada por exercício.
  O apuramento vindo de um SAF-T (não marcado) reconhece-se por mexer na 811 ou
  818. Sem abertura lançada, as contas de balanço trazem o acumulado desde a
  última abertura anterior.

### F4B-02 — Balancete em árvore

- `TrialBalanceTree` (Application) arruma as contas com valores pelo plano da
  empresa, da classe à conta de movimento, somando para cima; os saldos
  devedores e credores das subcontas não se compensam na conta de agregação.
- `TrialBalanceTreeService` e `GET …/accounting/balancete?year=&fromMonth=&toMonth=&columns=&depth=|parent=|search=&excludeClosing=&all=`:
  calcula a árvore toda no servidor (0,3–0,5 s na Vidreira, 2 500 contas com
  valores) e devolve só o pedido — as classes, um ramo, até um nível, ou uma
  pesquisa; no máximo 3 000 linhas de cada vez (`all=true` para exportar).
  Avisa quando o exercício não tem abertura lançada.
- `pages/BalancetePage.tsx` em `/balancete` (com a base de dados; em
  demonstração fica a lista antiga, que continua em `/balancete/lista` com o
  agrupamento por taxonomia): exercício, de mês a mês, conjunto de colunas,
  nível, com ou sem apuramento, pesquisa; abrir e fechar ramos; zeros a traço,
  negativos a vermelho; exportação CSV/Excel/PDF do que está à vista (ou de
  tudo, se o ecrã cortou).
- Corrige o defeito da secção 3.2 da comparação: a coluna «Abertura» do
  balancete do ano inteiro já não sai a zero — a abertura tem o seu lugar
  próprio, separada dos meses.

### F4B-03 — Mapas desenhados pelo utilizador

- `reporting.FinancialMap` (migração `20261005232025_FinancialMaps`): nome
  único por empresa e a `MapDefinition` em JSON (`MapJson`, enumerados por
  nome). `FinancialMapService`: listar, criar, alterar, apagar, correr e
  explicar; correr devolve avisos de cobertura — contas com valores que
  nenhuma linha apanha (só nas classes que o mapa toca) e contas que duas
  linhas apanham (uma linha de saldo devedor e outra de saldo credor não é
  repetição: o Estado está no ativo e no passivo).
- `MapTemplates`: modelos de partida pelo código de contas SNC — demonstração
  de resultados por naturezas (comparativo com o ano anterior), balanço
  sintético (corrente/não corrente por conta, a acertar ao plano da empresa) e
  mapa de exploração mensal. Um teste confirma que, num exercício pequeno, a DR
  e o balanço batem certo e o balanço fecha.
- Endpoints `…/maps` (lista + modelos + conjuntos de colunas), `…/maps/templates/{key}`,
  `POST/PUT/DELETE …/maps[/{id}]`, `POST …/maps/run` (definição + período;
  com `rowCode` devolve as contas por trás da linha; permitido a Consulta).
- PWA: `/mapas` (os da empresa e os modelos), `/mapas/{id}`, `/mapas/modelo/{key}`,
  `/mapas/novo`. O ecrã lê o mapa no período pedido (antes ou com o apuramento),
  clicar numa linha de contas mostra as contas que a formam (com ligação à
  razão), «Desenhar» abre o desenhador — linhas (código, descrição, tipo,
  contas ou fórmula, medida, sinal, quando mostrar, nível, negrito, oculta) e
  colunas (tipo, âmbito, exercício, mês, fórmula, medida, %), com os conjuntos
  prontos — «Testar» calcula sem gravar e «Guardar» faz do modelo um mapa da
  empresa. Exportação CSV/Excel/PDF.
- Validado na Vidreira: os três modelos correm em 0,3–0,6 s; os números do
  balanço não fecham nessa empresa por razões dos dados (compras da classe 31
  não transferidas, resultado de 2024 por aplicar) — o próprio mapa avisa.

### F4B-04 — Relato em PDF e relatório para a banca

- `Application/Maps/ReportPack.cs`: um relato é uma capa e secções — texto
  (parágrafos), mapa (guardado ou modelo), balancete (até um nível) ou rácios.
  `ReportPackPresets.Bank` é o relatório para a banca: apresentação, balanço,
  DR, exploração mensal, rácios e balancete sintético, lido antes do apuramento.
- `Infrastructure.Files/ReportPackWriter`: o PDF (MigraDoc) com capa (empresa,
  título, período, preparado por, índice), secções numeradas, tabelas com os
  níveis e negritos dos mapas, as tabelas largas (mais de 8 colunas, o mapa
  mensal) ao alto, rodapé com página; no máximo 3 000 linhas por tabela.
- `ReportPackService.BuildAsync` compõe o relato a partir dos números; o
  texto de IA é à parte (`NarrateAsync`): pede ao narrador dos relatórios de
  gestão (`ManagementReportService`, pergunta livre com o dossier do período)
  uma apresentação para a banca, e devolve os parágrafos para o utilizador
  **rever e corrigir antes** de gerar o PDF. Respeita o interruptor de IA da
  empresa e a chave do servidor; um texto marcado `[IA]` põe o aviso no fim do
  relato.
- Endpoints `GET …/reports/pack/presets`, `POST …/reports/pack` (PDF, ou
  `?format=json` para pré-visualizar; permitido a Consulta) e
  `POST …/reports/pack/narrate`.
- PWA `/relatorio-banca` (menu Gestão → «Relatório para a banca»): capa e
  período, secções a ligar/desligar, reordenar, acrescentar (texto, mapa,
  balancete, rácios), texto à mão ou «Redigir com IA» com indicações,
  pré-visualização e «Gerar PDF».
- Validado na Vidreira: 6 páginas em 1,8 s; a IA responde «desligada para
  esta empresa» enquanto não houver chave e interruptor.

### Por fazer na 4B

- O balanço e a DR oficiais assentes no motor (hoje continuam no serviço das
  demonstrações).
- Guardar relatos compostos pela empresa (hoje parte-se sempre do modelo),
  o período preferido por mapa, e partilhar mapas entre as empresas de um
  gabinete.

## Fase 4C — Impressos oficiais

### F4C-01 — Declaração periódica de IVA no impresso da AT

- `pages/VatDeclarationForm.tsx`: a declaração com o aspeto do modelo oficial
  (Portaria n.º 221/2017; o modelo em vigor até aos períodos de junho de
  2027) — quadros 01 (identificação e sede), 02 (ano, período, prazo), 03
  (operações noutra região, por enquanto só informativo), 04 (recapitulativa),
  05 (inexistência de operações), 06 (as três colunas com os números dos
  campos 1 a 96), 06-A (97 a 107 com a soma 106) e 20 (NIF do CC). Os campos
  apurados pela contabilidade aparecem a cinzento e abrem as contas que os
  formam; os que a aplicação não apura (14, 15, 18, 19, 95, 97–107, os
  assinalamentos e o NIF) escrevem-se no impresso. A vista «Tabela» continua
  disponível. Imprime-se com o aspeto do impresso.
- `VatDeclarationInputs` (Application) e a tabela `accounting.VatDeclarationInputs`
  (migração `20261006070630_VatDeclarationInputs`): o que se preencheu à mão
  fica guardado por (empresa, período) e entra no cálculo (campos 14/15/18/19
  nos totais, 95 limitado ao 94, 96 = 94 − 95, 106 = soma do 06-A) e no XML
  (`prazo`, `apresentouDeclRecapitulativa`, `semOperacoes`, `nifCC`,
  `temOperacoesAdquirenteComLiqImposto` «01»/«02» e os elementos do
  `desenvolvimento`). Endpoints `GET/PUT …/vat-declaration/inputs?from&to`;
  a entrega (XML, validar, submeter) lê o que está guardado — o painel de
  entrega deixou de ter as caixas de prazo e recapitulativa.
- O ficheiro com o quadro 06-A continua válido face ao `dpiva-06.xsd` oficial
  (teste de integração).

### F4C-02 — Modelo 22 no impresso da AT

- `pages/Modelo22Form.tsx`: o Modelo 22 com o aspeto do impresso em vigor a
  partir de janeiro de 2026 (Despacho n.º 320/2026): rosto (quadros 01 a 05:
  período, designação, NIF, tipo de sujeito passivo, tipo de declaração,
  NIF do representante e do CC), quadro 07 completo pela ordem oficial (com as
  faixas «A acrescer» / «A deduzir», as somas 708/753/776 e 777/778), quadro 08
  (regimes de taxa, a assinalar), quadro 09 (a coluna do regime geral apurada;
  as outras em branco), quadro 10 (todas as linhas, incluindo 348/349/350/370,
  que passam a entrar na coleta) e quadro 11 (410 e 411 da DR). A vista
  «Tabela» mantém a ajuda das tributações autónomas e a lista de campos.
- Os assinalamentos do rosto guardam-se como os outros campos do exercício
  (`tipo-sujeito-passivo`, `tipo-declaracao`, `nif-representante`, `nif-cc`,
  `regime-reducao-taxa`, `regime-geral`), sem tabela nova.
- Por fazer: quadros 06, 10-A, 10-B, 11-A, 11-B, 12 a 14 e os anexos; a
  entrega pelo webservice.

### F4C-03 — XML do Modelo 22

- `Modelo22XmlWriter` escreve o ficheiro pelo esquema oficial
  `Modelo22IRCv2026.xsd` (namespace `dm22irccli-net`, `version="v2026"`): rosto
  (quadros 01 a 05, o 02 em branco), 07, 08, 09 (regime geral), 10 e 11. Os
  valores vão em euros com duas casas decimais (a DP de IVA vai em cêntimos).
  Recusa NIF sem nove dígitos e valores negativos onde o esquema não admite sinal.
- Endpoint `.../modelo-22/xml?year=` e botão «Ficheiro XML (AT)» na página.
- O teste valida o ficheiro contra o XSD oficial com zero erros. Para isso o
  auxiliar `XsdValidation` passou a resolver os `include` (o `types.xsd`).
- Não verificado: o significado do quadro 02 (serviço de finanças), que fica em
  branco, e o que a AT aceita na entrega; a validação final é a do Portal.

### F4C-04 — XML da IES (Anexo A)

- `IesXmlWriter` escreve a IES pelo esquema «2027 e seguintes» (`IES-DA.xsd`,
  namespace `IES-DA/2021`, a IES com SAF-T): atributos `versao`, `nif`, `ano`,
  rosto (quadro 01 e NIF) e Anexo A (`id` = NIC/NCRF/NCRF-PE/NC-ME, opção do
  quadro 02-A, quadros 03 e 04 com as somas `-soma`). Os códigos `A00001…` são os
  que `IesAnnexAService` já usava. Endpoint `.../ies/xml?year=` e botão na página.
- O esquema vive em `Schemas/Ies2027/` (ignorado pelo git; o
  `get-official-schemas.ps1` descarrega-o), porque os ficheiros têm os mesmos
  nomes dos outros pacotes. O teste valida o ficheiro contra ele, sem erros.
- **Decisão do utilizador (2026-10-06):** o modelo de 2027 usa-se em 2027 para os
  dados de 2026. A página da AT diz «declaração do período de 2027», o que
  admite outra leitura (IES de 2028 sobre 2027); confirmar quando a AT o disser.
  O pacote «2025 e anteriores» usa outros códigos (`A5001-1/-2`) e não foi usado.
- **Não confirmado:** o valor do atributo `versao` (o esquema não o indica; está
  a 1). Rosto sem os quadros 02, 04, 06 e 07; sem os outros anexos.

### F4C-05 — Anexo R da DP de IVA

- `VatDeclarationService` passa a somar as operações de outra região (até duas)
  também em campos próprios dessa região: os campos 1 a 24 e os totais 62, 63 e
  64 do Anexo R (`VatRegionalAnnex`), além dos totais 65 a 68 que já entravam no
  quadro 06. As regularizações (40 e 41) continuam globais.
- `VatDeclarationXmlWriter` deixou de recusar o ficheiro com operações noutra
  região: escreve um `anexoR` por região (apuramento; o desenvolvimento vai vazio)
  e marca `temAnexoRAcores/Continente/Madeira`. Válido face ao `dpiva-06.xsd`.
- O impresso mostra o Anexo R de cada região, com as contas de cada campo.
- **Não confirmado:** o valor do atributo `id` do anexo (o esquema só pede um
  texto único; está o nome da região) e se a AT aceita o desenvolvimento vazio.
  As taxas da região não são verificadas contra as tabelas fiscais.

### F4C-06 — Anexos dos campos 40 e 41 e entrega do Modelo 22

- **Anexos 40 e 41** (`anexoCampo40R`, `anexoCampo41R`): quando há regularizações, o
  ficheiro leva o total em «Outras regularizações» (campos 3B e 4B) com a base de
  incidência dos lançamentos de base. Os lançamentos não dizem o artigo do CIVA
  (78.º-A, 78.º-B, 78.º-C, 23.º a 26.º, crédito até 750 €), por isso as listas por
  artigo não são geradas e o aviso pede para reclassificar no Portal, se for caso disso.
- **Modelo 22 pelo webservice**: ao contrário do que se registou, a AT publica o WSDL
  (`wsSubmeterDeclaracaoIRC.wsdl`) e o manual de integração de 31-07-2025. O
  `AtSoapClient` passou a ser o transporte comum (DP de IVA e Modelo 22);
  `Modelo22AtGateway` traz os endereços (portas 405 e 705), as operações
  `validarDeclaracao` e `submeterDeclaracao` e o ficheiro em Base64 simples (sem ZIP).
  `Modelo22FilingService` guarda cada pedido em `AtExchanges` e, como na DP de IVA, só
  submete no ambiente de testes. O painel «Entrega na AT» é agora genérico e aparece
  também na página do Modelo 22.
- **Não confirmado:** a `versaoDeclaracao`. O manual diz «22» mais a versão do ficheiro
  (2222 em 2020, depois 2223, 2224…); está `2228` por extrapolação (`At:Modelo22Version`
  corrige-a sem alterar código). A «Validar na AT» dirá se é aceite. Nada foi exercido
  contra a AT: falta o certificado de testes e o subutilizador com permissão para o serviço.

### Por fazer na 4C

- As listas por artigo dos anexos das regularizações dos campos 40 e 41 (exige guardar o artigo em cada lançamento).
- O modelo novo da Portaria n.º 298/2026/1 (períodos a partir de julho de
  2027): campos 108/109 e a parte E do 06-A.
- IES no impresso da AT, assente nos mesmos componentes.

## Validação

- .NET: 323 testes. PWA: 134 testes.
- Vistos a funcionar com a base real: a validação e a exportação do SAF-T, a
  exportação para PDF e Excel pela API, e a tarefa diária a correr sozinha.
- **Ainda não vistos no browser** com a sessão do utilizador: o painel do
  histórico, os botões de exportação e o painel das tarefas.

## Fases seguintes

Ver a secção 9 de [`COMPARACAO-MERCADO.md`](../COMPARACAO-MERCADO.md): 4A
(AT), 4B (motor de mapas, balancete e balanço novos), 4C (impressos
oficiais), 4D (listagens), 4E (gabinete), 4F (diferenciação). O utilizador
pediu também um **relatório para a banca** com IA, a fazer logo que haja o
motor de mapas e o PDF de relato.
