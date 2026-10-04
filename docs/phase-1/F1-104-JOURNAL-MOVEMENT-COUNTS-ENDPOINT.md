# F1-104 — Contagem de movimentos por diário

## Objetivo

Oitavo passo do arco. A coluna «Movimentos» do ecrã de Diários contava,
para cada linha, quantos lançamentos do array do contexto tinham aquele
código de diário — o último ecrã de cadastros a depender do histórico
inteiro.

## Regras

- Novo `GET .../accounting/journal-movement-counts` — um `GROUP BY` no SQL
  que devolve `{ "<código>": <contagem> }` para todos os diários com
  lançamentos.
- O ecrã de Diários (`JournalsPage`) pede-o quando a empresa está ligada à
  base de dados e mostra `contagem[código] ?? 0` por linha; em
  demonstração — ou se o pedido falhar — conta no array do contexto, como
  antes.
- O ecrã de séries de numeração já só usava o array como recurso de
  demonstração; fica como está.

Sem alteração de esquema. Endpoint de `/development`. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir `/diarios` numa empresa ligada à BD preenche a coluna «Movimentos»
a partir de um pedido agregado, não do histórico.

## Validação efetuada

- `NexGenAccountingAI.Api`: dezassete testes (um novo — contagem por diário com
  a chave do diário com lançamentos e o 404 de empresa sem acesso);
- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em demonstração conta no array, como antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia: o endpoint responde em ~0,08 s e o ecrã de Diários mostra
  «Vendas 15691», «Bancos-Recibos 4915», «Bancos-integrações 939» (soma
  27 311), com 0 nos diários provisionados sem lançamentos.

## Fora deste incremento

- o `WorkContext` continua a carregar o array completo de movimentos; com
  os ecrãs todos já a ler dos endpoints, o passo seguinte é deixar de o
  carregar (mantendo-o como recurso dos relatórios em caso de falha).
