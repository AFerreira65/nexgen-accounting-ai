# F1-25 — Reabertura do exercício

## Objetivo

Tornar o apuramento e a abertura reversíveis: se houver um erro ou um lançamento
tardio, anular o lançamento de fecho para o exercício poder ser reapurado ou
reaberto.

## Regras

`YearEndClosingService.ReopenAsync(companyId, year)` e
`YearOpeningService.ReopenAsync(companyId, year)` anulam (`Void`) o lançamento
`AP`/`AB` do exercício, mantendo-o no diário para rasto. Depois, `PreviewAsync`
volta a indicar o exercício por apurar / por abrir e o registo fica disponível.

Ordem obrigatória, das camadas de dentro para fora:

- não se reabre o apuramento de `year` enquanto a abertura de `year + 1` existir
  (anular primeiro a abertura seguinte);
- não se reabre a abertura de `year` enquanto o apuramento de `year` existir
  (anular primeiro o apuramento).

Endpoints de desenvolvimento:

```text
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-end-closing/reopen?year=2026
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-opening/reopen?year=2027
```

Nos ecrãs **Apuramento** e **Abertura**, quando o exercício já está fechado, o
botão passa a **Reabrir exercício**; o registo volta a aparecer depois de
reaberto. Continua a ser uma operação só com ligação ao SQL Server.

Sem alteração de esquema.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: com 2027 aberto, reabrir o apuramento de 2026 é recusado; `ReopenAsync(2027)` anula `AB-2027-00001` e a pré-visualização de 2027 deixa de estar aberta; a seguir `ReopenAsync(2026)` anula `AP-2026-00001` e 2026 deixa de estar apurado; reabrir 2026 uma segunda vez é recusado;
- teste de API: `/foundation` indica a fase `F1-25`; `reopen` de um exercício fora do intervalo devolve `400`;
- teste de interface: em modo de demonstração (nunca apurado) o botão «Reabrir exercício» não aparece nos ecrãs Apuramento e Abertura;
- verificação no browser contra a API: com `AP-2026-00001` e `AB-2027-00001` na base, os ecrãs mostram o estado «Apurado»/«Aberto» e o botão **Reabrir exercício**; sem erros de consola;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e sete testes de interface e build de produção da PWA.

## Fora deste incremento

- histórico das reaberturas por exercício;
- bloqueio de exercícios fechados (impedir novos lançamentos com data nesse exercício);
- reabertura em cadeia (abertura + apuramento numa só ação).
