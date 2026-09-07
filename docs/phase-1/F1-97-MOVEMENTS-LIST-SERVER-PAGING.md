# F1-97 — Endpoint de movimentos com paginação do lado do servidor

## Objetivo

`GET .../accounting/movements` devolve sempre o histórico inteiro. Numa
empresa importada de SAF-T (a Vidreira Algarvia tem 27 311 lançamentos)
são cerca de 20 MB por pedido, que o `WorkContext` carrega para memória e
a lista de movimentos percorre e filtra a cada tecla. O primeiro passo
para cortar isto é o endpoint saber devolver só uma página — o cliente
passa a consumi-lo num incremento seguinte.

## Regras

- O endpoint ganha parâmetros opcionais:
  - `page` e `pageSize` — `pageSize` limitado a [1, 200] (por omissão 50),
    `page` travada a [1, número de páginas];
  - `search` — filtra por referência do lançamento, número do documento ou
    descrição (contém, sem distinção de maiúsculas, no SQL Server);
  - `status` — `Por conferir` / `Conferido` / `Anulado`;
  - `from` / `to` — intervalo de datas ISO, validado como nos relatórios
    (`TryParseReportDateRange`, devolve 400 quando o formato é inválido).
  - `documentType` e `source` mantêm-se.
- **Sem `page`**, a resposta é exatamente a de antes: o array completo,
  ordenado por data e referência decrescentes. Todos os consumidores
  atuais (o contexto de trabalho, os relatórios que ainda leem tudo)
  continuam a funcionar sem alteração.
- **Com `page`**, a resposta é um envelope
  `{ items, page, pageSize, totalItems, totalPages }`; a contagem e o
  `Skip`/`Take` correm no SQL Server, não em memória.
- A projeção de cada movimento (com as linhas) passou a um ajudante único
  `ToMovementPayload`, partilhado pelos dois ramos.

Sem alteração de esquema. Continua a ser um endpoint de
`/development`. A fase do `/foundation` mantém-se em F1-71 (como em
F1-76/F1-77, que também mexeram na API sem a fazer avançar).

## Superfície de validação local

`GET .../accounting/movements?page=1&pageSize=25` devolve o envelope
paginado; sem `page`, devolve o array como sempre.

## Validação efetuada

- `AccountingAI.Api`: catorze testes (um novo — envelope paginado, trava
  da última página, `search` pela referência, 400 em data inválida, e o
  array completo intacto sem `page`);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `AccountingAI.Web`: lint, build e setenta e três testes de interface
  (sem alteração — nenhum ecrã consome ainda o endpoint paginado);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação com a API local contra a base `accountingAI`, na empresa
  Vidreira Algarvia: `?page=1&pageSize=3` devolve `totalItems` 27 311 e
  9104 páginas; `?page=999999` trava na última; `?pageSize=5000` limita a
  200; `search`, `status` e `from`/`to` filtram no servidor; o pedido sem
  `page` continua a devolver o array completo.

## Fora deste incremento

- fazer a lista de movimentos (`MovementsPage`) consumir o endpoint
  paginado em modo de base de dados — incremento seguinte;
- aliviar o `WorkContext` de carregar o array inteiro para os ecrãs de
  síntese (painel, tarefas, pesquisa rápida) — depois disso.
