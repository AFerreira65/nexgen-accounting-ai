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
| A | **F3-01** | Terceiros | **Em curso** — [`F3-01`](F3-01-PARTIES.md); F3-01A (cadastro) e F3-01B (importação do SAF-T e do e-Fatura) feitos |
| A | F3-02 | Linha de lançamento enriquecida (terceiro, código de IVA, base, documento externo) | Por especificar |
| A | F3-03 | Códigos de IVA e lançamentos-tipo | Por especificar |
| A | F3-04 | Tabelas fiscais por ano | Por especificar |
| A | F3-05 | Contabilidade analítica | Por especificar |
| B | F3-06 … F3-14 | Saídas sem a AT: SAF-T, demonstrações oficiais, IVA, recapitulativa, Modelo 22, IES, ativos fixos, retenções, dossier fiscal | Por especificar |
| C | F3-15 … F3-17 | Comunicação com a AT | Depende da adesão — [`../ADESAO-AT.md`](../ADESAO-AT.md) |
| D | F3-18 … F3-21 | Automação do lançamento | Por especificar |
| E | F3-22 … F3-26 | Gestão: gráficos, rácios, orçamento, assistente com IA | Por especificar |
| F | F3-27 … F3-28 | Gabinete: calendário de obrigações e painel | Por especificar |

A ordem acordada: Bloco A primeiro; depois F3-06 e F3-07, com os gráficos
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
