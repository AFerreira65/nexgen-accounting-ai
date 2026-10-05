# F3-28 — Painel do gabinete

Épico do Bloco F da [Fase 3](README.md). Até aqui, o trabalho via-se empresa a
empresa. O gabinete precisa de ver todas as empresas de uma vez e de saber por
onde começar.

## Sub-incrementos

### F3-28A — Todas as empresas, pelo que precisa de atenção (feito)

- **`FirmDashboardService`**: para cada empresa ativa a que o utilizador tem
  acesso, mostra:

| Coluna | De onde vem |
|---|---|
| Obrigações em atraso | Pendentes com prazo já passado, nos últimos 90 dias do [calendário](F3-27-OBLIGATIONS-CALENDAR.md) (F3-27A). |
| Próxima obrigação | A primeira pendente nos próximos 15 dias, e quantas mais há. |
| Por conferir | Lançamentos por conferir. |
| Compras por lançar | Faturas do e-Fatura dos últimos três meses ainda por lançar, pelo mesmo critério dos [lançamentos sugeridos](F3-18-SUGGESTED-ENTRIES.md) (sem as já lançadas, as anuladas e as dispensadas). |
| Bancos por conciliar | Movimentos dos extratos sem lançamento conciliado ([F3-19](F3-19-BANKS.md)). |
| Automáticos | Nos lançamentos dos últimos 12 meses, sem abertura nem apuramento, a parte que não é manual: importada do SAF-T, sugerida do e-Fatura ou do SAF-T de faturação, ou lançada do extrato. |

  **Ordem:** primeiro as empresas com mais obrigações em atraso; depois as
  que somam mais trabalho por fazer. Os atrasos pesam dez vezes mais.
- **API:** `GET /api/v1/development/users/{userId}/firm-dashboard?date=`, do
  utilizador e não de uma empresa.
- **PWA:** ecrã **«Painel do gabinete»** (`/painel-gabinete`, grupo
  «Trabalho»):
  - os totais do gabinete;
  - uma linha por empresa;
  - cada número abre a empresa no ecrã do trabalho em causa: o calendário,
    os movimentos, os lançamentos sugeridos ou os bancos.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`FirmDashboardServiceTests`, em PostgreSQL):
  - **Âmbito e ordem:** só as empresas do utilizador; a que tem atrasos
    primeiro.
  - **Números:** por conferir, compras por lançar, bancos por conciliar e
    automáticos (50 %).
  - **Obrigações:** a próxima obrigação; a declaração mensal de remunerações
    em atraso só na empresa com salários.
- `NexGenAccountingAI.Api.Tests`: o painel do utilizador de teste.
- PWA: rota e estado sem base de dados.

### F3-28B — Evolução da automação (feito)

- **Sem histórico guardado.** A automação sai da origem de cada lançamento
  (`EntrySource`), que já fica registada; por isso a evolução tem história
  desde o primeiro lançamento e não depende de alguém abrir o painel. O
  trabalho por fazer (por conferir, por lançar, por conciliar) não se
  reconstitui assim e não entra.
- **`FirmDashboard.Automation`:** os doze meses que acabam no mês da data,
  com os lançamentos de todas as empresas do painel por origem — à mão,
  SAF-T de contabilidade, e-Fatura, SAF-T de faturação e extrato — e a parte
  automática de cada mês (vazia num mês sem lançamentos). Ficam de fora,
  como no F3-28A, os anulados, a abertura e o apuramento.
- **Por empresa:** a parte automática dos últimos três meses e a dos três
  anteriores.
- **PWA:** secção «Evolução da automação» com os lançamentos por mês (à mão e
  automáticos), a parte automática por mês e os automáticos por origem; na
  tabela, a tendência de cada empresa (▲, ▼ ou =).

**Validação efetuada:** `FirmDashboardServiceTests` — os doze meses de junho
a maio, o 1 de junho no primeiro mês, abril com o lançamento do e-Fatura
(100 %), um mês vazio sem percentagem e as partes de cada trimestre (50 % e
0 %) e da empresa sem lançamentos. Os gráficos não foram vistos com a base
real (o ecrã precisa da sessão).

### Por fazer

- Filtrar por responsável dentro do gabinete, quando houver distribuição de
  carteiras.
