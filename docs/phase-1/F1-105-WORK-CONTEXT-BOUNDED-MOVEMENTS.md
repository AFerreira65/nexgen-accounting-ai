# F1-105 — O contexto deixa de carregar o histórico inteiro

## Objetivo

Passo final do arco (F1-97…F1-104). Com todos os ecrãs a ler os
movimentos dos endpoints dedicados — lista paginada, detalhe por
referência, resumo agregado, contagens por diário —, o `WorkContext` já
não precisa de carregar o array completo. Numa empresa importada de SAF-T
esse pedido chega a exceder os 120 s de tempo-limite de comando e punha a
app inteira em modo de demonstração.

## Regras

- Em modo de base de dados, o `WorkContext` pede
  `GET .../accounting/movements?page=1&pageSize=500` — uma amostra dos 500
  lançamentos mais recentes — em vez do array inteiro.
- Essa amostra serve só de recurso aos relatórios (balancete, razão,
  demonstração de resultados, balanço, resumo de IVA, contas correntes,
  livro diário, apuramento, abertura) e aos caminhos de cliente dos ecrãs
  já migrados, quando o endpoint agregado respetivo falha. Numa empresa
  pequena (até 500 lançamentos) o recurso continua exato; numa empresa
  grande passa a ser «os mais recentes», o que já é melhor do que o
  estado atual (o array não carregava de todo).
- Em demonstração e nos testes, o comportamento é o de sempre (o array
  sintético da empresa).

Sem alteração de esquema nem de API. A fase do `/foundation` mantém-se em
F1-71.

## Superfície de validação local

Abrir a app numa empresa com dezenas de milhar de lançamentos deixa de
transferir ~20 MB no arranque; todos os ecrãs continuam a funcionar a
partir dos endpoints dedicados.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em teste o contexto usa o array sintético);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia (27 311 lançamentos): o contexto pede agora
  `movements?page=1&pageSize=500` e não há nenhum pedido sem paginação; a
  lista de movimentos, o painel, o balancete, as Tarefas, o sino, a
  pesquisa rápida, o detalhe de um movimento, o detalhe de um diário e a
  contagem por diário funcionam todos, com a badge «Dados da BD».

## Fora deste incremento

- consolidar os dois pedidos ao resumo (barra de topo e Tarefas/painel)
  num só;
- paginação do lado do servidor para os próprios relatórios agregados
  (hoje o cálculo é todo no `AccountLedgerService`/serviços afins, que já
  correm no SQL) — não é necessário para o objetivo deste arco.
