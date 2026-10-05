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

## Validação

- .NET: 304 testes. PWA: 124 testes.
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
