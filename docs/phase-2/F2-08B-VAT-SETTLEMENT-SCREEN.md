# F2-08B — Ecrã «Apuramento de IVA»

Segundo sub-incremento do [F2-08](F2-08-VAT-SETTLEMENT.md). O ecrã que mostra o
apuramento periódico de IVA calculado em F2-08A.

## Regras

- **`WorkContext.vatReturn(companyId, { from, to })`** → `VatReturn`; tipos
  `VatReturn` / `VatReturnAccountLine` em `demo-data.ts`.
- **`/apuramento-iva`** (`VatSettlementPage`) — em modo demo/teste mostra o
  cabeçalho e «Requer ligação ao SQL Server»; ligado à base:
  - seletor de período (datas + atalhos mês/trimestre/ano) — arranca no
    **trimestre** da data de referência (um apuramento é de um período de
    declaração, não do exercício inteiro); recalcula ao mudar de período;
  - o **mapa do apuramento** com uma coluna de operador (`+` / `−` / `=`), à
    imagem de uma declaração — IVA liquidado, IVA dedutível, regularizações a
    favor do Estado e do sujeito passivo, o resultado do período (realçado), o
    excesso a reportar do período anterior e, no rodapé, a **posição final**:
    «a entregar ao Estado» ou «excesso a reportar». O IVA dedutível é mostrado
    pelo seu valor com o operador `−`; num período em que as regularizações o
    superam pode vir negativo, e o operador mantém-se;
  - uma nota quando o excesso anterior foi assumido do saldo de `2437`;
  - a **desagregação por conta** — todas as `243*` que se moveram, com o papel
    de cada uma e ligação ao razão;
  - folha `@media print` (`ReportChrome`, chave `apuramento-iva`) e CSV.
- Entrada no menu lateral («Apuramento de IVA», a seguir ao «Resumo de IVA») e
  no `screenTable.ts` (`accounting/JournalEntryLines`).

Sem alteração de API nem de esquema.

## Validação efetuada

- `NexGenAccountingAI.Web`: `App.test.tsx` — o ecrã `/apuramento-iva` monta e, sem
  ligação à base, mostra «Requer ligação ao SQL Server». `tsc`, `eslint` e os
  oitenta testes de interface verdes.
- `dotnet build`/`dotnet test` na solução (cento e cinquenta e nove testes
  .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia): o ecrã
  arranca no 4.º trimestre de 2025 e calcula o apuramento — IVA liquidado
  880 422,04, regularizações a favor do Estado 212,08 e a favor do sujeito
  passivo 14 653,35, resultado 865 980,77, a entregar 865 980,77 — com o mapa a
  fechar pelos operadores e a desagregação das três contas 24.3 que se moveram.

## Fora deste incremento

- a persistência (`accounting.VatReturn`), o histórico e o excesso anterior como
  input explícito encadeado — F2-08C.
