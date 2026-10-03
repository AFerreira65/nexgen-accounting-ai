# Análise de necessidades — Accounting AI face ao mercado e às obrigações fiscais

Data: 2026-10-03. Base: código no ramo `f2-11a-login-authentication`, pesquisa
web aos concorrentes e à documentação técnica da AT.

Convenção de confiança usada neste documento:

- **[AT]** — lido diretamente em manual ou página da AT.
- **[SEC]** — fonte secundária (OCC, APCMC, consultoras, fabricantes).
- **[NV]** — não verificado; tem de ser confirmado antes de se escrever código.

Nada do que aqui está deve ser codificado como regra fiscal sem reverificação
no texto legal — o princípio «sem regressões silenciosas de conformidade» da
Fase 2 mantém-se.

## 1. Resumo

O projeto tem um núcleo contabilístico sólido (lançamentos, mapas, exercício,
importação e validação de SAF-T, apuramento de IVA, relatórios com IA), mas
**ainda não produz nenhuma declaração nem ficheiro para a AT**, e o modelo de
dados do lançamento é demasiado pobre para as produzir. Um programa comercial
de contabilidade para gabinetes tem, no mínimo, onze capacidades (secção 3.1);
o projeto cobre por inteiro duas e parcialmente três.

Três conclusões mudam o plano que estava em curso:

1. **A AT tem webservices oficiais para quase tudo** [AT]: declaração
   periódica de IVA, recapitulativa, Modelo 22, IES (incluindo obter o
   pré-preenchimento), Imposto do Selo e um serviço de obrigações acessórias
   (DMR, Modelos 10, 25, 30, 39). Só não há webservice para o SAF-T da
   contabilidade, os inventários e o dossier fiscal.
2. **O F2-02C deixou de estar bloqueado pelo contrato técnico.** O WSDL e o
   manual da consulta de faturas (`fatshareFaturas`, v3.0 de out-2025) são
   públicos. O bloqueio que resta é a adesão como produtor de software e o
   certificado de testes — um passo que só o titular pode dar.
3. **A sugestão de 21% na estimativa de IRC está desatualizada**: a taxa geral
   é 19% em 2026 e 15% nos primeiros 50 000 EUR para PME [SEC]. As taxas têm
   de passar a tabelas por ano.

## 2. O que está feito

| Área | Estado |
|---|---|
| Multi-empresa, gabinete, utilizadores com função por empresa, login por sessão | Feito |
| Plano de contas por norma (SNC geral, PE, micro, ESNL, IFRS), taxonomias S e M | Feito |
| Diários, tipos de documento, séries de numeração | Feito |
| Lançamentos: registar, conferir, reabrir, corrigir, anular, duplicar, observações, bloqueio de período | Feito |
| Balancete, razão, livro diário, contas correntes 21/22, CSV, impressão/PDF, modelos de impressão | Feito |
| Demonstração de resultados e balanço | Sintéticos, por subclasse — não são os modelos oficiais |
| Apuramento de resultados, abertura e reabertura de exercício | Feito |
| Importação de SAF-T de contabilidade com reconciliação | Feito |
| Validação XSD e analítica do SAF-T | Feito (falta o drop-in do XSD oficial) |
| Leitura das vendas do SAF-T de faturação | Só leitura e resumo; não gera lançamentos |
| Compras do e-Fatura | Importação de CSV; não gera lançamentos |
| Cruzamento vendas × compras × contabilidade | Feito |
| IVA: classificação por conta, resumo por taxa/natureza, apuramento encadeado | Feito |
| IVA: campos numerados da declaração | Best-effort, fonte não oficial |
| IRC | Só estimativa com taxa e correções manuais |
| Comunicação com a AT | Só certificado e credenciais por empresa; nenhuma chamada na plataforma nova (o monitor WinForms já tem o cliente de consulta de compras, por validar ao vivo) |
| Análises configuráveis | Feito |
| Relatórios de gestão com IA (5 tipos + pergunta livre, dossiê determinístico) | Feito; sem gráficos; geração mensal por fazer |
| Dashboard | Só indicadores numéricos |
| Tarefas | Movimentos por conferir, contas por classificar, lembrete de IVA |
| Auditoria por registo, dicionário de dados | Feito |
| Adaptador PHC alargado (F2-04) | Por especificar |

Limitações estruturais do modelo de dados:

- `JournalEntryLine` tem apenas conta, descrição, débito e crédito. Não tem
  terceiro, código de IVA, base tributável, centro de custo, nem referência e
  data do documento externo.
- Não existe entidade de terceiros (clientes, fornecedores, outros devedores e
  credores). As contas correntes são deduzidas das subcontas 21/22.
- Não há tabelas fiscais por ano (taxas, prazos, versões de declaração).

## 3. O mercado

Produtos analisados: TOConline (OCC), CentralGest, Cegid Primavera / Cegid
Business, Sage for Accountants / 50 / 100, SAP S/4HANA, PHC CS e GO, Filosoft
SNC.32, ARTSOFT, Sendys, Wintouch. Não encontrei referência atual a um produto
«CPOC» da OCC; o produto da Ordem é o TOConline.

### 3.1 Requisito mínimo de mercado vs. projeto

| # | Todos os concorrentes orientados a gabinetes têm | Accounting AI |
|---|---|---|
| 1 | Multi-empresa, plano SNC pré-configurado, diários, lançamentos-tipo | Parcial — faltam lançamentos-tipo |
| 2 | SAF-T de faturação e compras do e-Fatura como origem de lançamentos, com mapa de documentos em falta | Parcial — lê e cruza, não lança |
| 3 | Códigos de IVA no lançamento → declaração periódica pré-preenchida com mapa de conferência; recapitulativa | Parcial — classificação por conta, sem ficheiro nem recapitulativa |
| 4 | Modelo 22 e IES pré-preenchidas por mapeamento conta → campo; SAF-T de contabilidade | Falta |
| 5 | Balanço, DR por naturezas, fluxos de caixa, balancetes, extratos, mapa de exploração | Parcial — faltam modelos oficiais, DFC e mapa de exploração |
| 6 | Contabilidade analítica / centros de custo | Falta |
| 7 | Ativos fixos com depreciações, integração e Modelos 31/32 | Falta |
| 8 | Ligação a salários (DMR AT e SS) | Falta |
| 9 | Reconciliação bancária com importação de extrato | Falta |
| 10 | Apuramentos automáticos de IVA e resultados, abertura | Feito (o apuramento de IVA calcula mas não lança) |
| 11 | Arquivo digital ligado ao lançamento | Falta |

### 3.2 Diferenciadores observados

- **Submissão por webservice além do IVA**: Modelo 22, 30, 10, 39, 25
  (CentralGest, Filosoft, ARTSOFT, Sage). A ARTSOFT valida primeiro no servidor
  de testes da AT.
- **Gestão de obrigações em lote** multi-empresa, com recolha de comprovativos
  e guias e envio ao cliente (Sage, Cegid Fiscal Automation, Sendys GOFF,
  CentralGest).
- **Robot de lançamento**: aprende pré-definidos por fornecedor (CentralGest),
  sugere por CAE (Filosoft), regras por NIF (ARTSOFT). O TOConline usa
  «sugestões» que o contabilista finaliza em lote.
- **Arquivo digital** com QR code e OCR, receção por email e app do cliente.
- **Bancos em tempo real** (Open Banking) com lançamento e reconciliação.
- **IA de gestão**: o TOConline lançou em 27-jul-2026 um assistente com índice
  de saúde financeira 0–100, rácios comparados com o setor (Banco de Portugal),
  chat sobre os números, alertas, previsão e relatório partilhável. O Cegid
  Pulse tem agentes em linguagem natural. A Sage ainda está em early adopters.
- **Fiscalidade fina**: estimativa de IRC ligada ao Modelo 22 no apuramento
  (ARTSOFT, TOConline), derrama por município (Cegid Business), mapa de
  acréscimos e deduções do Quadro 07 (CentralGest), ficha de viaturas para
  tributações autónomas (Filosoft).
- **Gabinete**: tempos por cliente, percentagem de lançamentos automáticos,
  avenças, cofre de senhas.
- **Preço**: o TOConline é o piso — 150 a 225 EUR/ano por pacotes de 5 a 100
  empresas, só para contabilistas certificados.

A matriz completa por produto e as fontes estão no anexo A.

## 4. Obrigações fiscais e canal técnico

Canal: **WS** = webservice SOAP oficial da AT para software de terceiros;
**Ficheiro** = só upload no Portal; **Interno** = não se entrega.

| Obrigação | Prazo legal | Canal | Notas |
|---|---|---|---|
| Declaração periódica de IVA | Dia 20 do 2.º mês seguinte; pagamento até 25 | WS [AT] | `submeterDeclaracao`, `validarDeclaracao`, `obterComprovativo`. Novo modelo (Portaria 298/2026): campos novos desde períodos de jul-2026; WSDL novo só no início de 2027 [SEC] |
| Declaração recapitulativa | Dia 20 do mês seguinte [NV] | WS [AT] | Serviço novo, manual v1.0 de dez-2025 |
| Modelo 22 | 31 de maio (2026: prorrogado a 30-jun) | WS [AT] | Submeter e validar; devolve referência de pagamento. Formato do ficheiro por confirmar |
| IES / Declaração Anual | 15 de julho | WS [AT] | Submeter, validar, comprovativo e **obter pré-preenchimento**. Formato do ficheiro por confirmar |
| SAF-T da contabilidade | Entrega obrigatória só para períodos de 2027, em 2028 [SEC] | Ficheiro | Sem webservice (operações removidas em 2023 [AT]). A capacidade de exportar já é obrigatória e é o que a inspeção pede |
| Consulta de faturas e-Fatura | — | WS [AT] | Como emitente ou adquirente; subutilizador WFA de cada cliente. Não traz a classificação feita no portal |
| DMR (AT), Modelos 10, 25, 30, 39 | Dia 10 / fim de fevereiro / 2.º mês seguinte | WS [AT] | Serviço de obrigações acessórias; operações por modelo a confirmar no PDF |
| Imposto do Selo (DMIS) | Dia 20 do mês seguinte [NV] | WS [AT] | Manual atualizado em jun-2026 |
| Retenções na fonte (guia) | Dia 20 do mês seguinte | Portal [NV] | Não encontrei webservice |
| Comunicação de inventários | 31 de janeiro | Ficheiro [AT] | Valorização obrigatória em jan-2027 só para quem tem inventário permanente |
| Dossier fiscal; mapas 30 (imparidades), 31 (mais-valias), 32 (depreciações) | Até à data da IES | Interno [NV] | Portaria 92-A/2011 |
| Declaração à Segurança Social | Novo regime obrigatório a 01-01-2027 [SEC] | Webservice da SS | Matéria de salários |
| RCBE | 31 de dezembro ou na IES | Manual | — |

Autenticação, igual em todos os serviços [AT]: TLS mútuo com certificado do
produtor de software assinado pela AT, cabeçalho WS-Security com senha cifrada
(AES + RSA). O contabilista certificado com plenos poderes declarativos
submete só com a sua senha e o NIF do cliente; sem plenos poderes são precisas
as duas credenciais. A consulta de faturas é a exceção: exige o subutilizador
WFA de cada cliente.

Dúvida em aberto: não confirmei se um produtor só de contabilidade (sem número
de certificação de faturação) adere sem restrições. Resolve-se no pedido a
`asi-cd@at.gov.pt`. Um programa de contabilidade **não precisa de certificação
AT** enquanto não emitir faturas nem recibos [SEC].

Parâmetros de IRC a pôr em tabelas por ano, todos a reverificar na lei:

- Taxa geral: 20% (2025), 19% (2026), 18% (2027), 17% (2028); PME 15% nos
  primeiros 50 000 EUR desde 2026 [SEC].
- Derrama estadual 3% / 5% / 9% [NV]; derrama municipal até 1,5%, por
  município [NV].
- Tributações autónomas de viaturas 8% / 25% / 32% com escalões de 37 500 e
  45 000 EUR; redução transitória de 15% para PME em 2026 indicada por uma só
  fonte [SEC]; representação 10%.
- Pagamentos por conta em julho, setembro e 15 de dezembro.

O calendário fiscal anual e o detalhe técnico estão no anexo B.

## 5. Lacunas e épicos propostos (Fase 3)

Ordenados por dependência. Cada épico decompõe-se em sub-incrementos como na
Fase 2.

### Bloco A — Fundação de dados (pré-requisito de quase tudo)

| Épico | Conteúdo |
|---|---|
| **F3-01** Terceiros | Entidade com NIF, país, tipo; ligação às subcontas 21/22/27; criação automática a partir do SAF-T e do e-Fatura |
| **F3-02** Linha de lançamento enriquecida | Terceiro, código de IVA, base tributável, referência e data do documento externo; migração sem perder os lançamentos existentes |
| **F3-03** Códigos de IVA e lançamentos-tipo | Tabela de códigos (taxa, natureza, região, campo da declaração, dedutibilidade); modelos de lançamento que calculam o IVA |
| **F3-04** Tabelas fiscais por ano | Taxas, escalões, prazos, versões de declaração; corrige a sugestão de 21% |
| **F3-05** Contabilidade analítica | Centros de custo na linha, balancete e extratos analíticos |

### Bloco B — Saídas que não dependem da adesão à AT

| Épico | Conteúdo |
|---|---|
| **F3-06** Exportação do SAF-T de contabilidade | XML 1.04_01 com taxonomias, validado pelo validador que já existe |
| **F3-07** Demonstrações financeiras oficiais | Balanço e DR por naturezas nos modelos SNC por norma, a partir das taxonomias; DFC, DACP; DR por funções; anexo |
| **F3-08** Declaração periódica de IVA | Campos oficiais, anexos R, regularizações 40/41, mapa de conferência, lançamento de apuramento, XML |
| **F3-09** Declaração recapitulativa | A partir dos terceiros e dos códigos de IVA |
| **F3-10** Modelo 22 | Quadro 07 com mapa de acréscimos e deduções, prejuízos, cálculo do imposto, derramas, tributações autónomas, pagamentos por conta, ficheiro; a estimativa de IRC passa a alimentar o apuramento |
| **F3-11** IES | Anexo A (e I, L, M, O, P conforme os dados), mapas de conferência por quadro, ficheiro |
| **F3-12** Ativos fixos | Fichas, depreciações com tabelas do DR 25/2009, lançamento automático, abates e alienações, mapas 32 e 31 |
| **F3-13** Retenções e declarações de terceiros | Retenções na fonte, Modelo 10, Modelo 30, Imposto do Selo |
| **F3-14** Dossier fiscal | Índice e mapas da Portaria 92-A/2011, incluindo o mapa 30 |

### Bloco C — Comunicação com a AT (depende da adesão)

| Épico | Conteúdo |
|---|---|
| **F3-15** Cliente SOAP comum | WS-Security da AT, dois atores, TLS mútuo, ambientes de teste e produção; fecha o F2-02C |
| **F3-16** Consulta de faturas | Compras e vendas do e-Fatura por webservice para a plataforma nova |
| **F3-17** Validar e submeter | Por esta ordem: IVA, recapitulativa, Modelo 22, IES (com pré-preenchimento), obrigações acessórias, DMIS; comprovativos e guias guardados |

Mantém-se a decisão já tomada: primeiro só validar, nunca submeter; a
submissão é um passo explícito do utilizador, por declaração.

### Bloco D — Automação do lançamento

| Épico | Conteúdo |
|---|---|
| **F3-18** Lançamentos sugeridos | A partir do e-Fatura e do SAF-T de faturação, com regras por NIF e por conta; estados sugestão → finalizado; mapa de documentos em falta |
| **F3-19** Bancos | Importação de extrato (CSV, ISO 20022 camt.053), reconciliação, lançamento a partir do extrato; Open Banking mais tarde |
| **F3-20** Arquivo digital | Documento anexo ao lançamento, leitura do QR code da fatura, conservação conforme o DL 28/2019 |
| **F3-21** Classificação com IA | Sugestão de conta e código de IVA a partir do histórico do fornecedor, com explicação e sempre sujeita a confirmação |

### Bloco E — Gestão: texto, gráfico e IA

| Épico | Conteúdo |
|---|---|
| **F3-22** Gráficos | Componente de gráficos partilhado; dashboard com evolução mensal de rendimentos, gastos, resultado, tesouraria, clientes e fornecedores; gráficos nos relatórios |
| **F3-23** Mapa de exploração e rácios | Mapa mensal por naturezas, rácios (liquidez, autonomia, rendibilidade, prazos médios), idade de saldos |
| **F3-24** Orçamento | Orçamento por conta e centro de custo, desvios |
| **F3-25** Assistente de gestão com IA | Conversa sobre os números com ligação à origem, índice de saúde, alertas, previsão de tesouraria, comparação setorial com dados do Banco de Portugal |
| **F3-26** Relatório para o empresário | Relatório mensal automático (fecha o F2-05E) com texto e gráficos, exportável |

### Bloco F — Gabinete

| Épico | Conteúdo |
|---|---|
| **F3-27** Calendário de obrigações | Por empresa, gerado das tabelas fiscais e da periodicidade; estado, comprovativo, alerta nas Tarefas |
| **F3-28** Painel do gabinete | Todas as empresas: obrigações pendentes, movimentos por conferir, percentagem de lançamentos automáticos |

### Fora de âmbito proposto

- **Faturação**: exige certificação AT (Portaria 363/2010). Fica fora, como já
  decidido na Fase 2.
- **Salários**: é um produto inteiro e a Segurança Social muda de regime em
  2027. Proposta: importar o resumo do processamento para lançar e alimentar a
  DMR e o Modelo 10, sem processar salários.
- **Consolidação de contas** e **orçamental pública**: sem procura no perfil
  de PME.

## 6. Sugestões

1. **O dossiê determinístico é o melhor ativo para IA.** Os concorrentes
   anunciam IA, mas nenhum mostra proveniência até ao lançamento. Estender o
   mesmo padrão (só o dossiê chega ao modelo, tudo com ligação à origem) ao
   chat, à classificação de documentos e às declarações é o diferenciador mais
   defensável.
2. **Validar no ambiente de testes da AT antes de submeter**, como faz a
   ARTSOFT, e guardar o resultado junto da declaração.
3. **Usar os plenos poderes do contabilista** em vez de guardar a senha de
   cada cliente sempre que o serviço o permita; a consulta de faturas é a
   única que obriga ao subutilizador do cliente.
4. **Conferência IA das declarações**: antes de gerar o ficheiro, um relatório
   que explica cada campo pelo razão e assinala o que mudou face ao período
   anterior.
5. **Preparar já a migração do IVA para 2027**: o catálogo de campos deve ser
   versionado por período de imposto.
6. **Rever o OE 2027** quando sair (outubro de 2026): pode voltar a adiar o
   SAF-T da contabilidade e a assinatura qualificada das faturas.

## 7. Ordem recomendada

1. Bloco A (F3-01 a F3-04) — sem isto, as declarações não têm dados.
2. F3-06 (SAF-T) e F3-07 (demonstrações oficiais) — obrigação legal já em
   vigor e base da IES.
3. F3-22 e F3-23 (gráficos e rácios) em paralelo — independentes e visíveis.
4. F3-08 e F3-09 (IVA e recapitulativa), depois F3-12 (ativos), F3-10
   (Modelo 22) e F3-11 (IES).
5. Bloco C assim que houver certificado de testes; o pedido de adesão deve
   seguir já, porque o prazo de resposta da AT não se controla.
6. Blocos D, E (restante) e F.

## 8. A confirmar antes de codificar

- Texto da Lei 73-A/2025: adiamento do SAF-T e redução das tributações
  autónomas para PME.
- Escalões da derrama estadual e tabela anual da derrama municipal.
- Formato dos ficheiros do Modelo 22 e da IES (páginas de suporte informático
  de cada declaração).
- Operações suportadas por modelo no webservice de obrigações acessórias.
- Campos oficiais da declaração periódica (o catálogo atual vem de um manual
  pedagógico) e o WSDL novo de 2027.
- Elegibilidade de um produtor só de contabilidade para os webservices.

## Anexo A — Concorrentes: fontes

- TOConline: toconline.pt/funcionalidades/contabilidade,
  manual.toconline.pt, toconline.pt/novidades/assistente-de-gestao-com-ia,
  occ.pt (preçário 2026).
- CentralGest: centralgestcloud.com (funcionalidades, software de
  contabilidade online, FAQ Modelo 30 por webservice),
  centralgest.com/software/contabilidade/declaracoes-legais-e-fiscais.
- Cegid: pt.primaverabss.com (ERP Evolution for Accountants, Accounting
  Automation, Cegid Docs), cegid.com (Fiscal Automation),
  help.business-pt.cegid.cloud (Modelo 22, IES).
- Sage: sage.com/pt-pt/contabilistas/sage-for-accountants, brochura Sage for
  Accountants Expert, partnews.sage.pt.
- SAP: help.sap.com (User Guide for SAF-T Portugal), notas 3495027 e 2926588.
- PHC: phcsoftware.com (módulo CS Contabilidade, PHC GO Accounting).
- Filosoft: filosoft.pt/pt/solucao/6-snc-32. ARTSOFT:
  artsoft.pt/software-contabilidade. Sendys: sendys.pt, sendysgoff.pt.
  Wintouch: wintouch.pt/produtos/gestao-de-contabilidade.

Funcionalidades confirmadas por produto, em resumo:

| Funcionalidade | TOConline | CentralGest | Cegid | Sage | Filosoft | ARTSOFT |
|---|---|---|---|---|---|---|
| Lançamentos a partir de SAF-T de faturação e e-Fatura | Sim | Sim | Sim | Sim | Sim | Sim |
| Extratos bancários e reconciliação | Sim | Sim | Sim | Add-on | Sim | Sim |
| Arquivo digital | Sim | Sim (QR e OCR) | Produto à parte | Add-on | Sim (QR e OCR) | Sim (QR) |
| IVA submetido do programa | Sim | Sim | Produto à parte | Sim | Sim | Sim |
| Modelo 22 | Sim | Webservice | Sim | Webservice | Webservice | Webservice |
| IES | XML para a aplicação da AT | Sim | XML | Sim | Sim | Sim |
| Modelos 10 / 30 / 39 | 10 | Todos | Parcial | Parcial | Todos | Todos |
| Modelos 31 e 32 | Sim | Sim | Sim | Sim | Não confirmado | Sim |
| IA de análise | Sim (jul-2026) | Não confirmado | Cegid Pulse | Em early adopters | Não | Não |
| Gestão de obrigações | Tarefas | Sim | Produto à parte | Add-on | Parcial | Sim |

## Anexo B — AT: endpoints e calendário

Endpoints de produção em `servicos.portaldasfinancas.gov.pt` (testes: a mesma
porta com 7 em vez de 4) [AT]:

| Serviço | Porta e caminho |
|---|---|
| Declaração periódica de IVA | `:406/dpivaws/DeclaracaoPeriodicaIVAWebService` |
| Declaração recapitulativa | `:426/driva/ws` |
| Modelo 22 | `:405/dm22ircws/SubmeterDeclaracaoIRCService` |
| IES | `:407/iesws/SubmeterDeclaracaoIESService` |
| Obrigações acessórias | `:418/oaatws/GestaoDeclarativaOAWebService` |
| DMIS | `:421/DmisServiceImplService` |
| Consulta de faturas | `:425/fatshare/ws/fatshareFaturas` |

Subutilizadores por serviço [AT]: IES — TIE (contabilista) / IES
(contribuinte); Modelo 22 — TRC / IRC; IVA — NIF do contabilista / WVA;
recapitulativa — WDR; obrigações acessórias — TOA; e-Fatura — WFA.

Os manuais estão em
`info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/`.

Calendário de uma PME em regime geral, IVA mensal, ano civil (datas legais;
passam ao dia útil seguinte e há prorrogações frequentes — os prazos têm de
ser editáveis):

- **Mensal**: dia 5, comunicação de faturas; dia 10, DMR; dia 20, retenções,
  Imposto do Selo, declaração periódica de IVA do 2.º mês anterior e
  recapitulativa; dia 25, pagamento do IVA.
- **Janeiro**: dia 31, inventários.
- **Fevereiro**: Modelos 10, 39 e 25.
- **Março**: aprovação de contas até dia 31.
- **Maio**: dia 31, Modelo 22 e pagamento.
- **Julho**: 1.º pagamento por conta; dia 15, IES e dossier fiscal.
- **Setembro**: 2.º pagamento por conta.
- **Dezembro**: dia 15, 3.º pagamento por conta; dia 31, RCBE.
- **IVA trimestral**: declaração até 20 de maio, agosto, novembro e fevereiro.
