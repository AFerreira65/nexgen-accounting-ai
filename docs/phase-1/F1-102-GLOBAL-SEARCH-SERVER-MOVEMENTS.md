# F1-102 — Pesquisa rápida procura os movimentos no servidor

## Objetivo

Sexto passo do arco. A pesquisa rápida da barra de topo (`GlobalSearch`)
filtrava os movimentos a partir do array completo do contexto. As contas
e os diários vêm dos cadastros (pequenos, ficam como estão); os
movimentos passam a um pedido de pesquisa.

## Regras

- Com a empresa ligada à base de dados, a parte dos movimentos vem de
  `GET .../accounting/movements?page=1&pageSize=5&search=<termo>`
  (a mesma pesquisa server-side de F1-98, sobre referência, documento,
  descrição e observações), com um atraso de 250 ms, e mostra até quatro
  resultados como antes.
- Em demonstração — ou se o pedido falhar — filtra o array do contexto,
  como antes.
- As contas e os diários continuam a ser filtrados no cliente a partir de
  `accountingSetup`.

Sem alteração de esquema nem de API — reutiliza o endpoint. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Escrever duas ou mais letras na pesquisa da barra de topo, numa empresa
ligada à BD, faz um pedido de pesquisa e lista os movimentos
correspondentes; `Ctrl`/`Cmd`+`K`, setas e Enter continuam iguais.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — o teste da pesquisa cobre contas; em demonstração a
  parte dos movimentos filtra no cliente, como antes);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Vidreira
  Algarvia: escrever `00630063120073` faz
  `GET .../movements?page=1&pageSize=5&search=00630063120073` e mostra
  «2025 12 31 63 00630063120073 — Movimento · Depósito Bancário».

## Fora deste incremento

- as contagens de movimentos por diário no ecrã de diários e os
  relatórios que ainda recorrem ao array do contexto em caso de falha —
  próximos incrementos, até o `WorkContext` deixar de o carregar.
