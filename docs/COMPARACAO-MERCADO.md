# Comparação com o mercado e plano para estar entre os melhores

- Data: 2026-10-05
- Estado do programa analisado: commit `605df6b`
- Substitui, como diagnóstico, a tabela 3.1 de
  [`ANALISE-NECESSIDADES.md`](ANALISE-NECESSIDADES.md), que está desatualizada.

## 1. Conclusão em uma página

O programa tem uma base contabilística sólida e três coisas em que já está ao
nível do mercado ou perto: a importação de SAF-T, os lançamentos sugeridos com
explicação e os relatórios de gestão com IA. Está atrás em quatro frentes, e é
nelas que se decide se fica «à altura dos melhores»:

1. **Comunicação com a AT: hoje é zero.** Não há nenhuma chamada a
   webservices da AT na plataforma. Os melhores nacionais submetem a DP de
   IVA, a recapitulativa, o Modelo 22 e as obrigações acessórias sem sair do
   programa, e recolhem o comprovativo e a guia de pagamento.
   **O bloqueio que tínhamos deixou de existir:** o certificado de testes e a
   chave pública da AT estão em descarregamento público, e os esquemas XML da
   DP de IVA e do Modelo 22 também (confirmei os quatro ficheiros).
2. **Modelos oficiais: mostramos listas de campos, não os impressos.** O
   Modelo 22 tem só os quadros 07, 09 e 10; a IES só dois quadros do Anexo A;
   a DP de IVA só o quadro 06. Nenhum gera o ficheiro oficial.
3. **Balancete e balanço: funcionam, mas leem-se mal.** O balancete não
   mostra as contas agregadoras nem graus, não separa saldo devedor de credor
   nem mês de acumulado. O balanço sintético tem um defeito provável depois
   do apuramento (ver 3.2).
4. **Motor de listagens e mapas: existe um embrião.** As «Análises» atuais
   têm duas origens de dados, filtros só com «E», limite de 5 000 linhas e
   nenhum gráfico, pivot ou drill-down. Não há mapas por linhas e colunas
   definidos pelo utilizador, que é o que PHC, Primavera, Sage e Eticadata
   oferecem.

**Onde podemos ficar à frente**, e não só igualar: IA com explicação e grau de
confiança visível em cada proposta, «explicar este valor» em qualquer célula
de qualquer mapa, conferência das declarações campo a campo contra a
contabilidade, e ligação a assistentes externos. Nenhum concorrente nacional
tem as quatro.

**Ordem proposta** (detalhe na secção 9): AT primeiro, depois balancetes e
motor de mapas (que partilham o mesmo trabalho), depois os impressos oficiais
sobre esse motor, depois o motor de listagens, depois a gestão de gabinete.

## 2. Como foi feita esta análise

Cinco levantamentos em paralelo, em 2026-10-05:

| Levantamento | Fontes | Limites |
|---|---|---|
| Programa como está | Leitura do código, não da documentação | Nada foi executado; os defeitos apontados em 3.2 estão por confirmar em execução |
| Concorrentes nacionais | Páginas, manuais e centros de ajuda dos fabricantes | Provam que a função é anunciada, não que funciona bem; nenhum produto foi testado |
| Concorrentes internacionais | Documentação oficial e notas de versão | Muita da IA está em beta e sem pt-PT |
| AT e outras entidades | Páginas, WSDL, XSD e PDF oficiais, lidos diretamente | Ver as dúvidas em 4.5 |
| Motores de listagens e mapas | Manuais do PHC e dos outros; documentação dos componentes | Quase não há opinião pública de utilizadores |

**Sobre o «CPOC»:** não existe nenhum programa com esse nome. No PHC, `cpoc`
é o campo da conta de contabilidade. O programa associado aos contabilistas é
o **TOConline**, da Ordem, e foi esse que tratei como referência. Se a
referência era outra, diga-me qual.

**Programas comparados**

- Nacionais: TOConline (e Cegid Business, na mesma plataforma), CentralGest
  (com a Filosoft, que comprou), Cegid Primavera, PHC CS e PHC GO, Sage for
  Accountants / Sage 50 / Sage 100, ARTSOFT, Eticadata, Sendys (e Sendys
  GOFF), Wintouch.
- Internacionais: SAP S/4HANA e Business One, Sage Intacct, Oracle NetSuite,
  Microsoft Business Central, Xero, QuickBooks, DATEV (Alemanha), Pennylane
  (França), a3 e Holded (Espanha), Odoo, e ferramentas de gabinete (Silverfin,
  Karbon, Fathom, Dext, Caseware).

A referência funcional mais próxima do nosso caso não são os ERP
generalistas: é o trio **DATEV, Pennylane e a3**, feito para gabinetes.

## 3. O programa como está

### 3.1 Números

194 endpoints, 62 ecrãs, 291 testes .NET e 124 testes da PWA, 13 migrações.

### 3.2 Defeitos e discrepâncias encontrados no levantamento

A corrigir antes de qualquer funcionalidade nova, porque afetam a confiança
nos números:

| # | Problema | Onde | Gravidade |
|---|---|---|---|
| 1 | O balanço e a DR sintéticos ignoram a classe 8. Depois do apuramento a 31/12, o balanço do próprio exercício deverá aparecer desequilibrado e a DR com resultado zero. As demonstrações oficiais (`/demonstracoes`) tratam o caso. **Por confirmar em execução.** | `/balanco`, `/resultados` | Alta |
| 2 | Quando a API falha, o balancete calcula em silêncio a partir de dados de demonstração; só um selo o indica. | `TrialBalancePage` | Alta |
| 3 | As Análises cortam às 5 000 linhas **antes** de ordenar e somar: os subtotais saem do conjunto truncado sem aviso suficiente. | `AnalysisRunner` | Alta |
| 4 | O saldo de abertura do balancete soma tudo o que é anterior à data inicial, sem respeitar o início do exercício. | `TrialBalanceAsync` | Média |
| 5 | A opção «geração mensal automática» dos relatórios grava-se mas nada a executa: o Worker está vazio. | `FoundationWorker` | Média |
| 6 | O idioma «English» aparece nas Definições sem traduções. | Definições | Baixa |
| 7 | Os esquemas oficiais (SAF-T, dossier fiscal) não estão no repositório, por isso a validação só corre quando alguém os coloca à mão. | `Schemas/`, testes | Média |
| 8 | A importação do plano de contas marca como «de movimento» qualquer conta com 3 ou mais dígitos, mesmo as que o SAF-T diz serem agregadoras. | `SaftAccountsImportService` | Média |

### 3.3 Onde já estamos bem

- Importação de SAF-T de contabilidade e de faturação, testada com um
  ficheiro real de 61,8 MB, com totais a coincidir ao cêntimo.
- Lançamentos sugeridos (compras do e-Fatura e vendas do SAF-T), com estados
  e regras por fornecedor. É o mesmo desenho do TOConline.
- Classificação por histórico e por IA, com explicação e confiança.
- Bancos: camt.053 e CSV, regras por descritivo, conciliação em grupo.
- Arquivo digital com QR lido em PDF e imagem, e o documento ao lado do
  lançamento.
- Relatórios de gestão com IA sobre um dossiê determinístico guardado.
- Mapas 30, 31 e 32 do dossier fiscal com o XML no esquema oficial.
- Calendário de obrigações com a fonte legal de cada prazo, e painel do
  gabinete com carteiras.

## 4. Comunicação com a AT

### 4.1 O que os melhores fazem

| Programa | DP IVA | Modelo 22 | IES | Em lote, para a carteira |
|---|---|---|---|---|
| TOConline | Submete na aplicação | Submete na aplicação | XML para a aplicação da AT | Sim |
| CentralGest | Webservice | Webservice, com «Validar na AT» | Gera, com validações | DMR e Segurança Social |
| Sage for Accountants | Submete | Submete | Prepara e envia | Sim, com comprovativos e email ao cliente |
| ARTSOFT | Direto, com comprovativo e guia | XML ou webservice | Ficheiro | Sim |
| Primavera | Prepara | Ficheiro | Prepara | Num produto à parte (Fiscal Automation) |
| PHC CS | Ficheiro | Ficheiro | Mapas em suporte informático | Não encontrado |
| **Nós** | **Nada** | **Nada** | **Nada** | **Nada** |

O que distingue os melhores, por ordem de valor para o contabilista:

1. Validar na AT antes de submeter, e submeter sem sair do programa.
2. Guardar o comprovativo e a referência de pagamento junto da obrigação.
3. Fazer o mesmo para a carteira inteira, com o estado por empresa.
4. Avisar o cliente por email com o valor a pagar.
5. Cofre de senhas com acesso aos portais sem o técnico conhecer a senha.

### 4.2 O que a AT disponibiliza hoje

Tudo confirmado nas páginas oficiais; os quatro primeiros ficheiros foram
descarregados por mim.

| Obrigação | Canal | Operações | Esquema |
|---|---|---|---|
| DP de IVA | Webservice SOAP | submeter, validar, comprovativo, pagamento, lista de declarações, e as de «IVA Automático» (obter faturas dedutíveis e emitidas, classificar, gerar a declaração) | `dpiva-06.xsd`, 522 campos descritos |
| Recapitulativa | Webservice SOAP | submeter | `dr-1.0` |
| Modelo 22 e anexos A a G e AIMI | Webservice SOAP | submeter, validar | `Modelo22IRCv2026.xsd`, 544 campos descritos |
| IES | Webservice SOAP | submeter, validar | XSD por anexo, quase sem descrições |
| DMR, Modelos 10, 30, 39, 25 | Webservice «Obrigações acessórias» | submeter, validar, consultar, comprovativo, erros, referência de pagamento | Um por modelo |
| Imposto do Selo (DMIS) | Webservice SOAP | — | Sim |
| e-Fatura: consulta de faturas | Webservice `fatshare`, desde outubro de 2025 | Faturas como emitente ou como adquirente | Sim |
| Retenções na fonte, pagamentos por conta | Só formulário ou ficheiro no Portal | — | Webservice não confirmado |
| Segurança Social | REST novo, obrigatório em 2027 segundo fontes secundárias | Remunerações, situação contributiva | OpenAPI |

Padrão comum: SOAP com certificado de cliente do produtor; as senhas vão
cifradas com a chave pública da AT; o contabilista com plenos poderes só
precisa do NIF do cliente. Testes nas portas 7xx, produção nas 4xx.

### 4.3 Factos que mudam o nosso plano

1. **O certificado de testes é público** (`TesteWebservices.pfx`, válido até
   06/01/2027), tal como a chave pública (válida até 15/04/2027). O Bloco C
   estava parado à espera de um email da AT que já não é preciso para
   começar a testar.
2. **O esquema da DP de IVA existe.** O incremento F3-08C estava marcado
   «bloqueado: esquema não encontrado». Está em
   `Suporte_informatico_IVA_2023.zip`.
3. **A importação do e-Fatura pode deixar de ser por ficheiro CSV.** Há
   webservice oficial de consulta. O projeto antigo (WinForms) já tem código
   para ele, por ligar à plataforma nova.
4. **Datas a respeitar:**
   - DP de IVA: modelo novo para períodos a partir de 1 de julho de 2027; o
     WSDL novo só sai no início de 2027.
   - Modelo 10: formato de ficheiro novo a partir de 1 de janeiro de 2027.
   - SAF-T da contabilidade: entrega adiada para os períodos de 2027, a
     entregar em 2028. A IES tem um segundo conjunto de esquemas para essa
     altura.
5. **Não é exigida certificação** a um programa só de contabilidade. Existe
   um selo voluntário (SVAT), que os concorrentes usam como argumento de
   venda, e cuja tabela oficial já usamos nas demonstrações.

### 4.4 Plano

| Passo | O quê | Depende de |
|---|---|---|
| AT-1 | Cliente SOAP comum na plataforma: certificado, cifra das senhas, registo de cada pedido e resposta. Testar a ligação com o certificado público. | Nada |
| AT-2 | DP de IVA: gerar o XML pelo esquema oficial, **validar na AT**, submeter em testes, guardar comprovativo e referência de pagamento na obrigação do calendário. | AT-1 |
| AT-3 | e-Fatura por webservice: compras e vendas diretamente para os lançamentos sugeridos, com agendamento. | AT-1, Worker (9.1) |
| AT-4 | Recapitulativa. | AT-2 |
| AT-5 | Modelo 22: XML, validar e submeter. | AT-1, impresso (secção 5) |
| AT-6 | Obrigações acessórias: Modelo 10 (já no formato de 2027), 30 e 39. | AT-1 |
| AT-7 | IES: XML por anexo, validar e submeter. | Impresso (secção 5) |
| AT-8 | Consola em lote para a carteira: gerar, validar e submeter várias empresas, com estado por empresa e email ao cliente. | AT-2, email (9.1) |
| AT-9 | Produção: pedido de adesão com o certificado próprio. | Decisão sua |

### 4.5 Dúvidas a esclarecer com a AT

1. O certificado de produção serve todos os serviços, ou há uma adesão por
   serviço?
2. As operações de IVA Automático estão abertas a software de terceiros?
3. O WSDL da IES só tem duas operações e o manual descreve mais. Qual vale?
4. Que esquema da IES se aplica ao período de 2026?
5. Há webservice para as retenções na fonte e para a guia de pagamento por
   conta?

## 5. Modelos oficiais

### 5.1 Situação

| Modelo | Temos | Falta | Os melhores |
|---|---|---|---|
| Modelo 22 | Quadros 07, 09 e 10, em lista de campos | Aspeto do impresso; quadros 01 a 06, 08, 11 a 14; anexos; ficheiro; entrega | TOConline: pré-preenchido, anexos automáticos, derrama por município, submissão |
| IES | Anexo A, quadros 03 e 04 | Restantes quadros e anexos; ficheiro; entrega | CentralGest: todos os anexos com deteção de inconsistências |
| DP de IVA | Quadro 06 | Quadros 01 a 05 e 06-A; anexos R, 40 e 41; ficheiro; entrega | PHC: «a mesma experiência da AT» no ecrã |
| Recapitulativa | Quadros 04 e 05 | Ficheiro; entrega | Todos entregam |
| Modelos 10, 30, 39 | Nada (só o mapa de retenções) | Tudo | CentralGest, ARTSOFT, Filosoft |
| Demonstrações SNC | Balanço, DR por naturezas, fluxos de caixa, alterações no capital próprio | DR por funções, Anexo, modelo ESNL | Todos têm o Anexo |

### 5.2 Como reproduzir os impressos

A AT **não publica moldes gráficos**: os PDF oficiais não são preenchíveis.
O impresso tem de ser desenhado por nós, uma vez por modelo e por ano.
Proposta:

1. **Um formato único de «impresso oficial»**: cada modelo é descrito por um
   ficheiro com os quadros, os campos (número, rótulo, tipo), a posição no
   impresso e a regra de cálculo. Os campos e as descrições vêm do esquema
   da AT (o do Modelo 22 traz 544 e o da DP de IVA 522), para os números
   baterem sempre com o ficheiro a entregar.
2. **No ecrã, o aspeto do impresso**, quadro a quadro, com os mesmos números
   de campo e a mesma ordem. Cada campo calculado mostra de onde vem (as
   contas, com ligação ao extrato) e pode ser corrigido à mão, ficando
   assinalado.
3. **O PDF sai igual ao impresso**, gerado no servidor, para o dossier.
4. **O XML sai do mesmo ficheiro de descrição**, validado contra o esquema
   oficial antes de qualquer envio.
5. **Versão por ano**: o modelo de 2026 e o de 2027 coexistem.

As regras de cálculo de cada campo são fórmulas sobre contas. É exatamente o
que o motor de mapas da secção 7 faz, por isso os impressos devem assentar
nele. PHC e ARTSOFT fazem assim: os quadros da IES e do Modelo 22 são mapas
por fórmulas que o utilizador pode ver e ajustar.

### 5.3 Ordem

1. DP de IVA completa (é mensal ou trimestral, é a que mais se usa).
2. Modelo 22 com todos os quadros e os anexos mais comuns (A e D).
3. IES: rosto e anexos A, L, M, O, P e Q.
4. Modelo 10 (formato de 2027), depois 30 e 39.
5. Anexo às demonstrações e DR por funções.

## 6. Balancetes e balanços fáceis de ler

### 6.1 O que temos e o que falta

| Aspeto | Hoje | Os melhores |
|---|---|---|
| Hierarquia | Só contas de movimento; agrupamento opcional pelo 1.º dígito | Árvore do plano com graus, cada nível recolhível (Odoo, NetSuite, Sage Intacct) |
| Colunas | Débito, crédito e um saldo com sinal | Saldo devedor e credor separados; movimento do mês e acumulado; saldo inicial |
| Comparativo | Período homólogo, só com as duas datas | Período anterior, homólogo ou orçamento, com variação absoluta e em percentagem (SAP) |
| Por mês | Não | 12 colunas, uma por mês (Business Central, TOConline) |
| Filtros | Só datas | Diário, centro de custo, terceiro, origem, incluir ou não apuramento |
| Navegação | Conta → extrato → lançamento | Rubrica → contas → extrato → lançamento → **imagem do documento**, guardando onde se estava ao voltar (CentralGest, QuickBooks) |
| Saída | CSV e imprimir pelo browser | Excel e PDF gerados pelo programa |
| Notas | Não | Nota por linha, incluída na impressão (Xero) |

### 6.2 Proposta

**Balancete**

- **Árvore com graus.** Classe, conta de 2 dígitos, de 3, e assim por diante,
  cada nível com a soma das de baixo. Um seletor «Mostrar até ao grau N» e
  setas para abrir e fechar cada ramo. As agregadoras em negrito, as de
  movimento indentadas.
- **Conjuntos de colunas à escolha**, guardados por utilizador:
  - Clássico: saldo inicial, débito, crédito, saldo devedor, saldo credor.
  - Mês e acumulado: movimento do mês e acumulado, cada um com devedor e
    credor.
  - Comparativo: período, comparação, variação e percentagem.
  - Mensal: uma coluna por mês e o total.
- **Filtros** por diário, centro de custo, terceiro e origem, e as opções
  «com ou sem apuramento» e «só contas com saldo».
- **Balancete de terceiros** (clientes e fornecedores) com as mesmas
  colunas.
- **Leitura**: cabeçalho fixo ao descer, primeira coluna fixa ao andar para
  o lado, números alinhados à direita com separador de milhares, negativos
  a vermelho ou entre parênteses (à escolha), zeros apagados, linhas
  alternadas discretas.
- **Sinais de alerta na própria linha**: conta com saldo contrário à sua
  natureza (um banco credor, um fornecedor devedor), conta sem taxonomia,
  conta sem movimento há mais de um ano.
- **Excel e PDF** gerados pelo servidor, com os níveis agrupados no Excel.

**Balanço e demonstração de resultados**

- **Um só balanço**, o do modelo SNC, em vez de dois ecrãs (o sintético e o
  oficial). Cada rubrica abre para as contas que a compõem.
- **Duas colunas de origem**: N e N-1, com variação.
- **Vista «para o empresário»**: as mesmas rubricas com nomes simples e um
  gráfico de estrutura (o que a empresa tem, o que deve, o que é dos
  sócios).
- **Verificações visíveis**: Ativo = Passivo + Capital próprio; o resultado
  da DR igual ao do balanço; contas fora de qualquer rubrica.
- **«Explicar este valor»** em qualquer célula: a fórmula, as contas e os
  saldos que a compõem, em árvore. É o que o PHC tem nos mapas de gestão e
  quase ninguém mais.

Estas melhorias fazem-se melhor **em cima do motor de mapas** da secção
seguinte: o balancete é um mapa cujas linhas são as contas, e o balanço um
mapa cujas linhas são rubricas.

## 7. Motor de listagens e mapas

São três motores diferentes, que convém não misturar.

### 7.1 Motor A — Mapas financeiros por linhas e colunas

É o que serve os balancetes, o balanço, a DR, os mapas de gestão e os
quadros dos impressos oficiais. Referências: Business Central (o melhor
modelo), Sage Intacct, NetSuite, Odoo; em Portugal, PHC, Primavera, Sage e
Eticadata.

**Conceito:** um mapa é o cruzamento de uma **definição de linhas** com uma
**definição de colunas**, guardadas à parte e reutilizáveis.

| Elemento | O que o utilizador define |
|---|---|
| Linha | Código, descrição, nível e formato. Tipo: contas, fórmula, título, total, texto ou «pede valor». |
| Contas da linha | Por prefixo ou intervalo, com exclusões (`61 + 62 exceto 6221`), ou por taxonomia. Nunca uma lista fixa: uma conta nova entra sozinha. |
| Medida | Saldo, saldo devedor, saldo credor, movimento a débito, movimento a crédito. |
| Sinal e condição | Inverter, valor absoluto, «se negativo passa a zero», mostrar só se positivo ou negativo. Regra para mudar de linha conforme o saldo (o banco a descoberto vai para o passivo). |
| Fórmula | Entre linhas, pelo código: `L10 + L20`, intervalos, percentagem de uma linha-base. |
| Coluna | Período ou acumulado; exercício relativo (N, N-1, mesmo mês do ano anterior); mês a mês; orçamento; variação e percentagem; abrir por centro de custo. |

**O que o torna melhor do que os dos concorrentes**

- Editor visual no browser, sem modo de programador (o do Odoo exige-o) e
  sem Excel (Primavera e Sage dependem dele).
- «Explicar este valor» em todas as células.
- **Validação automática**: contas que não entram em nenhuma linha e contas
  que entram em duas. A Primavera e a CentralGest têm isto nos planos
  alternativos; é o que evita balanços que não fecham.
- Modelos de sistema só de leitura (balanço, DR, mapa de exploração,
  quadros dos impressos), que o utilizador duplica e adapta.
- Âmbito à escolha: do gabinete (vale para todas as empresas) ou de uma
  empresa.
- Estados rascunho e ativo, e histórico de versões.
- Pacote de relato: capa, vários mapas, gráficos e notas num só PDF.

### 7.2 Motor B — Listagens e análises (tipo Análises Avançadas do PHC)

**Porque são apreciadas as do PHC:** não têm teto (qualquer pergunta vira
listagem), ficam dentro do programa (no menu, no ecrã do registo, no
painel), a mesma definição serve de listagem, gráfico, indicador e base de
uma tabela cruzada, e navega-se da linha para o registo.

**O que não copiar:** exigem escrever SQL e conhecer a base de dados, as
colunas definem-se por listas de texto, só há um nível de agrupamento, e os
parâmetros entram por substituição de texto no SQL, o que numa aplicação com
várias empresas na mesma base seria uma falha de segurança grave.

**Proposta: o mesmo poder, sem SQL.**

| Prioridade | Funcionalidade |
|---|---|
| 1 | **Catálogo de origens de dados** prontas, com nomes em português: lançamentos e linhas, saldos por conta e mês, terceiros e pendentes, documentos de compra e de venda, IVA, ativos, bancos, arquivo, obrigações. Cada origem já sabe as suas ligações. |
| 1 | Escolher colunas, filtros com «E» e «OU», ordenação, **agrupamento em vários níveis** com subtotais, total geral e coluna de saldo acumulado. |
| 1 | **Parâmetros pedidos ao abrir**: data, período, exercício, conta, terceiro, lista de opções, com valores por omissão relativos («mês anterior»). |
| 1 | **Navegar da linha para o registo** (o lançamento, o terceiro, o documento). |
| 1 | Guardar como privada, da empresa ou do gabinete; duplicar; favoritos e recentes; procura. |
| 1 | Excel e PDF; sem limite de linhas (paginação no servidor). |
| 2 | **Colunas calculadas** com expressões simples (contas, condições, datas). |
| 2 | **Tabela cruzada** (pivot): linhas, colunas e medidas por arrastar. |
| 2 | **Gráfico** associado e **indicador com semáforo** para pôr no painel. |
| 2 | Formatação condicional por regra. |
| 2 | **Modelos guiados**, como no PHC GO: comparação anual, evolução mensal, ranking e análise ABC, extrato. |
| 2 | Análise presa a um ecrã: no terceiro, «as análises deste cliente». |
| 3 | Subscrição por email com agenda. |
| 3 | Correr a mesma análise sobre toda a carteira. |
| 3 | Importar e exportar definições; biblioteca de análises de sistema. |
| 3 | Criar a análise a pedir em português («vendas por cliente em 2025, por mês»), com a IA a montar a definição, que o utilizador vê e corrige. |

Para quem quer mesmo SQL (o seu caso, como utilizador avançado), pode haver
um **modo especialista** restrito ao dono da instalação, só de leitura,
sobre vistas preparadas, com o filtro de empresa imposto pelo servidor,
tempo máximo de execução e registo de cada consulta. É uma decisão sua
(secção 10).

### 7.3 Motor C — Desenho de impressos

É o menos urgente. Proposta: começar por modelos com personalização leve
(logótipo, cores, textos, blocos opcionais), e só depois avaliar um
desenhador livre comprado (DevExpress ou Stimulsoft). Os impressos oficiais
têm layout fixo e não passam por aqui.

### 7.4 Decisões técnicas

| Tema | Proposta | Porquê |
|---|---|---|
| Motor A | Feito por nós | Nenhum produto genérico modela linhas de contas com regras de sinal |
| Motor B, consulta | Camada de origens na API, que gera SQL com parâmetros; o browser nunca envia SQL | É como NetSuite, Sage Intacct e Odoo evitam fugas entre empresas |
| Motor B, grelha | A decidir: AG Grid Enterprise (pago, cerca de 1 000 USD por programador, com pivot e agrupamento prontos) ou grelha própria (grátis, mais trabalho) | Decisão sua (secção 10) |
| PDF | Gerado no servidor (QuestPDF: grátis abaixo de 1 M USD de receita) | Hoje só há «imprimir» do browser |
| Excel | Gerado no servidor, com níveis agrupados e fórmulas de total | Hoje só há CSV |
| A evitar | Metabase, Grafana e ReportBro nas versões abertas | A licença AGPL obriga a abrir o código do produto |

**Segurança**, por ser uma aplicação com várias empresas na mesma base:

- A definição guarda-se como dados; o SQL é sempre gerado pelo servidor.
- O filtro de empresa vem da sessão, nunca do pedido.
- Segunda barreira na base de dados: utilizador só de leitura, regras por
  linha, tempo máximo por consulta.
- A análise corre com os direitos de quem a executa.
- Nada de código, HTML ou CSS escrito pelo utilizador.
- Nas exportações, neutralizar células que comecem por `=`, `+`, `-` ou `@`.

## 8. As outras áreas

### 8.1 Quadro geral

Escala: 0 não existe, 1 embrião, 2 utilizável, 3 ao nível do mercado, 4 ao
nível dos melhores.

| Área | Nós | Mercado nacional | Melhores | O que mais falta |
|---|---|---|---|---|
| Base contabilística | 2 | 3 | 4 | Plano SNC completo de origem; exercício não civil; períodos de regularização; bloqueio por mês; moeda estrangeira; conciliação de pendentes; histórico de alterações |
| Mapas | 2 | 3 | 4 | Secção 6; Anexo; DR por funções |
| Declarações e impressos | 1 | 3 | 4 | Secção 5 |
| Comunicação com a AT | 0 | 3 | 4 | Secção 4 |
| Listagens e mapas do utilizador | 1 | 3 | 4 | Secção 7 |
| Automação de lançamentos | 3 | 3 | 4 | Receção de documentos por email e fotografia; leitura do texto dos documentos sem QR; aprender com as correções |
| Bancos | 2 | 3 | 4 | Ligação direta aos bancos (open banking); mais formatos |
| IA | 3 | 2 | 3 | Conversa com memória; deteção de lançamentos anómalos; explicação de variações |
| Ativos fixos | 2 | 3 | 3 | Tabelas do DR 25/2009; quotas decrescentes |
| Gestão de gabinete | 1 | 3 | 4 | Tarefas atribuíveis; portal do cliente; email; tempos e avenças; cofre de senhas |
| Plataforma | 2 | 3 | 4 | Excel e PDF; cópias de segurança; recuperação de senha e segundo fator; registo de acessos; API documentada |

### 8.2 Notas por área

- **Base contabilística.** O plano de origem tem cerca de 80 contas; uma
  empresa criada à mão fica com um plano incompleto. O histórico de
  alterações (quem mudou o quê num lançamento) é requisito de qualquer
  programa comercial e do selo SVAT.
- **Automação.** O TOConline, a CentralGest e a Sage recebem documentos por
  um endereço de email por empresa e por uma aplicação de telemóvel. Nós só
  aceitamos ficheiros carregados à mão.
- **Bancos.** A CentralGest anuncia ligação a 3 400 bancos. A ligação direta
  exige um contrato com um agregador (um custo recorrente), por isso é uma
  decisão sua.
- **Gestão de gabinete.** É onde a Sage, a Sendys GOFF e o TOConline mais
  investem. O Worker vazio impede tudo o que seja automático: emails,
  agendamentos, recolha diária do e-Fatura.
- **Preço de referência.** O TOConline custa 150 a 225 euros por ano por
  pacote de empresas, com tudo incluído. Qualquer posicionamento comercial
  tem de partir daí.

## 9. Plano proposto

### 9.1 Fundações (primeiro, porque tudo o resto precisa delas)

| # | O quê |
|---|---|
| F-1 | Corrigir os defeitos da secção 3.2. |
| F-2 | Processamento em segundo plano (o Worker): agendamentos, filas, novas tentativas. |
| F-3 | Email de saída. |
| F-4 | Excel e PDF gerados no servidor. |
| F-5 | Histórico de alterações dos lançamentos. |
| F-6 | Cópias de segurança e restauro documentados e testados. |
| F-7 | Os esquemas oficiais descarregados por um script, para a validação correr sempre. |

### 9.2 Fases

| Fase | Conteúdo | Resultado |
|---|---|---|
| 4A — AT | AT-1 a AT-4: cliente SOAP, DP de IVA validada e submetida em testes, e-Fatura por webservice, recapitulativa | Deixamos de estar a zero na frente mais importante |
| 4B — Mapas | Motor A; balancete e balanço novos (secção 6) sobre ele; «explicar este valor» | Balancetes e balanços fáceis de ler; base para os impressos |
| 4C — Impressos | DP de IVA, Modelo 22 e IES no aspeto oficial, com XML e entrega (AT-5 e AT-7); Modelo 10 de 2027 | Modelos oficiais completos |
| 4D — Listagens | Motor B, prioridades 1 e 2 | O equivalente às Análises Avançadas, sem SQL |
| 4E — Gabinete | Consola de obrigações em lote (AT-8); tarefas atribuíveis; portal do cliente; receção de documentos por email | Ao nível da Sage e do TOConline na gestão da carteira |
| 4F — Diferenciação | Conversa com a IA sobre os dados; deteção de anomalias; papéis de trabalho de fecho; ligação a assistentes externos | À frente do mercado nacional |

A 4A e a 4B não dependem uma da outra e podem avançar alternadas. A 4C
depende das duas.

### 9.3 Onde ficar à frente

1. **Confiança visível.** Cada proposta de lançamento com o motivo e o grau
   de confiança, e lançamento automático só das seguras. É o desenho da
   DATEV; em Portugal ninguém o mostra.
2. **«Explicar este valor» em todo o lado**: mapas, impressos, rácios.
3. **Conferência das declarações**: cada campo com os movimentos que o
   compõem e a diferença para a contabilidade. Já o fazemos no quadro 06 do
   IVA; estender a todos os impressos.
4. **Papéis de trabalho de fecho ligados aos números vivos**: se um
   lançamento tardio muda um saldo, a revisão dessa conta volta a «por
   rever» (Silverfin). Nenhum programa nacional o tem.
5. **Explicação de variações** escrita pela IA sobre o comparativo, com as
   contas que mais pesaram (NetSuite lançou-o em 2026).
6. **Porta para assistentes externos** (MCP), só de leitura e com os
   direitos do utilizador. Pennylane, Holded e NetSuite já a têm.

## 10. Decisões que preciso de si

1. **A ordem das fases** da secção 9.2 serve, ou há alguma frente que quer
   primeiro?
2. **Produção na AT:** avanço já com os testes (não precisa de nada seu).
   Para produção é preciso o pedido de adesão com o certificado da sua
   empresa: quer tratá-lo quando a DP de IVA estiver a passar nos testes?
3. **Grelha das listagens:** comprar o AG Grid Enterprise ou fazer uma
   grelha própria?
4. **Modo especialista com SQL** nas listagens, restrito a si: sim ou não?
5. **Salários e faturação** continuam fora de âmbito? Sem salários não há
   DMR nem declaração à Segurança Social, que os concorrentes entregam.
6. **Ligação direta aos bancos:** aceita um custo recorrente com um
   agregador, ou ficamos pelos ficheiros?
7. **O «CPOC»:** confirma que a referência era o TOConline?

## Anexo — Fontes principais

**AT** (base `info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico`)

- Certificado de testes e chave pública: `/Certificados_de_seguranca/`
- Webservices: `/Webservice/` (IVA, IRC, IES, Obrigacoes_acessorias, DMIS,
  e_Fatura)
- Esquemas: `/Formato_de_ficheiros/Contribuintes_e_contabilistas_certificados/Documents/`
- Novo modelo da DP de IVA: Portaria n.º 298/2026/1 e FAQ 00933
- Adiamento do SAF-T da contabilidade: Lei n.º 73-A/2025, art. 95.º
- Selo SVAT: Portaria n.º 293/2017

**Concorrentes nacionais**

- TOConline: `manual.toconline.pt`; preçário em `occ.pt`
- CentralGest: `centralgestcloud.com` (FAQ da Modelo 22 e da DP de IVA por
  webservice)
- Primavera: `pt.primaverabss.com` (Office Extensions, Accounting Automation,
  Fiscal Automation)
- PHC: `helpcenter.phccs.net` (Análises Avançadas, ptxid 20371942; mapas de
  gestão, ptxid 20369330) e `helpcenter.phcgo.net`
- Sage: `sage.com/pt-pt/contabilistas`
- ARTSOFT: `artsoft.pt/tutorial/automatizar-modelo-22`

**Referências internacionais**

- Business Central, Financial Reports: `learn.microsoft.com/dynamics365/business-central/bi-row-definitions`
- Odoo, relatórios configuráveis: `odoo.com/documentation/19.0/applications/finance/accounting/reporting/customize.html`
- NetSuite, Financial Report Builder e Workbook: `docs.oracle.com/en/cloud/saas/netsuite`
- Sage Intacct, Financial Report Writer: `intacct.com/ia/docs`
- DATEV, serviços de automação: `datev.de`
- Pennylane, revisão e IA: `pennylane.com/fr/expert-comptable`
- Silverfin, papéis de trabalho: `silverfin.com/product/working-papers`

Este documento resume os cinco levantamentos; os relatórios completos, com o
URL de cada facto, não foram guardados no repositório. Antes de implementar
cada passo, confirmar de novo a fonte oficial respetiva.
