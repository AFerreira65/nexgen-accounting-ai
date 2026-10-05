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

## Validação

- .NET: 300 testes. PWA: 124 testes.
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
