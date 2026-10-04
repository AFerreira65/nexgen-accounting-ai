# F3-13 — Retenções na fonte e declarações de terceiros

Épico do Bloco B da [Fase 3](README.md). Quem paga honorários, rendas ou
salários retém imposto, entrega-o ao Estado no mês seguinte e declara-o por
beneficiário (DMR, Modelo 10). Na aplicação as retenções só existiam como
movimentos da conta 242.

## Sub-incrementos

### F3-13A — Mapa de retenções na fonte (feito)

- **`WithholdingService`** — só lê; exclui os anulados. Parte das contas
  **242** («Retenção de impostos sobre rendimentos»):

| Vista | Regra |
|---|---|
| Por conta | O que vinha por entregar (lançamento de abertura), o retido no exercício (créditos), o entregue ao Estado (débitos) e o que fica por entregar. |
| Por mês | O retido e o entregue em cada mês, por conta — o retido num mês é o que se entrega no seguinte. |
| Por beneficiário | O retido por terceiro e por conta. O beneficiário é o terceiro da linha da retenção ou, se a linha não o tem, o **único** terceiro do lançamento. |
| Rendimento | Uma **estimativa**: os gastos (classe 6) dos mesmos lançamentos. Só é calculada quando o beneficiário está identificado. |
| Sem terceiro | As retenções de lançamentos sem terceiro, ou com mais de um, ficam numa linha própria e num total — é o caso normal dos salários processados em conjunto. |

- **A natureza do rendimento não é inferida.** O SNC só normaliza a conta
  242; as subcontas (trabalho dependente, independente, prediais…) são de
  cada plano de contas, pelo que o mapa mostra a conta com o nome que a
  empresa lhe deu, em vez de adivinhar a categoria.
- **API:** `GET .../withholdings?year=`.
- **PWA:** ecrã **«Retenções na fonte»** (`/retencoes`, grupo «Fiscal») — os
  totais do exercício, as três vistas, o aviso das retenções sem terceiro,
  exportação dos beneficiários em CSV e impressão.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `WithholdingServiceTests` — saldo de
  abertura, retido e entregue por conta e por mês, beneficiário herdado do
  lançamento e beneficiário na linha, estimativa do rendimento, salários sem
  terceiro, lançamento anulado e de outro exercício de fora.
- `NexGenAccountingAI.Api.Tests`: o endpoint e a recusa de um exercício inválido.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 100 testes, `vite build`.
- A suite completa: 251 testes .NET.

**Por verificar**

- Com uma **empresa real**: os lançamentos importados de um SAF-T só têm
  terceiro se a ligação de terceiros (F3-01D) os tiver encontrado.

**Pressupostos a confirmar com um contabilista**

- Todo o crédito numa conta 242 é uma retenção e todo o débito uma entrega;
  uma regularização a débito conta como entrega.
- A estimativa do rendimento ignora os lançamentos em que a fatura e a
  retenção estão em lançamentos diferentes.

**O que não faz**

- A declaração mensal de retenções, a DMR e o **Modelo 10**: faltam a
  categoria e o código de cada rendimento, que a contabilidade não tem.
- O Modelo 30 (pagamentos a não residentes) e o Imposto do Selo (DMIS).

### A seguir

- **F3-13B** — categoria e código do rendimento por conta 242 (configuração)
  e o Modelo 10 a partir do mapa por beneficiário.
- **F3-13C** — Imposto do Selo e Modelo 30.
