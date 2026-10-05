# Fase 3 — Paridade com o mercado e obrigações fiscais

A Fase 2 deixou a plataforma a validar, cruzar e relatar. A Fase 3 leva-a ao
que um programa comercial de contabilidade tem de fazer: produzir as
declarações e os ficheiros para a AT, automatizar o lançamento e dar à gestão
relatórios em texto, gráfico e com IA. O levantamento que a sustenta — o
estado do projeto, os concorrentes e as obrigações com o respetivo canal
técnico — está em [`../ANALISE-NECESSIDADES.md`](../ANALISE-NECESSIDADES.md).

Ficam fora de âmbito, por decisão do utilizador (2026-10-03): a faturação
(exige certificação AT) e o processamento de salários.

## Blocos e épicos

| Bloco | Épico | Título | Estado |
|-------|-------|--------|--------|
| — | **F3-00** | Moldura da aplicação: menu em grupos, recolhível, gaveta em ecrãs estreitos | **Concluído** — [`F3-00`](F3-00-APP-SHELL.md) |
| A | **F3-01** | Terceiros | **Concluído** — [`F3-01`](F3-01-PARTIES.md); F3-01A (cadastro), F3-01B (importação do SAF-T e do e-Fatura), F3-01C (saldos e extrato por terceiro) e F3-01D (ligar os terceiros a lançamentos já importados) |
| A | **F3-02** | Linha de lançamento enriquecida (terceiro, código de IVA, base, documento externo) | **Concluído** — [`F3-02`](F3-02-ENRICHED-ENTRY-LINE.md); épico concluído: F3-02A (terceiro na linha), F3-02B (data do documento) e F3-02C (terceiro na importação de movimentos do SAF-T) |
| A | **F3-03** | Códigos de IVA e lançamentos-tipo | **Concluído** — [`F3-03`](F3-03-VAT-CODES.md) ([`ADR-0012`](../architecture-decisions/ADR-0012-VAT-CODES-ON-ENTRY-LINES.md)); épico concluído: F3-03A (cadastro de códigos), F3-03B (código na linha), F3-03C (resumo de IVA a ler o código da linha) e F3-03D (lançamentos-tipo) |
| A | **F3-04** | Tabelas fiscais por ano | **Em curso** — [`F3-04`](F3-04-FISCAL-TABLES.md); F3-04A (catálogo com fontes, consulta, estimativa de IRC e códigos de IVA) feito |
| A | **F3-05** | Contabilidade analítica | **Concluído (F3-05A)** — [`F3-05`](F3-05-ANALYTIC-ACCOUNTING.md): centros de custo, centro na linha do lançamento e mapa analítico |
| B | **F3-06** | Exportação do SAF-T (PT) de contabilidade | **Concluído** — [`F3-06`](F3-06-SAFT-EXPORT.md); F3-06A (exportador, análise e ecrã) e F3-06B (morada da empresa e dos terceiros) feitos, validados contra o XSD oficial; falta exportar uma empresa real |
| B | **F3-07** | Demonstrações financeiras oficiais (modelos SNC) | **Em curso** — [`F3-07`](F3-07-OFFICIAL-STATEMENTS.md); F3-07A (balanço e demonstração de resultados por naturezas, pela tabela de taxonomias da AT) e F3-07B (fluxos de caixa pelo método direto e alterações no capital próprio, por critério da aplicação) feitos; faltam resultados por funções e anexo |
| B | **F3-08** | Declaração periódica de IVA | **Em curso** — [`F3-08`](F3-08-VAT-DECLARATION.md); F3-08A (quadro 06 pelo modelo oficial em vigor, com conferência por conta) e F3-08B (lançamento de apuramento do IVA) feitos; faltam os anexos e o XML (sem XML-Schema oficial encontrado); modelo novo para períodos a partir de 1 de julho de 2027 |
| B | **F3-09** | Declaração recapitulativa de IVA | **Concluído (sem entrega)** — [`F3-09`](F3-09-RECAPITULATIVE-STATEMENT.md); F3-09A (naturezas intracomunitárias, quadros 04 e 05, campo 7 da declaração periódica) feito; a entrega pelo webservice fica para o Bloco C |
| B | **F3-12** | Ativos fixos e depreciações | **Em curso** — [`F3-12`](F3-12-FIXED-ASSETS.md); F3-12A (cadastro e mapa de depreciações por quotas constantes, com conferência contra a contabilidade) F3-12B (lançamento das depreciações do exercício, por conferir) e F3-12C (alienação e abate com ganho ou perda) feitos; o mapa fiscal (Modelos 32 e 31) está no F3-14; faltam as tabelas do DR 25/2009 |
| B | **F3-10** | Modelo 22 (IRC) | **Em curso** — [`F3-10`](F3-10-MODELO-22.md); F3-10A (quadros 07, 09 e 10 pelo impresso oficial, com os valores guardados por exercício) e F3-10B (tributações autónomas por tipo de encargo e prejuízos fiscais reportáveis com o limite de 65 %) feitos; faltam os anexos e a entrega |
| B | **F3-11** | IES | **Em curso** — [`F3-11`](F3-11-IES.md); F3-11A (Anexo A, quadros 03 e 04, pelos códigos do impresso oficial) feito; faltam o quadro 04-A, os fluxos de caixa, os quadros fiscais, os outros anexos e a entrega |
| B | **F3-13** | Retenções na fonte e declarações de terceiros | **Em curso** — [`F3-13`](F3-13-WITHHOLDINGS.md); F3-13A (mapa de retenções por conta, mês e beneficiário) feito; faltam o Modelo 10, o Modelo 30 e o Imposto do Selo |
| B | **F3-14** | Dossier fiscal | **Concluído** — [`F3-14`](F3-14-FISCAL-DOSSIER.md); F3-14A (índice dos doze elementos da Portaria 92-A/2011, com indícios da contabilidade e estado por exercício) feito; F3-14B (Modelo 32 e Modelo 31 pelos impressos oficiais, a partir do cadastro dos ativos) F3-14C (Modelo 30, da contabilidade com as partes tributadas indicadas) e F3-14D (ficheiro XML dos mapas no esquema oficial DossierFiscal.xsd, validado) feitos |
| C | F3-15 … F3-17 | Comunicação com a AT | Depende da adesão — [`../ADESAO-AT.md`](../ADESAO-AT.md) |
| D | **F3-18** | Lançamentos sugeridos | **Concluído** — [`F3-18`](F3-18-SUGGESTED-ENTRIES.md); F3-18A (sugestões a partir das compras do e-Fatura, com regra por fornecedor e lançamento-tipo, finalizadas por conferir), F3-18B (vendas do SAF-T de faturação, documento a documento, com regra por taxa de IVA) e F3-18C («não lançar» com motivo e mapa de documentos em falta) feitos |
| D | **F3-19** | Bancos | **Concluído** — [`F3-19`](F3-19-BANKS.md); F3-19A (extratos camt.053 e CSV, conciliação automática e manual, mapa de reconciliação) F3-19B (regras por descritivo para lançar a partir do extrato, já conciliado) e F3-19C (conciliação de vários movimentos com vários lançamentos) feitos; fica o Open Banking para mais tarde |
| D | **F3-20** | Arquivo digital | **Em curso** — [`F3-20`](F3-20-DIGITAL-ARCHIVE.md); F3-20A (documentos com SHA-256 ligados aos lançamentos, código QR das faturas pelas especificações da AT, procura do mesmo documento, conservação dos documentos de lançamentos conferidos) F3-20B (código QR lido nos PDF e nas imagens em qualquer browser) e F3-20C (o documento ao lado das linhas do editor, ligado ao guardar) feitos |
| D | **F3-21** | Classificação das compras | **Concluído** — [`F3-21`](F3-21-AI-CLASSIFICATION.md); F3-21A (proposta pelo histórico do fornecedor, com explicação e confiança) e F3-21B (proposta da IA para fornecedores sem histórico, a partir de um dossiê determinístico e validada contra o plano) feitos; aceitar cria o lançamento-tipo e a regra |
| E | **F3-22** | Gráficos | **Concluído** — [`F3-22`](F3-22-CHARTS.md); F3-22A (componente de gráficos e ecrã «Evolução mensal»), F3-22B (gráficos de estrutura e gráfico na visão geral) e F3-22C (gráficos do dossiê nos relatórios de gestão com IA) feitos |
| E | **F3-23** | Mapa de exploração e rácios | **Concluído** — [`F3-23`](F3-23-RATIOS-OPERATING-MAP.md); F3-23A (mapa de exploração mensal e rácios com fórmula e período homólogo) e F3-23B (idade de saldos de clientes e fornecedores) feitos |
| E | **F3-24** | Orçamento | **Concluído** — [`F3-24`](F3-24-BUDGET.md); F3-24A (orçamento anual por conta e centro de custo, com desvios), F3-24B (valores por mês e cópia do orçamento ou do realizado de outro exercício) e F3-24C (o orçamento no mapa de exploração, por rubrica) feitos |
| E | **F3-25** | Assistente de gestão | **Em curso** — [`F3-25`](F3-25-MANAGEMENT-ASSISTANT.md); F3-25A (previsão de tesouraria por critérios à vista: documentos em aberto, prazos e gastos fixos) feito; a conversa já existe na pergunta livre do F2-05; faltam o índice de saúde e a comparação setorial (sem fonte de referência) |
| E | **F3-26** | Relatório para o empresário | **Em curso** — [`F3-26`](F3-26-OWNER-REPORT.md); F3-26A (relatório do mês sem IA: números com o homólogo, posição, frases e alertas factuais, gráficos, impressão) feito; a geração automática mantém a fronteira do F2-05E |
| F | **F3-27** | Calendário de obrigações | **Em curso** — [`F3-27`](F3-27-OBLIGATIONS-CALENDAR.md); F3-27A (prazos confirmados no texto legal, aplicados pelos dados da empresa, com estado, entrega, nota e prorrogação; alerta nas Tarefas) feito; e F3-27B (Imposto do Selo, dossier fiscal e comunicação de faturas, e a opção de mostrar as que os lançamentos não indicam) feitos; faltam os inventários e o RCBE |
| F | **F3-28** | Painel do gabinete | **Em curso** — [`F3-28`](F3-28-FIRM-DASHBOARD.md); F3-28A (todas as empresas, por obrigações em atraso e trabalho por fazer, com a parte dos lançamentos automáticos) feito |

O **Bloco A está fechado** (2026-10-03); o F3-04B ficou resolvido no Modelo 22 (F3-10B). A ordem acordada: Bloco A primeiro; depois F3-06 e F3-07, com os gráficos
(F3-22/23) em paralelo; a seguir o resto do Bloco B; o Bloco C assim que houver
certificado de testes.

## Princípios

Os da [Fase 2](../phase-2/README.md) mantêm-se — incrementos pequenos, gates,
fronteiras de camada, pt-PT, e nada apresentado como «validado pela AT» sem o
processo formal. Acresce:

- **Nenhuma regra fiscal sem fonte.** Taxas, prazos e campos de declaração só
  entram no código depois de confirmados no texto legal ou no manual da AT; o
  que vier de fonte secundária fica assinalado como tal.
- **Validar antes de submeter.** Qualquer envio à AT passa primeiro pela
  operação de validação, e a submissão é um passo explícito do utilizador.
