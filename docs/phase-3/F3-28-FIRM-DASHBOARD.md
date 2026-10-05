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

### F3-28C — Carteiras: o painel por responsável (feito)

**Antes, uma correção de segurança.** Para indicar responsáveis era preciso
gerir os acessos às empresas, e os endpoints do cadastro não verificavam quem
pedia: qualquer utilizador com sessão podia dar-se a si próprio a função de
dono de qualquer empresa. Ficou corrigido primeiro, com as regras na
[ADR-0014](../architecture-decisions/ADR-0014-ORGANIZATION-PERMISSIONS.md).

**O responsável:**

- **`CompanyUser.IsResponsible`** marca de quem é a empresa na carteira do
  gabinete. Migração aditiva `CompanyResponsible`.
- Pode haver mais de um responsável por empresa. É só informação: não dá
  acesso nenhum.
- Só um acesso ativo pode ser responsável; desativar o acesso tira-o.
- Indica-se no acesso à empresa, pelo dono ou por um administrador:
  `PUT .../organization/companies/{empresa}/members/{utilizador}` com
  `isResponsible`. Omitido, mantém-se o que estava.

**No painel:**

- `GET .../firm-dashboard?responsible=` fica só com as empresas desse
  responsável, incluindo os totais e a evolução da automação.
- A resposta traz sempre todos os responsáveis das empresas que o
  utilizador vê, para o filtro, e o responsável de cada empresa.
- **PWA:** escolha «Carteira» (todas, a minha ou a de um colega, com o
  número de empresas de cada um) e o responsável por baixo do nome de cada
  empresa.

**No ecrã «Empresas e utilizadores»**, que até aqui não tinha gestão de
acessos:

- ao editar uma empresa que administra, aparece **«Acessos a …»**: a função,
  o acesso ativo, o responsável e dar acesso a outro utilizador;
- ao criar um utilizador, dá-se logo acesso a uma empresa, porque um
  utilizador sem empresas não aparece na lista;
- as funções têm nome em português (`app/companyRoles.ts`): Dono,
  Administrador, Contabilista, Consulta;
- um administrador não vê a função de dono nem mexe no acesso de um dono.

**Validação efetuada**

- **`FirmDashboardServiceTests`:**
  - os dois responsáveis na escolha;
  - o responsável de cada empresa;
  - o painel filtrado só com a empresa da colega, com a evolução a seguir o
    filtro e a escolha ainda com os dois;
  - um acesso inativo recusado como responsável.
- **`OrganizationAdminAccessTests`:** marcar, manter quando omitido e perder
  ao desativar.
- **`CompanyMembers.test.tsx`:**
  - um administrador sem o dono nem a função de dono;
  - gravar o responsável;
  - a recusa do servidor mostrada.
- **`companyRoles.test.ts`:** os quatro nomes.
- **Ainda não visto no browser.** Os ecrãs com a base real não foram abertos,
  porque precisam da sessão.

### Por fazer

- Nada pedido. O que podia vir: o histórico do trabalho por fazer (por
  conferir, por lançar), que não se reconstitui dos lançamentos e teria de
  ser guardado.
