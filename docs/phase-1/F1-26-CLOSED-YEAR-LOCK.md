# F1-26 — Bloqueio de lançamentos em exercício apurado

## Objetivo

Dar sentido ao apuramento: depois de apurado, um exercício não deve receber nem
alterar movimentos, para que a demonstração de resultados e o balanço não fiquem
desatualizados sem aviso.

## Regras

`JournalPostingService` verifica, antes de registar (`PostAsync`) ou corrigir
(`ReviseAsync`), se o exercício da data do movimento tem um lançamento de
apuramento (`AP`) não anulado. Se tiver, recusa com:

> O exercício de {ano} está apurado. Reabra-o para lançar movimentos com esta
> data.

Na correção, a verificação abrange tanto o ano da data atual do lançamento como o
ano da nova data. Os próprios lançamentos de apuramento e de abertura
(`AP`/`AB`) ficam de fora da regra. Reabrir o exercício (F1-25) remove o
bloqueio.

Sem alteração de esquema. Na PWA, o editor de movimentos já apresenta a mensagem
de erro devolvida pela API.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: depois de apurar 2026, lançar um movimento com data de 2026 é recusado; depois de reabrir 2026, o mesmo lançamento é aceite e recebe uma referência `GERAL-2026-…`;
- teste de API: `/foundation` indica a fase `F1-26`;
- verificação no browser contra a API: com `AP-2026-00001` na base, `POST` de um movimento datado de 2026-06-01 devolve `400` com a mensagem de exercício apurado, e a transação é revertida;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e sete testes de interface e build de produção da PWA.

## Fora deste incremento

- data de bloqueio configurável por empresa (independente do apuramento);
- bloqueio da anulação de lançamentos de um exercício apurado;
- aviso no ecrã de lançamento antes de submeter (a data já está num exercício apurado).
