# F2-02A — Configuração e estado do certificado

Primeiro sub-incremento do [F2-02](F2-02-AT-COMMUNICATION.md). Configura o
certificado de transporte da AT e mostra o seu estado, sem nunca expor a senha
nem a chave ([`ADR-0008`](../architecture-decisions/ADR-0008-AT-COMMUNICATION.md)).

## Regras

- **`Application/At/`** — `AtCertificateStatus` (`Configured`, `Valid`,
  `Subject`, `Issuer`, `SerialNumber`, `Thumbprint`, `NotBeforeUtc`,
  `NotAfterUtc`, `HasPrivateKey`, `Environment`, `Problem`) e
  `IAtCertificateProvider.Describe()`.
- **Projeto novo `NexGenAccountingAI.Infrastructure.At`** — só referencia
  `Application`. `AtOptions` (secção `At`): `Certificate:Directory`,
  `Certificate:FileName`, `Certificate:Path` (absoluto, com prioridade),
  `Certificate:Password`, `Environment`. `AtCertificateProvider`:
  - sem caminho → `Configured = false`;
  - ficheiro inexistente → `Problem` «… não existe em «…»»;
  - senha errada / formato inválido (`CryptographicException`) → `Problem`
    «Confirme a senha e o formato do ficheiro (PFX/P12)»;
  - carrega com `X509CertificateLoader.LoadPkcs12FromFile(...,
    EphemeralKeySet)` e devolve os metadados; fora do período de validade →
    `Valid = false` com `Problem`.
  `EphemeralKeySet` — a chave nunca é escrita em disco. `Environment` é
  normalizado para `Testes` / `Producao`.
- **`AddAccountingAt(configuration)`** — liga `AtOptions`, completa a senha com
  `AT_CERTIFICATE_PASSWORD` quando a secção não a traz, e regista o *provider* e
  o `AtSecretProtector`. Chamado no `Program.cs`. `appsettings.json` tem a secção
  `At` (ambiente + local vazio); `appsettings.Development.json` aponta para
  `C:\PHC_Loc\certifphc.pfx`.
- **Configuração cifrada** — os valores de `At:Certificate:*` podem ir cifrados
  (prefixo `enc:v1:…`); o `AtSecretProtector` decifra-os ao ler (um valor sem
  prefixo é usado tal como está). A cifra é a **Data Protection do ASP.NET
  Core**, com o chaveiro em `%LOCALAPPDATA%\AccountingAI\dp-keys` protegido pelo
  DPAPI do Windows — os valores só se decifram na mesma máquina e conta.
  - **Ferramenta de linha de comandos:**
    `dotnet run --project src/NexGenAccountingAI.Api --no-launch-profile -- protect-at-secret "<valor>"`
    imprime o valor `enc:v1:…` a colar na configuração (a senha e/ou o local do
    ficheiro).
- **`.slnx`, referência da API e `ModuleDependencyTests`** atualizados
  (`Infrastructure.At` → `Application`; a API passa a poder referenciá-lo).
- **API:** `GET /api/v1/development/at/certificate` → o `AtCertificateStatus`
  (nunca a senha).
- **PWA:** ecrã **«Comunicação com a AT»** (`/comunicacao-at`) — em modo demo
  «Requer ligação ao SQL Server»; ligado, mostra o estado do certificado
  (ambiente, badge configurado/válido/com problema, assunto, emissor, série,
  impressão digital, validade, chave privada) e um painel «Onde se configura»
  (as chaves da secção `At`, a senha em *user-secrets* / `AT_CERTIFICATE_PASSWORD`,
  e a nota de que as chamadas SOAP aguardam a adesão à AT).

Sem alteração de esquema.

## Validação efetuada

- `NexGenAccountingAI.Infrastructure.Files.Tests`: `AtCertificateProviderTests` — sem
  caminho → não configurado; PFX auto-assinado gerado no teste → configurado,
  válido, com chave privada, ambiente normalizado, assunto correto, sem a senha
  em lado nenhum; **senha cifrada (`enc:v1:…`) na configuração → o certificado
  carrega na mesma**; senha errada → `Problem` com «senha»; ficheiro inexistente
  → `Problem` com «não existe». `AtSecretProtectorTests` — ida e volta com o
  prefixo `enc:v1:`, texto simples inalterado, e um chaveiro diferente não
  decifra.
- `NexGenAccountingAI.Api.Tests`: `AtCertificateStatusIsReportedWithoutSecrets` — o
  *endpoint* devolve `configured:false`, `environment:"Testes"`,
  `hasPrivateKey:false`, e o corpo não contém «password».
- `NexGenAccountingAI.ArchitectureTests` verde com o módulo novo.
- `NexGenAccountingAI.Web`: `App.test.tsx` (o ecrã e o gate de demonstração), `tsc`,
  `eslint` e os oitenta e um testes de interface verdes.
- `dotnet build`/`dotnet test` na solução; `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` (inventário e auditoria NuGet) sem ocorrências.
- Verificação em direto contra `accountingAI` (sem certificado configurado no
  servidor de desenvolvimento): o ecrã mostra «Não configurado» e o painel de
  configuração — a degradação sem certificado funciona.

## Fora deste incremento

- as chamadas SOAP à AT (consulta e-Fatura, submissão) — dependem da adesão e
  dos elementos de teste da AT (F2-02B+);
- as credenciais por sujeito passivo (subutilizador + perfil WFA).
