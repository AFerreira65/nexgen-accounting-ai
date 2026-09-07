# F1-99 — Resumo da fila de trabalho sem o array de movimentos

## Objetivo

Terceiro passo do arco (F1-97, F1-98). A barra de topo (sino) e o ecrã de
Tarefas leem o array completo de movimentos que o `WorkContext` carrega —
só para contar os que estão «por conferir» e somar a posição de IVA. Numa
empresa importada de SAF-T esse array não chega sequer (o pedido excede os
120 s na Vidreira Algarvia), e o sino e as Tarefas caíam nos dados de
demonstração. Passam a um endpoint de agregados.

## Regras

- Novo `GET .../accounting/movements/summary` — tudo agregado no SQL, sem
  trazer lançamentos:
  - `total`, `pendingReview` (contagem dos que estão por conferir);
  - `vatInput` / `vatOutput` / `vatPosition` — somas sobre as linhas das
    contas `2432*` (débito − crédito) e `2433*` (crédito − débito), sem os
    anulados; `vatPosition = vatOutput − vatInput`, a mesma fórmula do
    resumo de IVA e do ecrã de Tarefas;
  - `pendingPreview` — até sete lançamentos por conferir, dos mais antigos
    aos mais recentes, só com `id`, `date`, `description` e `amount` (sem
    linhas).
- Novo hook partilhado `useMovementsSummary` (`src/app/movementsSummary.ts`):
  numa empresa ligada à base de dados busca o endpoint; em demonstração —
  ou se o pedido falhar — calcula o mesmo a partir do array do contexto.
- `AppLayout` (ponto do sino) e `TasksPage` (lembrete de IVA, lista e
  contagem de movimentos por conferir) passam a usar o hook e deixam de
  ler `movements` diretamente.

Sem alteração de esquema. Endpoint de `/development`. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

`GET .../accounting/movements/summary` devolve as contagens e a posição de
IVA; abrir Tarefas ou olhar para o sino numa empresa ligada à BD já não
depende do histórico inteiro.

## Validação efetuada

- `AccountingAI.Api`: quinze testes (um novo — `total`, `pendingReview`,
  `vatPosition` e a antevisão sem linhas, mais o 404 de empresa sem
  acesso);
- `AccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em modo de demonstração o hook calcula no cliente, como
  antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: na Almeida & Costa
  (ligada à BD) as Tarefas mostram «IVA a entregar 598,00 €» e um
  movimento por conferir (BANC 2026 00196) vindos do endpoint, e o sino
  diz «Tarefas — 21 pendentes» com o ponto aceso; na Vidreira Algarvia,
  cujo array completo dá 500 aos 120 s, o resumo responde em ~0,4 s e as
  Tarefas passam a abrir.

## Fora deste incremento

- o `WorkContext` continua a carregar o array completo em modo de base de
  dados — o painel, a pesquisa rápida, o detalhe de um diário/movimento e
  as contagens por diário ainda dependem dele; cada um segue num
  incremento próprio até o array poder desaparecer;
- `AppLayout` e `TasksPage` fazem cada um o seu pedido ao resumo (dois
  pedidos iguais por troca de empresa); consolidá-los pode vir depois.
