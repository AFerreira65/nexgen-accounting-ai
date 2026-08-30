# F1-08 — Conferir, corrigir e anular lançamentos

## Objetivo

Depois de a aplicação passar a registar movimentos (F1-07), fecha-se o ciclo de
vida do lançamento: conferir, corrigir enquanto está por conferir, e anular.

Ficam disponíveis:

- marcar um lançamento por conferir como conferido;
- corrigir um lançamento por conferir — cabeçalho e linhas — mantendo a referência;
- anular um lançamento (qualquer estado), preservando-o para rasto.

## Regras de domínio

- `JournalEntryStatus` ganha o valor `Voided`.
- `JournalEntry.MarkReviewed()` só transita de *por conferir* para *conferido*.
- `JournalEntry.Void()` marca o lançamento como anulado a partir de qualquer estado; as linhas e o total mantêm-se.
- `Complete` deixa de aceitar `Voided` como estado de conclusão.
- Corrigir é substituir: `JournalPostingService.ReviseAsync` verifica que o lançamento está por conferir, remove-o (as linhas caem por *cascade*) e recria-o com a mesma referência, revalidando diário e contas. Isto evita a manutenção parcial de coleções e mantém as invariantes do domínio.

Não há alteração de esquema.

## Superfície de validação local

```text
PUT  /api/v1/development/users/{userId}/companies/{companyId}/accounting/movements/{reference}
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/movements/{reference}/review
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/movements/{reference}/void
```

A leitura de movimentos passa a devolver o estado `Anulado`. Conferir ou corrigir
um lançamento que não está por conferir, e referências inexistentes, devolvem
`ValidationProblem`.

Na PWA, o ecrã **Movimentos** ganha ações por linha: *Corrigir* e *Conferir* nos
lançamentos por conferir, *Anular* nos restantes. O editor de lançamento serve
também a correção, pré-preenchido. O dashboard exclui os anulados dos totais.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes de domínio: transição para conferido (e recusa de repetição), anulação e recusa de `Complete(Voided)`;
- teste de integração SQL Server LocalDB: conferir, recusar a correção de um lançamento conferido, anular, e corrigir um lançamento por conferir mudando o diário e as linhas;
- teste de API para as recusas (desequilíbrio, referências e diário/conta desconhecidos, conferir um já conferido, corrigir/anular inexistente, sem associação), sem escrever na base configurada;
- lint, doze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- corrigir um lançamento já conferido (exige reabrir ou estornar);
- estorno automático (lançamento inverso) na anulação;
- exclusão dos anulados dos saldos por conta e do razão;
- numeração por tipo de documento;
- autorização de produção e auditoria das alterações.
