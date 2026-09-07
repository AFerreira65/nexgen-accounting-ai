# F1-98 — A lista de movimentos consome a página do servidor

## Objetivo

Segundo passo do arco iniciado em F1-97. A lista de movimentos
(`MovementsPage`) filtrava e paginava no cliente sobre o array inteiro
que o `WorkContext` carrega. Numa empresa importada de SAF-T esse array
são dezenas de milhar de linhas — e o pedido que o traz chega a exceder
o tempo-limite de comando (120 s) na Vidreira Algarvia, deixando o ecrã
sem dados reais. Passa a pedir só a página atual ao servidor.

## Regras

- Com a empresa ligada à base de dados (`accountingSource === 'database'`),
  a lista pede
  `GET .../accounting/movements?page=&pageSize=&search=&journal=&status=&source=&from=&to=`
  e desenha a resposta paginada; a paginação, a contagem e os filtros
  passam a ser do servidor. `pageSize` é o das preferências da empresa.
- A pesquisa tem um atraso de 300 ms antes de disparar o pedido; os
  restantes filtros pedem de imediato. Qualquer mudança de filtro volta à
  primeira página.
- O CSV continua a exportar o conjunto filtrado **inteiro**: em modo de
  base de dados busca-o com os mesmos filtros mas sem `page` (o endpoint
  devolve então o array completo).
- Sem ligação à base de dados (demonstração) — ou se o pedido falhar —, a
  lista mantém o comportamento anterior: filtra e pagina no cliente sobre
  o array do contexto (`paginate`), com uma nota discreta quando é uma
  degradação por falha.
- O endpoint de F1-97 ganhou o filtro `journal` (código do diário) e a
  pesquisa passou a abranger também as observações, para igualar o que a
  lista fazia no cliente.
- A contagem mostra «N movimentos» em modo de base de dados (só há o total
  filtrado) e «N de M» em modo cliente, como antes.

Sem alteração de esquema. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Numa empresa ligada à BD, abrir `/movimentos`, escrever na pesquisa ou
mexer nos filtros: cada mudança faz um pedido `?page=…` e a lista mostra
só essa página; o CSV exporta tudo o que corresponde aos filtros.

## Validação efetuada

- `AccountingAI.Api`: catorze testes (o de F1-97 passou a cobrir também o
  filtro `journal`);
- `AccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — correm em modo de demonstração, o caminho do cliente,
  que se mantém igual);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa
  Almeida & Costa (ligada à BD): o ecrã mostra «Dados da BD», cada filtro
  (diário, estado, datas) e a pesquisa (com atraso) disparam um pedido
  `?page=1&pageSize=…&…` e a lista desenha só a resposta; o CSV faz um
  pedido sem `page`. Na Vidreira Algarvia, cujo pedido sem paginação
  excede hoje os 120 s, o ecrã cai graciosamente no array já carregado
  com a nota de degradação — o incremento seguinte tira esse pedido.

## Fora deste incremento

- deixar o `WorkContext` de carregar o array inteiro em modo de base de
  dados (os ecrãs de síntese — painel, tarefas, pesquisa rápida, sino —
  ainda dependem dele) — incremento seguinte do arco;
- teste de interface do caminho de base de dados (exigiria simular
  `fetch`, que esta suíte evita; fica coberto pelos testes da API e pela
  verificação no browser).
