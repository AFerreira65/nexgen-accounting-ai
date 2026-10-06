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
  a 10). Rosto sem os quadros 02, 04, 06 e 07; sem os outros anexos.

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

### F4C-07 — IES pelo webservice

- `IesAtGateway` (portas 407 e 707, WSDL `wsSubmeterDeclaracaoIES`, manual de 31-07-2025):
  ficheiro em Base64 simples, e a `versaoDeclaracao` é a do atributo `versao` do próprio
  ficheiro. O serviço não tem alertas.
- A IES é entregue pelo **contabilista certificado**: o pedido leva o utilizador e a senha
  dele (actor TOC) e o sujeito passivo (só o NIF se o contabilista tem plenos poderes
  declarativos; com a senha da empresa se não tem). O manual manda pedir estas credenciais
  em cada entrega, por isso `IesFilingService` recebe-as no pedido e **não as guarda nem
  as regista**; o painel limpa as senhas depois de cada pedido.
- Mesmos limites que os outros serviços: submissão só no ambiente de testes; cada pedido
  fica em `AtExchanges`. O painel «Entrega na AT» aparece na página da IES.
- **Não confirmado:** o valor de `versao` (10, extrapolado de «Versão 9» do pacote de 2024)
  e se o modelo de 2027 serve para os dados de 2026; a AT dirá ao validar. A permissão «TIE»
  do subutilizador do contabilista (manual, ponto 3) tem de estar atribuída.

## Fase 4D — Listagens à medida (motor B)

### F4D-01 — Motor de listagens, origem «linhas de lançamentos»

- **Definição só de dados** (`Application/Listings`): origem, colunas, filtros «e»/«ou» com
  grupos dentro de grupos, ordenação, agrupamento até três níveis e parâmetros com valor por
  omissão relativo («início do ano», «fim do mês anterior»…). Validada por `ListingCatalog.Validate`
  (campos que existem, operadores que o tipo admite, máximos de colunas, condições e níveis).
- **Execução** (`ListingEngine`): cada filtro, ordenação e agrupamento vira uma expressão LINQ
  tipada que o EF traduz em SQL com parâmetros; nunca se junta texto do utilizador a uma consulta.
  O filtro de empresa vem da sessão. Os lançamentos anulados ficam de fora, salvo se a listagem
  filtrar pelo estado. A pesquisa de texto não distingue maiúsculas (`ILIKE`) e os caracteres
  `%`, `_` e `\` valem como texto: o provider acrescenta `ESCAPE ''` se não se indicar o escape,
  por isso o caractere é passado de forma explícita (teste com «z_100%» e com texto hostil).
- **Agrupamento por níveis**: cada pedido traz um nível (grupos com contagem e somas de débito,
  crédito e saldo) ou, no último, as linhas paginadas; abre-se um nível de cada vez, como no
  balancete em árvore. Total geral sempre presente.
- **Navegar da linha ao registo**: cada linha leva o lançamento e o terceiro; a conta liga ao razão.
- **Guardar** (`SavedListing`, migração `SavedListings`): privada, da empresa ou do gabinete;
  duplicar; favoritas; procura por nome. A privada e a do gabinete só o dono as muda; a da empresa
  qualquer membro. A função «Consulta» corre e exporta, não guarda.
- **Excel e PDF no servidor** (`listings/export`): todas as linhas até 100 000, com os campos de
  grupo primeiro na ordenação; o PDF mantém o limite do escritor existente.
- PWA: `/listagens` (lista) e `/listagens/nova` e `/listagens/:id` (desenho e resultado).
### F4D-02 — Mais origens de dados

- O núcleo passou a ser genérico (`ListingRunner<TRow>`): cada origem diz ao motor a consulta
  de partida (sempre limitada à empresa), o que cada campo é em SQL, as chaves de agrupamento,
  até quatro somas e a ligação ao registo. Filtros, ordenação, níveis, parâmetros, paginação e
  totais são comuns. Campos que admitem nulos (datas de abate, data do documento) filtram-se
  com comparações entre tipos com nulos.
- Origens (`ListingCatalog`): linhas de lançamentos, saldos por conta e mês, documentos de venda,
  compras do e-Fatura, ativos fixos, movimentos bancários e arquivo digital. Os estados que as
  tabelas não guardam como texto («Por lançar», «Lançada», «Dispensada», «Em uso», «Abatido»,
  «Conciliado»…) calculam-se na consulta e servem de filtro e de grupo. Os anulados ficam de
  fora nas linhas, nas vendas e nos saldos, salvo se a listagem filtrar pelo estado.
- Teste (`ListingSourcesTests`): dados de cada origem numa base real, agregações, filtros, o
  isolamento entre empresas e a exportação de todas as colunas de todas as origens.

### F4D-03 — Modelos guiados

- `ListingTemplates`: extrato de uma conta, razão por conta, saldo por terceiro, evolução mensal
  por conta, vendas por cliente, vendas por contabilizar, compras por lançar, ativos por estado,
  movimentos bancários por conciliar e documentos arquivados por ligar. Abrem-se em
  `/listagens/modelo/:key`, correm com os valores por omissão (datas relativas ao dia de hoje) e
  guardam-se como listagem própria. Todos são validados e corridos pelo teste.
- O desenhador passou a ter o seletor de origem (numa listagem nova).

### F4D-04 — Tabela cruzada, gráfico, saldo acumulado e formatação condicional

- **Tabela cruzada** (`listings/pivot`): dois campos de agrupamento (linhas e colunas) e uma medida
  (qualquer soma da origem, o saldo derivado, ou o número de linhas). Usa os filtros e os parâmetros
  da listagem; o servidor agrupa por uma chave composta, até 500 linhas e 40 colunas (acima disso
  pede filtros). A página mostra a matriz com os totais, um gráfico das cinco linhas maiores e
  exporta para CSV, Excel e PDF.
- **Saldo acumulado** (linhas de lançamentos e movimentos bancários): soma o débito menos o crédito
  (ou o valor) pela ordem da listagem e continua de página para página, calculando o que ficou nas
  anteriores na base de dados. O extrato de uma conta usa-o.
- **Formatação condicional**: até dez regras por listagem (coluna numérica ou de data, comparação,
  valor e tom). Só dá cor à célula; o valor não muda. Validada no servidor, aplicada no browser.
- Testes: pivot sobre saldos, vendas e ativos (incluindo medidas derivadas, contagem e o grupo
  «sem valor»), pedidos inválidos, saldo acumulado entre páginas, regras de formatação válidas e
  inválidas, e o painel e a coloração no browser.

### F4D-05 — Criar a listagem a pedir em português

- `ListingsPage` tem a caixa «Pedir em português». O servidor (`ListingDraftService`) exige acesso
  à empresa, o interruptor da IA ligado nessa empresa e a chave da Anthropic; sem isso devolve a
  razão em português e não chama nada.
- **Privacidade:** ao modelo (`ClaudeListingDrafter`) só vão o pedido e o catálogo de origens e
  campos, com os operadores e as datas relativas; nunca dados da empresa.
- A resposta é uma definição em JSON, validada contra o catálogo (`ListingCatalog.Validate`). Se
  tiver problemas, o modelo recebe-os e tenta uma segunda vez; se continuar mal, o utilizador vê os
  problemas e a explicação, sem definição.
- O rascunho nunca se guarda sozinho: abre no desenhador (`/listagens/nova`) com o aviso de que a IA
  pode enganar-se, e é o utilizador quem corrige e guarda. Os tokens usados vêm na resposta.
- Testes: leitura da resposta (com texto à volta, sem definição, campo inventado), todos os modelos
  do sistema a atravessar o formato da resposta, as guardas do serviço e o fluxo no browser. Não foi
  exercido contra a API real (não há chave configurada nesta máquina).

### F4D-06 — Listagens presas aos ecrãs de registo

- O extrato de uma conta e o extrato de um terceiro têm o botão «Ver em listagem»: abre o modelo
  `extrato-conta` ou `extrato-terceiro` já com os parâmetros no endereço (conta, terceiro, datas
  do filtro do ecrã) e corre-o logo. Os parâmetros do endereço valem para qualquer listagem com
  parâmetros do mesmo nome; `run=1` manda corrê-la ao abrir.
- Novo modelo guiado «Extrato de um terceiro», com o saldo acumulado a cada linha.

- **Ainda por fazer na 4D** (secção 7.2 do documento de mercado): mais origens (terceiros e
  pendentes, IVA, obrigações); colunas calculadas; indicador com semáforo para o painel; subscrição por email; modo especialista.
  Não visto no browser.

## Fase 4E — Gabinete

### F4E-01 — Tarefas atribuíveis

- **Modelo** (`FirmTask`, migração `FirmTasks`, tabela `accounting.FirmTasks`): título, descrição,
  prazo, prioridade (baixa, normal, alta), estado (por fazer, em curso, concluída, cancelada),
  responsável, autor, quem e quando a concluiu, e a origem. A migração acrescenta também as colunas
  de auditoria às listagens guardadas.
- **Acesso** (`FirmTaskService`): uma tarefa é sempre de uma empresa. Vê-a quem tem acesso a essa
  empresa; cria, altera e conclui quem não é só de consulta (a regra vale também no servidor, não só
  no ecrã); o responsável tem de ser membro da empresa; apaga o autor, o dono ou um administrador.
- **Das obrigações:** «Criar das obrigações» cria uma tarefa por cada obrigação pendente em atraso ou
  a vencer nos próximos 15 dias, com o prazo da obrigação, prioridade alta se já passou e entregue ao
  responsável pela empresa, se houver. A origem (`obrigacao:IVA-DP-M:2026-09`) é única por empresa:
  repetir a ação não duplica.
- **Lista** (`/tarefas-gabinete`): filtros por empresa, responsável (todos, minhas, sem responsável),
  estado e pesquisa (sem distinguir maiúsculas, com `%` e `_` como texto); as em atraso primeiro, depois
  por prazo e prioridade; concluir e reabrir de um clique; formulário para criar e editar.
- A página existente «Tarefas» (a fila de trabalho do dia) mantém-se; esta é a das tarefas atribuídas.
- Testes: acessos e papéis, atribuição, ordem, filtros, conclusão e reabertura, apagar, geração sem
  duplicar e o ecrã. Não visto no browser. Ainda por fazer na 4E: ligar as tarefas ao painel do gabinete,
  comentários e histórico das tarefas, notificações, a consola de obrigações em lote (AT-8), o portal do
  cliente e a receção de documentos por email (espera pelos dados do servidor de email).

## Fase 4F — Diferenciação

### F4F-01 — Deteção de anomalias

- **Cinco regras determinísticas** (`AnomalyService`), sobre os lançamentos de um período, sem IA e
  sem alterar nada: possíveis lançamentos duplicados (mesma conta, dia, valor e terceiro, em
  lançamentos diferentes; sem terceiro só a partir de 500 €; as linhas do mesmo par de lançamentos
  dão um achado só); documento repetido (o mesmo documento do mesmo terceiro em mais de um
  lançamento); valores fora do habitual (linhas de gastos e rendimentos de pelo menos 100 € acima da
  média mais quatro desvios-padrão dos 24 meses anteriores e de pelo menos três vezes a média, com
  um mínimo de oito linhas de histórico); linhas nas contas 21 e 22 sem terceiro; e saldos com o
  sinal trocado (cliente credor, fornecedor devedor) à data final.
- Ficam de fora os lançamentos anulados e os de abertura e de apuramento. Cada regra mostra no máximo
  100 achados (o relatório avisa quando truncou), por gravidade e valor.
- **Dispensar** (`AnomalyDismissals`, migração `AnomalyDismissals`): o utilizador dispensa um achado com
  uma razão; a chave é estável (regra e dados em causa), por isso não volta enquanto os dados forem
  os mesmos, e vê-se e repõe-se em «Mostrar os dispensados». Quem só tem acesso de consulta lê e não dispensa.
- Página `/anomalias`: período (o exercício por omissão), resumo por regra, achados por regra com a
  razão à vista e ligação ao lançamento. São pistas para conferir, não erros confirmados.
- Testes: cada regra com os seus dados, o que não conta, o período sem achados, dispensar e repor,
  acesso e validação; o ecrã. Ainda por fazer na 4F: explicação das variações com IA, conversa com memória, ligação a assistentes externos.

### F4F-02 — Papel de trabalho de fecho do mês

- `ClosingChecklistService` + página `/fecho`: para cada mês, o que falta tratar antes de fechar.
  **Automáticas** (calculadas de cada vez): lançamentos por conferir, lançamentos com débito diferente
  de crédito, anomalias por tratar (as da F4F-01), compras do e-Fatura por lançar, vendas por
  contabilizar, documentos arquivados por ligar, movimentos bancários por conciliar, a declaração de
  IVA do período (mensal, ou o trimestre que acaba no mês) e as contas com saldo fora das
  demonstrações. Cada uma diz quantos faltam e leva ao ecrã onde se trata.
- **Manuais** (`ClosingChecks`, migração `ClosingChecks`): seis conferências (saldos bancários com os
  extratos, acréscimos e diferimentos, provisões e imparidades, depreciações, ordenados e encargos,
  resumo enviado ao cliente), com nota, quem e quando; desmarcar volta a pendente.
- «Pronto para fechar» quando nada está por tratar nas automáticas e todas as manuais estão feitas.
  Não bloqueia nada nem fecha o período: é um guia, não um cadeado. Quem só tem consulta vê e não marca.
- Teste do percurso completo: o mês com pendências, o tratamento de cada uma, as marcas, a reabertura,
  outro mês sem marcas, acesso e validação; o ecrã.

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
