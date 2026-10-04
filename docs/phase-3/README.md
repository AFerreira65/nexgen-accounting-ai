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
| B | **F3-08** | Declaração periódica de IVA | **Em curso** — [`F3-08`](F3-08-VAT-DECLARATION.md); F3-08A (quadro 06 pelo modelo oficial em vigor, com conferência por conta) feito; faltam o lançamento de apuramento, os anexos e o XML (sem XML-Schema oficial encontrado); modelo novo para períodos a partir de 1 de julho de 2027 |
| B | **F3-09** | Declaração recapitulativa de IVA | **Concluído (sem entrega)** — [`F3-09`](F3-09-RECAPITULATIVE-STATEMENT.md); F3-09A (naturezas intracomunitárias, quadros 04 e 05, campo 7 da declaração periódica) feito; a entrega pelo webservice fica para o Bloco C |
| B | **F3-12** | Ativos fixos e depreciações | **Em curso** — [`F3-12`](F3-12-FIXED-ASSETS.md); F3-12A (cadastro e mapa de depreciações por quotas constantes, com conferência contra a contabilidade) e F3-12B (lançamento das depreciações do exercício, por conferir) feitos; faltam a alienação e o mapa fiscal |
| B | **F3-10** | Modelo 22 (IRC) | **Em curso** — [`F3-10`](F3-10-MODELO-22.md); F3-10A (quadros 07, 09 e 10 pelo impresso oficial, com os valores guardados por exercício) e F3-10B (tributações autónomas por tipo de encargo e prejuízos fiscais reportáveis com o limite de 65 %) feitos; faltam os anexos e a entrega |
| B | F3-11, F3-13, F3-14 | Saídas sem a AT: IES, retenções, dossier fiscal | Por especificar |
| C | F3-15 … F3-17 | Comunicação com a AT | Depende da adesão — [`../ADESAO-AT.md`](../ADESAO-AT.md) |
| D | F3-18 … F3-21 | Automação do lançamento | Por especificar |
| E | **F3-22** | Gráficos | **Em curso** — [`F3-22`](F3-22-CHARTS.md); F3-22A (componente de gráficos e ecrã «Evolução mensal») feito; faltam os gráficos na visão geral e nos relatórios |
| E | **F3-23** | Mapa de exploração e rácios | **Em curso** — [`F3-23`](F3-23-RATIOS-OPERATING-MAP.md); F3-23A (mapa de exploração mensal e rácios com fórmula e período homólogo) feito; falta a idade de saldos |
| E | F3-24 … F3-26 | Gestão: orçamento, assistente com IA, relatório para o empresário | Por especificar |
| F | F3-27 … F3-28 | Gabinete: calendário de obrigações e painel | Por especificar |

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
