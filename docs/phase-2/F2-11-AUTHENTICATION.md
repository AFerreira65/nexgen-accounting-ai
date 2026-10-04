# F2-11 — Autenticação por sessão

Não é um dos épicos da Fase 2 (não trata de conformidade, cruzamento nem
integração) — é a peça que faltava desde o
[F1-03A](../phase-1/F1-03A-WORK-CONTEXT.md) e que ficou por fazer durante
toda a Fase 1 e a Fase 2. A decisão está na
[`ADR-0011`](../architecture-decisions/ADR-0011-AUTHENTICATION.md).

## Contexto

Toda a API vivia atrás de `if (app.Environment.IsDevelopment())`, com o
`userId` a vir diretamente da rota — um placeholder deliberado desde o
primeiro dia, nunca substituído. O gatilho para o resolver agora foi
operacional: querer a API e o site sempre ativos (serviço do Windows), o
que alargaria a janela de exposição de "enquanto tenho um terminal aberto"
para 24 horas por dia, sem autenticação nenhuma, sobre dados fiscais e
financeiros reais.

## O que ficou feito (F2-11A)

- `UserAccount.PasswordHash` (nullable — sem hash, a conta não entra),
  `PasswordHasher<UserAccount>` (`Microsoft.Extensions.Identity.Core`, já no
  *runtime* partilhado do ASP.NET Core, sem pacote novo).
- `AuthService` (`Infrastructure.SqlServer`) — `ValidateCredentialsAsync`,
  `SetPasswordAsync`. Sem endpoint de registo: só a ferramenta de linha de
  comandos `dotnet run --project src/NexGenAccountingAI.Api -- set-user-password
  "<email>" "<senha>"` define uma senha, à imagem do `protect-at-secret`
  já existente para o certificado da AT.
- Sessão por cookie (`AddAuthentication().AddCookie()`, 14 dias com
  expiração deslizante) + `POST /api/v1/auth/login`,
  `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`.
- **Reforço centralizado**, sem tocar nos mais de 100 *handlers* existentes:
  `AddAuthorization(options => options.FallbackPolicy =
  ...RequireAuthenticatedUser())` exige sessão em tudo, salvo
  `.AllowAnonymous()` explícito (login, logout, health checks, fundação); o
  `CurrentUserMiddleware` (que já lia o `userId` da rota para o carimbo de
  auditoria) passa também a exigir que esse `userId` seja o da sessão —
  `403` em caso de divergência.
- PWA: `LoginPage` (`/`, fora do `AppLayout`) + `AuthGate`
  (`src/app/AuthGate.tsx`) que chama `/auth/me` ao arrancar e só mostra a
  app com sessão válida, passando o `userId` real ao `WorkContextProvider`
  (que passou a aceitar `userId` em vez do `demoUserId` fixo — os mais de
  50 pedidos que já liam `data.user.id` continuam a funcionar sem qualquer
  alteração). Botão «Sair» no rodapé da barra lateral.

## Migração

`20260917171335_UserAccountPasswordHash` — uma coluna nova, nullable,
aditiva.

## Validação

- 186 testes .NET (Domain 35, Arquitetura 3, Infrastructure.Files 87,
  Integração 22 — `AuthServiceTests` nova —, API 39 — `AuthApiTests` nova,
  3 métodos com o fluxo real de login/logout/cookie numa base LocalDB
  descartável, sem substituir o esquema de autenticação —), 83 testes web.
  `build.ps1 -SyntheticOnly` e `verify-security.ps1` limpos.
- Os testes de API existentes (`FoundationApiTests`) passaram a autenticar
  sempre como o utilizador de demonstração fixo via um esquema de
  autenticação de teste (`TestAuthHandler`), em vez de fazerem login a
  sério — poupa reescrever cada teste, mantém o comportamento que já
  assumiam. Três chamadas que testavam um `userId` desconhecido na rota
  (esperando `404` do *handler*) passaram a esperar `403` do reforço
  centralizado, que nunca deixa esse pedido chegar ao *handler*.
- **Verificado em direto** contra a base `accountingAI` real: sem sessão,
  `GET /api/v1/development/organization` passou de `200` (antes) a `401`;
  login com senha errada → `401`; login certo → `200` + cookie; `/auth/me`
  devolve a identidade certa; uma rota com o `userId` de outra pessoa →
  `403`; logout → `200`, `/auth/me` volta a `401`. Confirmado também na PWA
  (ecrã de login, erro de credenciais, entrada, «Sair», reentrada).
- **Achado incidental, corrigido**: dois testes de API
  (`MovementsCanBeRequestedAsAServerSidePage`,
  `MovementsSummaryAggregatesTheWorkQueueWithoutTheFullList`) já falhavam
  *antes* deste incremento (confirmado por comparação com o commit anterior)
  — os seus valores esperados (`totalItems:9`, `total:9`,
  `vatPosition:598`) nunca corresponderam ao que
  `DevelopmentAccountingDataSeeder.CreateEntries()` cria (3 lançamentos,
  inalterado desde a criação do ficheiro). Corrigidos para os valores reais
  (3 lançamentos, `vatPosition:299`) — não é uma regressão deste
  incremento, mas deixava os gates permanentemente vermelhos para qualquer
  trabalho seguinte.

## Fora deste incremento

Ver "Fora deste incremento" na `ADR-0011`: limite de tentativas de login,
gestão de utilizadores pela interface, registo público, recuperação de
conta, MFA, e a parte de rede do serviço do Windows em si (Kestrel só em
`127.0.0.1`, publicação em modo Release) — que só faz sentido a seguir a
este, confirmado em uso real.

## Referências

- [`ADR-0011`](../architecture-decisions/ADR-0011-AUTHENTICATION.md).
- [`F1-03A`](../phase-1/F1-03A-WORK-CONTEXT.md) — onde o placeholder nasceu.
