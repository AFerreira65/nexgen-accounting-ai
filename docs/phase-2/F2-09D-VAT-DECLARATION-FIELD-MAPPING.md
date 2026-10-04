# F2-09D — Mapeamento aos campos numerados da declaração periódica

Quarto sub-incremento do [F2-09](F2-09-ACCOUNT-VAT-CLASSIFICATION.md). Mapeia
cada linha do [resumo por taxa e natureza](F2-09B-VAT-SUMMARY.md) ao campo
numerado da declaração periódica de IVA em vigor — best-effort, nunca oficial
([`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md)).

## Regras

- **Fonte** — este incremento só avançou depois do utilizador confirmar a
  numeração do modelo em vigor (`ADR-0009`, F2-09D "requer confirmação do
  utilizador"). A fonte é um manual pedagógico com o formulário e exemplos
  completos e cruzados (Ribeiro & Silva, 2026, CEOS.PP/ISCAP) — **não** o texto
  oficial da AT, daí o mapeamento continuar a ser best-effort e a levar sempre
  um aviso. Uma primeira tentativa de fonte (uma portaria do Diário da
  República) veio a revelar-se o modelo que só entra em vigor em 1 de julho de
  2027, com uma estrutura de quadro 06 diferente (por categoria, não por
  taxa) — não foi usada.
- **`Application/Vat/VatDeclarationFieldCatalog`** — `ForRow(kind, nature,
  rate, region)` devolve o `VatDeclarationField { Number, Note }` ou `null`
  quando a combinação não tem correspondência inequívoca no catálogo (ex.: a
  base de uma compra, que não tem campo próprio — só o IVA dedutível). Cobre:
  - base/imposto por taxa das operações internas (campos 1–6);
  - isentas com e sem direito à dedução (campos 8, 9);
  - aquisições intracomunitárias de bens e de serviços (campos 12/13, 16/17);
  - IVA dedutível por categoria e taxa (campos 20–24);
  - regularizações a favor do sujeito passivo/do Estado (campos 40, 41).
  - quando a região da linha não é Continente, o mesmo número aplica-se no
    Anexo R dessa região (mesma numeração, noutro impresso) — o catálogo
    assinala-o numa nota, sem distinguir automaticamente sede vs. Anexo R.
  `Source`/`Disclaimer` acompanham sempre os dados.
- **`VatSummaryService`** — cada `VatSummaryRow` ganha `Field`
  (`VatDeclarationField?`); `VatSummary` ganha `FieldMappingSource`/
  `FieldMappingDisclaimer`, na mesma chamada `GET .../vat-summary` — sem
  *endpoint* novo.
- **PWA** — o painel «Resumo por taxa e natureza» de `/apuramento-iva` ganha a
  coluna «Campo» (com a nota do Anexo R em `title`, quando existe) e o aviso
  fixo por baixo da tabela; incluído na exportação CSV.

## Validação efetuada

- `NexGenAccountingAI.Infrastructure.Files.Tests`: `VatDeclarationFieldCatalogTests`
  — todas as combinações documentadas (base/imposto × taxa para operações
  internas; dedutível por categoria/taxa; isentas; intracomunitárias;
  regularizações); a nota do Anexo R aparece só fora de Continente e com o
  mesmo número; combinações sem correspondência (ex.: autoliquidação diversa,
  uma linha `outras`) devolvem `null`.
- `NexGenAccountingAI.IntegrationTests`: `VatSummaryServiceTests` estendido —
  a linha de vendas à taxa normal traz o campo 3 sem nota (sede em
  Continente), a de IVA liquidado o campo 4, a de IVA dedutível de outros bens
  e serviços o campo 24; a base de uma compra não traz campo.
- `NexGenAccountingAI.Api.Tests`: `VatSummaryReturnsRowsGroupedByNatureRateAndRegion`
  passa a verificar as chaves `fieldMappingSource`/`fieldMappingDisclaimer`.
- `NexGenAccountingAI.Web`: `tsc`, `eslint` e os oitenta e três testes de interface
  verdes.
- `dotnet build`/`dotnet test` na solução (182 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia, 4.º
  trimestre de 2025): classificadas as contas `7121131` (vendas) e
  `24331131` (IVA liquidado) com taxa normal/Continente; `vat-summary`
  devolveu campo `3` na linha base e campo `4` na linha de imposto, sem
  nota (Continente); o texto do aviso e da fonte confirmados byte a byte
  (sem corrupção de acentuação); o painel na PWA mostrou a coluna «Campo» e
  o aviso; classificações removidas a seguir, contagem confirmada a 0.

## Fora deste incremento

- distinguir automaticamente operações da sede vs. Anexo R (a nota avisa, mas
  não separa os valores);
- os campos das operações menos comuns (autoliquidação diversa, regimes da
  margem, e-Taxfree, grupos de IVA, entre outros do quadro 06-A/06-B/06-C/06-D);
- a submissão à AT — precisa de certificação e das credenciais de teste
  (F2-02C+, bloqueado externamente).
