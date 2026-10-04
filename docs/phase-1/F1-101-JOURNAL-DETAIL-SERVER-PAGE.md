# F1-101 — Detalhe de um diário pela página do servidor

## Objetivo

Quinto passo do arco. O ecrã de um diário (`/diarios/{código}`) filtrava
o array completo de movimentos por diário e paginava no cliente. Num
diário grande (o de bancos da Vidreira Algarvia tem 939 lançamentos) isso
só funciona se o array inteiro tiver carregado — que numa empresa de
SAF-T não carrega.

## Regras

- Com a empresa ligada à base de dados, o ecrã pede
  `GET .../accounting/movements?page=&pageSize=&journal={código}`
  (o filtro `journal` já existe desde F1-98) e desenha a resposta
  paginada; a contagem passa a ser o `totalItems` do servidor.
- Em demonstração — ou se o pedido falhar — mantém o cálculo no cliente
  sobre o array do contexto (`usePagedList`).
- O nome do diário no cabeçalho vem do primeiro lançamento mostrado e, se
  não houver nenhum, do plano de contas da empresa (`accountingSetup`),
  com recurso ao código.

Sem alteração de esquema nem de API — reutiliza o endpoint de F1-97/F1-98.
A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir `/diarios/{código}` numa empresa ligada à BD pagina do lado do
servidor; o «Seguinte»/«Anterior» pede a página respetiva.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — em demonstração o ecrã pagina no cliente, como antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia: `/diarios/63` mostra «Bancos-integrações», 939 lançamentos, e
  «Página 1 de 19»; «Seguinte» pede `?page=2&pageSize=50&journal=63`.

## Fora deste incremento

- a pesquisa rápida (`GlobalSearch`) e as contagens de movimentos por
  diário no ecrã de diários ainda leem o array do contexto — próximos
  incrementos.
