# ADR-0011 — Autenticação por sessão (substitui o placeholder de desenvolvimento)

- Estado: aceite, F2-11A implementado
- Data: 2026-09-17

## Contexto

Desde o [F1-03A](../phase-1/F1-03A-WORK-CONTEXT.md) (30 de agosto), toda a API
vive atrás de `if (app.Environment.IsDevelopment())`
([Program.cs](../../src/AccountingAI.Api/Program.cs)) e cada pedido recebe o
`userId` diretamente da rota (`CurrentUserMiddleware`). Esse próprio documento
já avisava: *"Esta API de desenvolvimento não representa autenticação: será
substituída por contexto derivado da sessão quando identidade e autorização
forem implementadas."* Isso nunca aconteceu — toda a Fase 1 e a Fase 2 foram
construídas sobre esse placeholder.

O gatilho para resolver isto agora foi operacional: passar a API e o site a
correr sempre ativos (serviço do Windows, não só quando há um terminal aberto
com `dotnet run`) alargaria a janela de exposição de "enquanto tenho um
terminal aberto no meu portátil" para "24 horas por dia" — sem que exista
autenticação nenhuma. A app tem dados fiscais e financeiros reais, credenciais
da AT, e várias empresas reais (`Vidreira Algarvia Lda`, `TNMC Lda`).

A base de dados real (`accountingAI`) tem hoje exatamente **um**
`UserAccount`, semeado pelo `DevelopmentDataSeeder`
(`33333333-3333-3333-3333-333333333333`,
`antonio.demo@accountingai.local`), sem palavra-passe nem forma de
autenticar. O modelo de dados já prevê multiutilizador por empresa
(`CompanyUser` + `CompanyUserRole`), mas isso é só estrutura — nunca houve
login.

## Decisão

- **Sessão por cookie**, não token/JWT: a PWA e a API partilham origem (a
  mesma porta, ver F2-11B) ou, em desenvolvimento, o proxy do Vite torna-as
  same-origin — um cookie `HttpOnly`/`SameSite=Lax` é mais simples e mais
  seguro contra XSS do que guardar um token em `localStorage`. Sem "lembrar-me"
  distinto nesta fase: 14 dias com expiração deslizante.
- **`PasswordHasher<UserAccount>`** (`Microsoft.Extensions.Identity.Core`) —
  o mesmo algoritmo (PBKDF2) que o ASP.NET Core Identity usa, sem trazer o
  resto do *framework* Identity (sem `IdentityDbContext`, sem *stores*
  genéricos) — mantém o padrão já seguido pelo projeto de só adicionar a peça
  precisa (o mesmo raciocínio do `AtSecretProtector` para a Data Protection).
- **`UserAccount.PasswordHash`** fica `nullable`: uma conta sem *hash* não
  consegue autenticar-se, ponto final — nunca há uma palavra-passe por
  omissão. Define-se por uma ferramenta de linha de comandos
  (`set-user-password`, à imagem do já existente `protect-at-secret`), nunca
  por um endpoint de registo público.
- **Reforço centralizado, não por endpoint**: em vez de tocar em cada um dos
  mais de 100 *handlers* que hoje recebem `{userId}` da rota, dois mecanismos
  cobrem toda a API de uma vez:
  1. `AddAuthorization(options => options.FallbackPolicy = ...RequireAuthenticatedUser())`
     — qualquer endpoint sem `.AllowAnonymous()` explícito exige sessão válida.
  2. `CurrentUserMiddleware` (que já lia o `userId` da rota para o carimbo de
     auditoria) passa também a **comparar esse `userId` com o da sessão
     autenticada** e a responder `403` em caso de divergência — sem isto, um
     utilizador autenticado podia trocar o `userId` no URL e agir por outro.
- **Sem registo próprio, sem recuperação de senha, sem MFA nesta fase** — só
  existe (e vai continuar a existir por agora) um utilizador real. Cada uma
  dessas peças é um incremento à parte quando houver mais do que uma pessoa a
  usar a aplicação a sério.
- A PWA ganha um ecrã de login (`/login`, fora do `AppLayout`) que chama
  `GET /api/v1/auth/me` ao arrancar; sem sessão válida mostra o formulário,
  com sessão válida passa o `userId` devolvido ao `WorkContextProvider` — os
  mais de 50 pedidos que já usam `data.user.id` (não o valor fixo
  `demoUserId`) continuam a funcionar sem qualquer alteração.

## Fora deste incremento (F2-11A)

- Limite de tentativas de login / atraso progressivo — só um utilizador real
  hoje, risco de força bruta é baixo; a acrescentar quando houver exposição
  maior que "este PC" (ver F2-11B, ainda por fazer, do serviço do Windows).
- Gestão de utilizadores pela interface (criar/desativar contas, repor senha)
  — continua a ser a ferramenta de linha de comandos, tal como o certificado
  da AT.
- Registo público, recuperação de conta por email, MFA.
- Vincular a política de "IsDevelopment" ao serviço do Windows em si — essa
  parte (Kestrel só em `127.0.0.1`, publicação em modo Release, o próprio
  serviço) fica para quando este incremento estiver confirmado em uso real.

## Consequências

- A API deixa de estar protegida só por "correr `dotnet run` na minha
  máquina" — exige sempre uma sessão de um utilizador real com senha, e essa
  sessão já não pode agir por outro `userId` na URL.
- O único utilizador existente (`antonio.demo@accountingai.local`) fica sem
  conseguir entrar até correr `set-user-password` com uma senha à escolha do
  utilizador — feito fora desta conversa, nunca pedindo a senha ao agente.
- Continua a faltar a parte de rede do problema (o serviço do Windows em si);
  este incremento resolve só a falta de autenticação, que era o bloqueio mais
  grave e o que o próprio F1-03A já tinha identificado e nunca resolvido.
