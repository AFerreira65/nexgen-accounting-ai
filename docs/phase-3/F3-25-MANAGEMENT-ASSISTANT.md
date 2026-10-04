# F3-25 — Assistente de gestão

Épico do Bloco E da [Fase 3](README.md). O levantamento pedia: a conversa
sobre os números com ligação à origem, um índice de saúde, alertas, a
previsão de tesouraria e a comparação setorial.

**Já cobertos noutros épicos:**

- **Conversa:** a pergunta livre dos [relatórios de gestão com IA](../phase-2/F2-05-AI-MANAGEMENT-REPORTS.md)
  (F2-05) responde sobre os números da empresa, com ligação às contas e aos
  movimentos citados.
- **Alertas:** estão no relatório para o empresário
  ([F3-26A](F3-26-OWNER-REPORT.md)).

## Sub-incrementos

### F3-25A — Previsão de tesouraria (feito)

- **`TreasuryForecastService`** faz uma previsão por **critérios à vista**, não
  por um modelo estatístico:

| Parte | Regra |
|---|---|
| Ponto de partida | O saldo de caixa e bancos (11, 12 e 13) na data, no exercício da data, com a abertura incluída. |
| Recebimentos | Os documentos de clientes (21) em aberto, apurados pela regra da idade de saldos ([F3-23B](F3-23-RATIOS-OPERATING-MAP.md)): os pagamentos abatem os documentos mais antigos de cada terceiro. Cada um entra no mês da **data do documento mais o prazo de recebimento**, 30 dias por omissão. Se essa data já passou, conta no primeiro mês. |
| Pagamentos | O mesmo, para os fornecedores (22), com o prazo de pagamento. |
| Duvidosos | Os documentos com mais de **180 dias** ficam fora da previsão e são mostrados à parte. É um critério da aplicação. |
| Gastos fixos | O valor indicado ou, por omissão, a **média dos gastos com o pessoal (63) dos três meses completos anteriores**, que não passam por fornecedores. No mês da data, contam só os dias que faltam. |
| Horizonte | De 1 a 12 meses. Mostra o saldo inicial, os recebimentos, os pagamentos, os gastos fixos e o saldo final de cada mês, o saldo mais baixo e os maiores recebimentos e pagamentos previstos. |

- **Ficam de fora**, e o ecrã di-lo: o IVA a entregar, os outros impostos e
  os investimentos que não passem por fornecedores.
- **API:** `GET .../management/treasury-forecast?date=&months=&collectionDays=&paymentDays=&fixedMonthly=`.
- **PWA:** ecrã **«Previsão de tesouraria»** (`/previsao-tesouraria`, grupo
  «Gestão») com:
  - a data, os meses, os dois prazos e os gastos fixos, que se podem mudar;
  - os números principais e o saldo mais baixo, com aviso quando fica
    negativo;
  - o gráfico mensal, a tabela e os maiores recebimentos e pagamentos;
  - a impressão.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`TreasuryForecastServiceTests`, em
  PostgreSQL):
  - **Ponto de partida:** o saldo a partir da abertura; a média do pessoal
    com um mês sem gastos.
  - **Documentos:** uma fatura já paga fica de fora; um documento duvidoso
    também; o recebimento e o pagamento entram no mês certo.
  - **Gastos fixos:** no mês da data contam zero; o saldo de cada mês.
  - **Prazo zero:** o vencido entra no primeiro mês.
  - **Erros e acesso:** meses e prazos inválidos; sem acesso.
- `NexGenAccountingAI.Api.Tests`: a previsão e meses fora do limite.
- PWA: rota e estado sem base de dados.

### Por fazer

- **IVA e impostos na previsão.** Precisa dos prazos de entrega, que o
  [calendário de obrigações](README.md) (F3-27) há de trazer com fonte legal.
- **Índice de saúde e comparação setorial.** Precisam de valores de
  referência com fonte, por exemplo os Quadros do Setor do Banco de Portugal,
  que ainda não estão na plataforma. Os rácios
  ([F3-23A](F3-23-RATIOS-OPERATING-MAP.md)) continuam sem valores de
  referência.
- **Conversa com seguimento.** Hoje, a pergunta livre é de uma só volta.
