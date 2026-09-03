# F1-27 — Bloqueio da anulação em exercício apurado

## Objetivo

Completar o bloqueio de F1-26: além de não se lançarem nem corrigirem movimentos,
também não se anulam movimentos de um exercício apurado — a anulação altera o
resultado tanto como um lançamento.

## Regras

`JournalPostingService.VoidAsync` passa a verificar, antes de anular, se o
exercício da data do lançamento tem um apuramento (`AP`) não anulado; se tiver,
recusa com a mesma mensagem de F1-26. `MarkReviewedAsync` (marcar conferido) não
é afetado.

Os lançamentos de apuramento e de abertura (`AP`/`AB`) continuam de fora da
regra, para que a reabertura (F1-25), que anula precisamente esses lançamentos,
funcione.

Sem alteração de esquema nem de API (a anulação já existia; passou a poder
devolver este erro).

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: depois de apurar 2026, anular `COMP-2026-00842` é recusado; a reabertura de 2026 continua a funcionar (anula o `AP` sem ser bloqueada);
- teste de API: `/foundation` indica a fase `F1-27`;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e sete testes de interface e build de produção da PWA.

## Fora deste incremento

- data de bloqueio configurável por empresa;
- aviso no ecrã antes de anular um lançamento de exercício apurado.
