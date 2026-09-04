# F1-60 — Reabrir um lançamento conferido para correção

## Objetivo

Corrigir um lançamento só é possível enquanto está "por conferir" (F1-08). Um
lançamento conferido — ou importado de um SAF-T, que entra já conferido (F1-53) —
só se podia emendar anulando e voltando a lançar, perdendo a referência. Falta
poder reabri-lo.

## Regras

- `JournalEntry.Reopen()` devolve um lançamento **conferido** ao estado "por
  conferir"; recusa a partir de qualquer outro estado.
- `JournalPostingService.ReopenAsync(companyId, reference)` aplica a transição
  dentro de uma transação, com as mesmas guardas de uma correção: recusa num
  **exercício apurado** ou num **período bloqueado** pela data de bloqueio da
  empresa, e recusa os lançamentos de **apuramento e abertura** (`AP`/`AB`), que
  têm ecrãs próprios de reabertura.
- Endpoint (só em `Development`):
  `POST .../accounting/movements/{reference}/reopen` — `404` se o utilizador não
  estiver associado à empresa; `400` (`ValidationProblem`, chave `movement`) com a
  mensagem em pt-PT quando a reabertura é recusada.
- Na lista de movimentos, um lançamento **conferido** (que não seja `AP`/`AB`)
  mostra a ação «Reabrir», desativada com o motivo quando a data cai num exercício
  apurado ou período bloqueado. Depois de reaberto passa a "por conferir" e
  aparecem «Corrigir» e «Conferir».

Sem alteração de esquema.

## Superfície de validação local

Na lista de movimentos, «Reabrir» um lançamento conferido; passa a "por conferir"
e pode ser corrigido. Reabrir um `AP`/`AB` ou um lançamento de um exercício
apurado é recusado com a razão.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `AccountingAI.Domain.Tests`: `Reopen()` leva de conferido a "por conferir" e
  recusa a partir de "por conferir" ou "anulado";
- `AccountingAI.IntegrationTests` (LocalDB descartável): depois de conferir um
  lançamento e de a correção ser recusada, `ReopenAsync` devolve-o a "por
  conferir" (e recusa uma segunda reabertura); a correção seguinte passa e o
  lançamento fica conferido com a nova descrição;
- `AccountingAI.Web`: lint, build e trinta e quatro testes de interface (um novo:
  «Reabrir» num movimento conferido de demonstração passa-o a "por conferir" e
  revela «Corrigir»);
- verificação no browser contra a base `accountingAI`: reabrir na empresa da ilha
  um lançamento conferido de um exercício aberto passa-o a "por conferir" e revela
  «Corrigir»/«Conferir»; via API, reabrir um `AP`/`AB` devolve «Os lançamentos de
  apuramento e abertura reabrem-se pelos respetivos ecrãs» e reabrir um lançamento
  de 2026 (apurado) devolve «O exercício de 2026 está apurado…»; o lançamento de
  teste foi removido da base no fim;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos.

## Fora deste incremento

- registar quem reabriu e quando (rasto de alterações);
- reabrir vários lançamentos de uma vez.
