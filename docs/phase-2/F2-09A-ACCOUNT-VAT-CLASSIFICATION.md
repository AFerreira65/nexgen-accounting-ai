# F2-09A — Classificação de IVA por conta

Primeiro sub-incremento do [F2-09](F2-09-ACCOUNT-VAT-CLASSIFICATION.md). A
classificação de IVA (taxa / natureza / região) por conta, por conta e em lote
([`ADR-0009`](../architecture-decisions/ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md)).

## Regras

- **`Domain/Accounting/`** — `VatRate` (`Nenhuma`/`Isento`/`Reduzida`/
  `Intermedia`/`Normal`), `VatOperationNature` (lista plana: transmissões
  internas, prestações de serviços, exportações/isentas com dedução, aquisições
  intracomunitárias de bens e de serviços, autoliquidação, isentas sem dedução,
  IVA dedutível imobilizado / existências / outros, regularizações a favor do
  sujeito passivo / do Estado), `VatRegion` (`Continente`/`Acores`/`Madeira`);
  `AccountVatClassification` (uma linha por conta, `IsEmpty` quando taxa e
  natureza são `Nenhuma`).
- **`accounting.AccountVatClassification`** — `AccountId` único, `CompanyId`, FK
  a `Accounts` (cascade), enums como `int`, colunas de auditoria de F2-06A;
  migração aditiva `20260910202125_AccountVatClassification`. No `record-info`
  (`classificacoes-iva`) e no `DataDictionaryText`.
- **`AccountVatClassificationService`** — só as contas de **movimento** das
  classes 6 e 7 e as `2432*` `2433*` `2434*` são relevantes:
  - `ListAsync` → cada conta relevante com a classificação (ou vazia) e
    `Classified`;
  - `SetAsync(accountCode, rate, nature, region)` → *upsert*; se ficar tudo
    `Nenhuma`, apaga a linha; `false` sem acesso ou conta desconhecida;
  - `BulkSetAsync(prefix, …)` → aplica a todas as contas relevantes cujo código
    começa pelo prefixo; devolve o número atingido, `-1` sem acesso.
- **API:** `GET .../vat-classification`,
  `PUT .../vat-classification/{código}` (corpo `{ rate, nature, region }` — `400`
  se um enum é inválido), `POST .../vat-classification/bulk`
  (corpo `{ prefix, rate, nature, region }` → `{ updated }`).
- **PWA:** ecrã **«Configuração de IVA»** (`/config-iva`) — em modo demo «Requer
  ligação ao SQL Server»; ligado, um painel «Aplicar em lote» (prefixo + taxa +
  natureza + região) e a grelha das contas com `<select>` de taxa / natureza /
  região por linha (guarda ao mudar), com pesquisa e a contagem de
  classificadas. Entrada no menu lateral a seguir ao «Apuramento de IVA».

## Validação efetuada

- `AccountingAI.IntegrationTests`: `AccountVatClassificationServiceTests` —
  a lista traz só as contas 6/7/243 (não a 1201) e todas por classificar;
  classificar a 7111 fica `Classified` com a taxa/natureza; voltar a `Nenhuma`
  apaga a linha; o lote por `2433` atinge as duas contas `243`; conta
  desconhecida `false`, sem acesso `null`/`-1`. `SqlServerMigrationTests` inclui
  a migração nova.
- `AccountingAI.Api.Tests`: `VatClassificationListsRelevantAccounts` — a lista
  vem como `[` com `accountCode`/`rate`/`nature`; um enum inválido no `PUT` dá
  `400`; empresa sem acesso `404`.
- `AccountingAI.Web`: `App.test.tsx` (o ecrã e o gate de demonstração), `tsc`,
  `eslint` e os oitenta e oito testes de interface verdes.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI`.

## Fora deste incremento

- o resumo do IVA por taxa e natureza de um período — F2-09B;
- o mapeamento para os campos numerados do modelo oficial — F2-09C.
