# F3-27 — Calendário de obrigações

Épico do Bloco F da [Fase 3](README.md). O levantamento listava o calendário
de uma PME, mas com vários prazos por verificar ([NV]). O calendário só usa
prazos confirmados no texto legal.

## Sub-incrementos

### F3-27A — Calendário por empresa, com fonte e estado (feito)

- **`ObligationCatalog`** (`Application/Fiscal`): as obrigações, cada uma com
  a regra do prazo e a fonte. Os textos foram confirmados no Portal das
  Finanças, consultado em 2026-10-05.

| Obrigação | Prazo | Fonte |
|---|---|---|
| Declaração periódica de IVA, mensal / trimestral | Dia 20 do 2.º mês seguinte ao mês / trimestre | CIVA, art. 41.º, n.º 1, a) e b) (redação da Lei n.º 12/2022) |
| Pagamento do IVA, mensal / trimestral | Dia 25 do 2.º mês seguinte | CIVA, art. 27.º, n.º 1, a) e b) |
| Declaração recapitulativa, mensal / trimestral | Dia 20 do mês seguinte ao mês / trimestre | RITI, art. 30.º, n.º 1 (n.º 2: acima de 50 000 € no trimestre, passa a mensal) |
| Entrega das retenções na fonte | Dia 20 do mês seguinte | CIRS, art. 98.º, n.º 3 |
| Declaração mensal de remunerações | Dia 10 do mês seguinte | CIRS, art. 119.º, n.º 1, c), i) |
| Modelo 10 | Final de fevereiro do ano seguinte | CIRS, art. 119.º, n.º 1, c), ii) |
| Modelo 22 e pagamento do IRC | Último dia de maio, seja ou não útil | CIRC, arts. 120.º, n.º 1, e 104.º, n.º 1, b) |
| IES | 15 de julho, seja ou não útil | CIRC, art. 121.º, n.º 2 |
| Pagamentos por conta | Julho, setembro e 15 de dezembro do próprio ano | CIRC, art. 104.º, n.º 1, a) |

  **Ficam de fora** até se confirmar o prazo em vigor: a comunicação dos
  elementos das faturas e a dos inventários (DL n.º 198/2012), o Imposto do
  Selo, o dossier fiscal e o RCBE. As datas são **as legais**; quando calham em
  dia não útil ou há prorrogação, altera-se o prazo à mão nesse período.
- **`Domain/Organizations/TaxObligationRecord`**: o que se regista por
  obrigação e período. Guarda o estado (pendente, entregue com data, ou
  dispensada), uma nota (o comprovativo ou a referência de pagamento) e o
  prazo alterado. É auditável. Migração aditiva
  `20261004232941_TaxObligationRecords`.
- **`ObligationCalendarService`** decide pelos dados que obrigações se aplicam
  em cada período:
  - **IVA:** pela periodicidade das credenciais da AT (F2-02A); sem
    credenciais, assume o regime mensal e o ecrã avisa.
  - **Recapitulativa:** quando há linhas com as naturezas intracomunitárias
    13, 14 ou 15 no período.
  - **Retenções:** quando há créditos nas contas 242 no mês.
  - **Declaração mensal de remunerações:** quando há gastos nas contas 63 no
    mês.
  - **Modelo 10:** quando houve retenções no ano.
  - **Modelo 22, IES e pagamentos por conta:** sempre.

  Um período com estado registado aparece mesmo que os dados não o peçam. Um
  prazo alterado pode tirar o período da janela ou trazê-lo para ela.
- **API:** `GET .../obligations?from=&to=` (até 400 dias) e
  `PUT .../obligations/{código}/{período}` (`{ state, deliveredOn, note, dueDateOverride }`).
- **PWA:**
  - **Ecrã «Calendário de obrigações»** (`/obrigacoes`, grupo «Fiscal»):
    - o período e os totais (entregues e em atraso);
    - cada obrigação com o prazo (e o legal, se foi alterado), o motivo por
      que se aplica, a fonte e o estado;
    - os botões «Registar» (data de entrega, nota e prazo), «Dispensar» e
      «Voltar a pendente»;
    - a impressão.
  - **«Tarefas»:** ganham as obrigações pendentes em atraso ou com prazo nos
    próximos 15 dias.

**Validação efetuada**

- `ObligationCatalogTests`:
  - os prazos legais de cada obrigação, incluindo o 4.º trimestre, que passa
    para o ano seguinte, e um fevereiro de ano bissexto;
  - os pagamentos por conta do próprio ano;
  - os períodos que vencem numa janela, por ordem;
  - a fonte em todas.
- `ObligationCalendarServiceTests` (PostgreSQL):
  - **Aplicabilidade:** IVA mensal por omissão; DMR, retenções e
    recapitulativa só no mês com operações; o trimestral não aparece; atraso.
  - **Registo:** entregue com data e nota; prorrogação do Modelo 22, que sai
    de maio; entregue sem data, estado e obrigação inválidos.
  - **Janela e acesso:** janela longa demais; sem acesso.
- `NexGenAccountingAI.Api.Tests`: o calendário de maio e «entregue» sem data.
- PWA: rota e estado sem base de dados.

### Por fazer

- Confirmar e acrescentar a comunicação de faturas, a de inventários, o
  Imposto do Selo, o dossier fiscal e o RCBE.
- Passar ao dia útil seguinte com um calendário de feriados, com a regra
  legal da contagem.
- Ligar o comprovativo ao arquivo digital (F3-20) e marcar «entregue» a
  partir do Bloco C (submissão pela AT).
