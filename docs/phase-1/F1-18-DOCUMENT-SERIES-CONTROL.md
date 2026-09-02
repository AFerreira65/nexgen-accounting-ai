# F1-18 — Controlo de séries documentais

## Resultado

O contador criado no F1-17 passa a estar preparado para emissão simultânea e a
ter uma operação prática na aplicação Web. Cada série continua isolada por
empresa e exercício e mantém o formato `SERIE-AAAA-NNNNN`.

## Reserva concorrente

Antes de ler ou criar o contador, `JournalPostingService` obtém no SQL Server um
*application lock* exclusivo, pertencente à mesma transação do lançamento. O
recurso do bloqueio identifica empresa, série e exercício. Duas sessões que
emitam simultaneamente na mesma série entram uma de cada vez na pequena secção
crítica; séries, empresas ou exercícios diferentes não se bloqueiam entre si.

O incremento do contador e o movimento são confirmados na mesma transação. Se o
lançamento falhar, o número não é consumido.

## Consulta e avanço controlado

`DocumentNumberSeriesService` reúne:

- os tipos de documento e diários configurados na empresa;
- o maior número já emitido no exercício;
- o próximo número persistido, quando o contador já foi iniciado;
- séries históricas ainda presentes nos movimentos ou contadores.

É permitido avançar o próximo número, por exemplo de 3 para 100, para acomodar
uma transição controlada. Nunca é permitido recuar abaixo do contador atual nem
do maior número emitido mais um. Também não é possível criar por esta operação
uma série que não pertença aos tipos de documento ou diários da empresa.

Endpoints de desenvolvimento:

```text
GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/number-series?year=2026
PUT /api/v1/development/users/{userId}/companies/{companyId}/accounting/number-series/{series}/{year}
```

O corpo do `PUT` é `{ "nextNumber": 100 }`. Ambos os endpoints verificam a
associação ativa do utilizador à empresa.

## Experiência Web

O menu **Séries** abre um ecrã pesquisável com:

- indicadores de séries configuradas, contadores persistidos e séries usadas;
- escolha do exercício;
- último número emitido, próximo número e estado do contador;
- ligação direta ao cadastro de documentos ou diários que origina a série;
- edição em linha apenas para avançar o próximo número.

Em modo de demonstração, o mesmo ecrã é funcional mas identifica explicitamente
que o ajuste não é persistido no SQL Server.

## Base de dados e compatibilidade

O F1-18 reutiliza a tabela `accounting.DocumentNumberSeries` e a restrição única
do F1-17. Não é necessária uma nova migração. A proteção de concorrência usa uma
capacidade nativa do SQL Server e não muda o contrato das referências.

## Validação

- teste de domínio: um contador pode avançar, mas não recuar;
- teste SQL Server: seis sessões em paralelo recebem números únicos e
  consecutivos na mesma empresa, série e exercício;
- teste de serviço: consulta, avanço e rejeição de recuo/série desconhecida;
- teste de API: consulta por empresa e recusa de utilizador sem acesso;
- teste Web: abertura do ecrã, interligação e avanço em demonstração;
- endpoint `/api/v1/foundation` atualizado para `F1-18`.

O teste SQL cria uma base temporária com nome aleatório e elimina-a no fim. Pode
usar LocalDB ou uma ligação de teste fornecida apenas em
`ACCOUNTINGAI_TEST_SQL_CONNECTION`; as credenciais não são versionadas.

## Fora deste incremento

- formatos de referência configuráveis;
- reinicialização ou reciclagem de números;
- séries fiscais de faturação certificada;
- autenticação e autorização de produção;
- importação dos movimentos completos de SAF-T.
