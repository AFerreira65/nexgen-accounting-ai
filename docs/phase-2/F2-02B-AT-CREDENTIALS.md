# F2-02B — Credenciais da AT por empresa

Segundo sub-incremento do [F2-02](F2-02-AT-COMMUNICATION.md). Guarda o
subutilizador e a senha do perfil e-Fatura de cada empresa, com a senha
**cifrada** — reaproveitando o `AtSecretProtector` de
[F2-02A](F2-02A-AT-CERTIFICATE-CONFIG.md).

## Regras

- **`Application/At/IAtSecretProtector`** — a fronteira de cifra (`Protect` /
  `Unprotect` / `IsProtected`); `AtSecretProtector` (`Infrastructure.At`)
  implementa-a e é registada também como a interface, para o
  `Infrastructure.SqlServer` a poder usar sem referenciar `Infrastructure.At`.
- **`Domain/Organizations/CompanyAtCredentials`** — uma linha por empresa:
  `SubUser` (formato `NIF/n`, ex.: `500584095/1`) e `EncryptedPassword` (já
  cifrada, valor `enc:v1:…`). Tabela `organization.CompanyAtCredentials` (índice
  único por `CompanyId`), com as colunas de auditoria de F2-06A; migração
  aditiva `20260910200132_CompanyAtCredentials`. No `record-info`
  (`credenciais-at`) e no `DataDictionaryText`.
- **`CompanyAtCredentialsService`** (`Infrastructure.SqlServer`) —
  `GetAsync` devolve `{ Configured, SubUser, UpdatedAtUtc }` (nunca a senha);
  `SaveAsync(subUser, password)` cifra a senha antes de a gravar (um valor já
  `enc:v1:…` é guardado tal como está) e faz *upsert*; `DeleteAsync` remove.
  `null` / `false` sem acesso à empresa.
- **API:** `GET/PUT/DELETE .../companies/{companyId}/at-credentials`. O `PUT`
  recebe `{ subUser, password }`; `400` sem um dos dois, `404` sem acesso.
- **PWA:** `/comunicacao-at` ganha um painel **«Credenciais de <empresa>»** —
  o subutilizador (editável), a senha (campo *password*, nunca pré-preenchida),
  «Guardar» e «Remover», com o badge configuradas / não configuradas e a data
  da última atualização.

## Validação efetuada

- `AccountingAI.IntegrationTests`: `CompanyAtCredentialsServiceTests` (protetor
  falso com prefixo) — não configurado → guardar cifra a senha (`enc:v1:…` na
  BD, decifra para o original), um valor já cifrado fica igual, atualizar muda o
  subutilizador, remover apaga, sem acesso devolve `null`/`false`.
  `SqlServerMigrationTests` inclui a migração nova.
- `AccountingAI.Api.Tests`: `AtCertificateStatusIsReportedWithoutSecrets`
  acrescenta a leitura de `at-credentials` (`"configured":false`, sem
  «password»).
- `AccountingAI.Web`: `App.test.tsx` (o ecrã e o gate de demonstração), `tsc`,
  `eslint` e os oitenta e um testes de interface verdes.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI`.

## Fora deste incremento

- as chamadas SOAP à AT com estas credenciais — F2-02C+, bloqueado pela adesão
  ao webservice e pelos elementos de teste.
