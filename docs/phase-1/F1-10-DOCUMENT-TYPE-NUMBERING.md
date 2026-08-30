# F1-10 — Lançamento por tipo de documento, com numeração própria

## Objetivo

Dar aos lançamentos uma numeração por tipo de documento, além da numeração por
diário já existente.

Ficam disponíveis:

- registar um lançamento indicando o tipo de documento;
- referência gerada por tipo de documento — `{TIPO}-{ANO}-{NNNNN}` — sequencial e independente da numeração por diário;
- o diário do lançamento passa a ser o predefinido do tipo de documento, salvo indicação de outro.

## Regras

`JournalPostingService` aceita um `documentTypeCode` opcional em `PostAsync` e
`ReviseAsync`:

- sem tipo de documento, mantém-se o comportamento anterior: numeração `{DIÁRIO}-{ANO}-{NNNNN}`;
- com tipo de documento, este tem de existir e estar ativo na empresa; o prefixo da referência passa a ser o código do tipo de documento e o diário é o predefinido do tipo (a menos que seja indicado explicitamente um diário ativo);
- a sequência é contada pelas referências da empresa que começam por `{TIPO}-{ANO}-`, com verificação de unicidade.

Não há alteração de esquema: o tipo de documento fica registado no prefixo da
referência, sem chave estrangeira no lançamento nesta fase.

## Superfície de validação local

O corpo de `POST` e `PUT .../accounting/movements` ganha o campo opcional
`documentTypeCode`. Um tipo de documento desconhecido ou inativo devolve
`ValidationProblem`.

Na PWA, o editor de lançamento ganha o seletor **Tipo de documento**; ao
escolher um tipo, o diário passa para o predefinido desse tipo e a referência
gerada usa o prefixo do tipo.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: dois lançamentos com o tipo `FTV` recebem `FTV-2026-00001` e `FTV-2026-00002` e o diário `VENDAS` (predefinido do tipo); um tipo de documento inexistente é recusado;
- teste de API para a recusa de tipo de documento desconhecido;
- lint, catorze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- chave estrangeira do lançamento para o tipo de documento (exige migração);
- consulta de lançamentos por tipo de documento;
- séries por tipo de documento e por exercício com contadores próprios persistidos;
- estorno automático na anulação.
