# F1-70 — Ecrã de Tarefas: pendências da empresa ativa

## Objetivo

«Tarefas», na barra lateral, era desde a fundação um `PlaceholderPage` — «Este
módulo pertence às próximas tarefas da Fase 1.». Passa a agregar o que precisa de
atenção na empresa ativa, num só sítio.

## Regras

- Novo ecrã (`/tarefas`), substitui o placeholder e usa só dados já carregados
  pelo `useWorkContext` (sem pedidos adicionais):
  - **Movimentos por conferir** — lista, da data mais antiga à mais recente, com
    ligação a cada um; mostra os primeiros 6 e uma ligação «Ver todos os N» à
    lista quando há mais.
  - **Contas de movimento por classificar** — contas sem código de taxonomia
    (`Unclassified` e `SuggestionAvailable` do `taxonomyReview`), com a contagem
    das que têm sugestão do referencial disponível, e uma ligação a Taxonomias.
- Sem nada pendente, mostra o estado «Tudo em dia».
- `PlaceholderPage.tsx` foi removido (sem outras utilizações).

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-66.

## Superfície de validação local

Em «Tarefas», os movimentos por conferir e as contas por classificar da empresa
ativa aparecem com ligação direta; sem nenhum dos dois, mostra «Tudo em dia».

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e trinta e oito testes de interface (um novo:
  em «Tarefas», o movimento por conferir de demonstração e a secção de contas por
  classificar aparecem, com a ligação a Taxonomias);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa da ilha, «1»
  movimento por conferir (`VEND-AZO-2026-00014`) e «9» contas por classificar; na
  empresa do Continente, «1» movimento (`BANC-2026-00196`) e «20» contas; a
  ligação do movimento abre o seu detalhe.

## Fora deste incremento

- lembrete de IVA a entregar (exigiria carregar o balancete, como no dashboard);
- marcar tarefas como dispensadas;
- tarefas de outras empresas do gabinete.
