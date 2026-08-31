# F1-17 — Contadores de numeração persistidos

## Objetivo

Substituir a numeração dos lançamentos baseada em contar as referências
existentes por um contador persistido por empresa, série e exercício. Assim a
sequência é estável, sem saltos e imune a apagar/reordenar lançamentos.

## Regras

Nova entidade `DocumentNumberSeries` (`CompanyId`, `Series`, `Year`, `NextNumber`)
no schema `accounting`, com índice único em `(CompanyId, Series, Year)`. `Series` é
o prefixo da referência — o código do tipo de documento quando há tipo, senão o
código do diário.

Ao emitir um lançamento, `JournalPostingService` procura a linha da série; se não
existir, cria-a a começar **a seguir ao maior número já existente** para esse
prefixo e exercício (continuidade com os lançamentos anteriores à migração).
Depois incrementa o contador e devolve `{PREFIX}-{ANO}-{NNNNN}`.

Corrigir um lançamento mantém a referência e **não consome** número da série.
Anular também não. A numeração continua a ser dentro da transação de escrita.

## Alteração de esquema

Migração `20260831215048_DocumentNumberSeries`: cria `accounting.DocumentNumberSeries`
e o índice único. Não toca em dados existentes; aplicada à base `accountingAI`.

## Superfície de validação local

Sem endpoint novo. O comportamento é interno ao registo de movimentos; a
referência devolvida na resposta do POST de movimentos é a mesma de sempre.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de domínio: `DocumentNumberSeries.Take()` devolve números consecutivos, aceita um número inicial e valida série, exercício e número;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; após emitir duas faturas `FTV`, existe a linha de série `("FTV", 2026)` com `NextNumber = 3`; corrigir a segunda fatura mantém a referência `FTV-2026-00002` e o contador fica na mesma; um tipo de documento inexistente não cria série;
- teste de API: `/api/v1/foundation` indica a fase `F1-17`;
- suite .NET completa (55 testes), lint, vinte e um testes de interface e build de produção da PWA;
- verificação: a base `accountingAI` tem a tabela `accounting.DocumentNumberSeries` (vazia até ao primeiro lançamento);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- ecrã para consultar e reiniciar séries;
- séries com máscara configurável (largura, prefixo por ano);
- numeração no modo de demonstração (continua a ser feita no cliente);
- concorrência multi-utilizador na mesma série (a ferramenta de desenvolvimento é de utilizador único).
