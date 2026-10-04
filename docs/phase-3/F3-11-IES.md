# F3-11 — IES (Informação Empresarial Simplificada)

Épico do Bloco B da [Fase 3](README.md). A IES é a declaração anual que junta
a prestação de contas, a informação fiscal e a estatística. O **Anexo A** é o
das entidades residentes que exercem, a título principal, atividade
comercial, industrial ou agrícola — e os seus dois primeiros quadros de
valores são as demonstrações financeiras que a aplicação já calcula
([F3-07](F3-07-OFFICIAL-STATEMENTS.md)).

## Fonte

IES / Declaração Anual — **Anexo A, modelo em vigor para declarações do
período de 2019 e seguintes**, consultado em 2026-10-04 em
`info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/modelos_formularios/decl_anual_inf_contabilistica_fiscal/Documents/IES_DA_Anexo_A_2019_e_seguintes.pdf`:
os códigos dos campos (`A00001` a `A00027` no quadro 03, `A00101` a `A00158`
no quadro 04), as fórmulas dos subtotais do quadro 03 e os normativos a que
cada campo se aplica.

## Sub-incrementos

### F3-11A — Anexo A, quadros 03 e 04 (feito)

- **`IesAnnexAService`** — só lê. Parte da demonstração de resultados do
  exercício e do balanço a 31 de dezembro e leva cada rubrica ao campo do
  impresso **com a mesma designação**:

| Parte | Regra |
|---|---|
| Quadro 02-A | O normativo pela norma contabilística da empresa: `S` NCRF, `P` NCRF-PE, `M` NC-ME, `N` NIC. |
| Quadro 03 | Rendimentos positivos; **gastos com valor positivo**, porque o impresso os subtrai (as reversões saem negativas). Subtotais A00018, A00021, A00024 e A00026 pelas fórmulas do impresso. |
| Quadro 04 | As rubricas do balanço por secção; somas A00112, A00125, A00136, A00145 e A00156; totais A00126, A00139, A00157 e A00158. |
| Campos | Só os que têm valor. |

- **Avisos**, que são o mapa de conferência do anexo: rubrica sem campo no
  impresso; total do ativo diferente do total do capital próprio e do
  passivo; resultado líquido do quadro 03 diferente do do balanço ou do da
  demonstração de resultados; contas com saldo fora das demonstrações;
  empresa em normas internacionais.
- **API:** `GET .../ies/annex-a?year=`.
- **PWA:** ecrã **«IES — Anexo A»** (`/ies`, grupo «Fiscal») — o normativo,
  os dois quadros com o código de cada campo, os avisos, CSV e impressão.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`CashFlowAndEquityStatementsTests`): os
  campos dos dois quadros para o cenário das demonstrações (gastos positivos,
  subtotais, somas, totais iguais, resultado igual nos dois quadros, sem
  avisos).
- `NexGenAccountingAI.Api.Tests`: o endpoint e a recusa de um exercício inválido.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 99 testes, `vite build`.
- A suite completa: 250 testes .NET.

**Por verificar**

- Com uma **empresa real**, comparando com a IES entregue.
- A correspondência código ↔ designação foi lida por extração automática do
  PDF do impresso; merece uma leitura humana.
- O **sinal** das ações (quotas) próprias (A00128) e dos dividendos
  antecipados (A00138): a aplicação envia o valor com o sinal do balanço
  (negativo); o impresso pode esperá-lo positivo.

**O que não faz**

- Só o período N; não há coluna do período anterior nestes quadros.
- A folha de rosto, o quadro 04-A (alterações no capital próprio), os fluxos
  de caixa, o anexo às demonstrações (PDF), os quadros de informação fiscal e
  estatística (05 em diante) e os outros anexos (I, L, M, O, P, Q, R).
- O ficheiro da declaração e a entrega (Bloco C; o WSDL está em
  `docs/at-wsdl/ies/`).

### A seguir

- **F3-11B** — quadro 04-A (demonstração das alterações no capital próprio)
  e quadro dos fluxos de caixa, que a aplicação já calcula com outra
  estrutura de linhas.
- **F3-11C** — anexos de IVA (L, M) e de operações com terceiros (O, P), a
  partir dos terceiros e dos códigos de IVA.
