# F2-05E — Geração mensal automática: fronteira

Quinto e último sub-incremento do [F2-05](F2-05-AI-MANAGEMENT-REPORTS.md).
**Fecha o épico.** Documenta porque a geração mensal automática fica fora do
âmbito local e como se faria quando houver contexto de operação.

## O que já existe

O interruptor `CompanyAiSettings.AutoMonthly` (F2-05C) está persistido e
aparece no ecrã (F2-05D), ao lado do `Enabled`. O caminho manual está
completo: um clique em «Gerar» produz a `sintese-mensal` (ou qualquer outro
tipo) para o período escolhido, com o resultado guardado e no histórico.

## Porque a geração agendada fica de fora

À imagem da fronteira do envio por email ([F2-07E](F2-07E-SECOND-SOURCE-AND-DELIVERY.md)),
um agendador que dispara chamadas pagas à API de IA sozinho precisa de um
contexto que a plataforma local ainda não tem:

- **Cadência e janela** — «uma vez por mês» só é útil depois de o exercício do
  mês anterior estar lançado e reconciliado; decidir esse gatilho (dia fixo?
  after o fecho do período? sinal do utilizador?) é uma decisão de operação,
  não de código.
- **Controlo de custo** — cada execução automática gasta *tokens* da chave da
  organização sem um clique. Precisa de um teto por empresa/mês e de
  observabilidade do gasto — que hoje não existem.
- **Idempotência e reentrada** — o `Worker` local corre uma instância; um
  agendador real precisa de garantir «uma geração por empresa e mês» mesmo com
  reinícios, e de saber retomar.
- **Sem chave, não há nada a agendar** — o `Worker` teria de degradar em
  silêncio quando `Ai:ApiKey` não está definida, o que torna a funcionalidade
  invisível na maioria dos ambientes de desenvolvimento.

## Como se faria

Quando houver um contexto de operação (agendamento, tetos de custo,
monitorização):

- o `Worker` passa a referenciar `NexGenAccountingAI.Infrastructure.Ai` (mais uma
  linha no teste de arquitetura) e regista um `BackgroundService` com um
  `PeriodicTimer` diário;
- uma vez por dia, verifica se está no início de um mês novo e, em caso
  afirmativo, para cada empresa com `AutoMonthly = true` e narrador
  configurado, chama `ManagementReportService.GenerateAsync` com a
  `sintese-mensal` do mês anterior — saltando as que já têm um relatório desse
  tipo e período (o índice `(CompanyId, PeriodFrom, PeriodTo)` já suporta a
  verificação);
- as Tarefas ganham um sinal «síntese mensal de {mês} disponível» com ligação
  ao ecrã.

## Épico F2-05 concluído

F2-05A (dossiê determinístico) · F2-05B (narrador Claude no projeto
`Infrastructure.Ai`) · F2-05C (persistência, interruptor e geração) · F2-05D
(ecrã «Relatórios de gestão») · F2-05E (fronteira da geração mensal). Fica em
aberto, para épicos futuros: a geração agendada (esta fronteira), mais tipos
pré-definidos, e o envio dos relatórios por email (fronteira de F2-07E).
