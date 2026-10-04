# F3-26 — Relatório para o empresário

Épico do Bloco E da [Fase 3](README.md). Os mapas de gestão (F3-22 a F3-24)
servem o contabilista. O empresário precisa do mês num ecrã: o que vendeu, o
que gastou, quanto ganhou, quanto tem e o que merece atenção.

## Sub-incrementos

### F3-26A — Relatório do mês, sem IA (feito)

- **`OwnerReportService`** junta o que os mapas já calculam: a evolução
  mensal (F3-22A), a idade dos saldos (F3-23B) e o orçamento (F3-24). Escreve
  o resultado em frases simples, com alertas factuais. Não precisa de chave de
  IA nem chama o modelo.

| Parte | Conteúdo |
|---|---|
| O mês | Rendimentos, gastos e resultado do mês, com o mesmo mês do ano anterior e a variação. |
| O ano | Os mesmos valores acumulados até ao mês, contra o mesmo período do ano anterior. |
| A posição | A tesouraria no fim do mês, com quantos meses de gastos médios cobre; os clientes por receber, com o que tem mais de 90 dias; e os fornecedores por pagar. |
| Frases | Por exemplo: «Em março de 2026, os rendimentos foram de 500,00 €, menos 50 % face ao mesmo mês do ano anterior.» Há também frases sobre o resultado, a posição e, quando há orçamento, o realizado contra o previsto. |
| Alertas | Prejuízo no mês; rendimentos mais de 20 % abaixo do homólogo; tesouraria abaixo de um mês de gastos médios; clientes com mais de 90 dias; até três desvios desfavoráveis de 10 % ou mais no orçamento. São **critérios da aplicação**, assim indicados no ecrã, e não referências setoriais. |

- **API:** `GET .../management/owner-report?year=&month=`.
- **PWA:** ecrã **«Relatório do mês»** (`/relatorio-empresario`, grupo
  «Gestão») com:
  - o exercício e o mês;
  - as frases e os alertas;
  - os três blocos de números;
  - quatro gráficos: rendimentos contra o ano anterior, resultado e
    tesouraria, e a idade dos saldos de clientes e de fornecedores;
  - a impressão ou o PDF, sem os controlos;
  - a ligação à síntese com IA dos relatórios de gestão (F2-05).

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`OwnerReportServiceTests`, em PostgreSQL):
  - **Números:** os do mês e do ano com o homólogo; a tesouraria e os
    clientes a partir da abertura.
  - **Frases e alertas:** a frase dos rendimentos; os alertas de prejuízo,
    de queda de 50 % e de clientes com mais de 90 dias.
  - **Fecho:** sem orçamento; mês inválido; sem acesso.
- `NexGenAccountingAI.Api.Tests`: o relatório de março e um mês inválido.
- PWA: rota e estado sem base de dados.

### Por fazer

- **Geração automática e envio.** A [fronteira do F2-05E](../phase-2/F2-05E-MONTHLY-GENERATION-BOUNDARY.md)
  mantém-se: não há agendamento, teto de custo nem envio de email. O relatório
  imprime-se ou guarda-se em PDF a pedido.
- Juntar ao relatório o texto da síntese com IA do mesmo mês, quando existir.
