# ADR-0014 — Quem pode alterar o cadastro organizacional

- Estado: aceite (o utilizador pediu a correção em 2026-10-05)
- Data: 2026-10-05

## Contexto

A [ADR-0011](ADR-0011-AUTHENTICATION.md) pôs toda a API atrás de uma sessão,
e o `CurrentUserMiddleware` garante que o `userId` da rota é o da sessão. Os
endpoints do cadastro organizacional (`/api/v1/development/organization/...`)
não têm `userId` na rota e não verificavam mais nada. Ao preparar o filtro
por responsável do painel do gabinete (F3-28), encontrou-se o seguinte:

- **`GET /organization`** devolvia todos os gabinetes, todas as empresas e
  todos os utilizadores, com o email e os acessos de cada um.
- **`PUT /organization/companies/{empresa}/members/{userId}`** deixava
  qualquer utilizador com sessão **dar-se a si próprio a função de dono de
  qualquer empresa**. Bastava pôr o seu id no lugar do `userId`, e ficava com
  acesso à contabilidade dela.
- **Criar empresas e utilizadores**, e alterar empresas, não pedia nenhuma
  função.
- **Ao contrário:** como o alvo estava no `{userId}` da rota, o middleware
  recusava (403) a um administrador a gestão de **outra** pessoa.
- **Dono errado:** a empresa criada ficava associada ao utilizador de
  demonstração, e não a quem a criou.

## Decisão

O `OrganizationAdminService` recebe o utilizador da sessão em cada operação e
aplica estas regras:

| Operação | Quem pode |
|---|---|
| Ver o cadastro | Só os gabinetes e as empresas onde tem acesso ativo. Os acessos dos outros utilizadores, só nas empresas que administra. |
| Alterar uma empresa | O dono ou um administrador ativo dela. |
| Gerir os acessos a uma empresa | O dono ou um administrador. Só um dono dá ou tira a função de dono, ou mexe no acesso de um dono. A empresa nunca fica sem um dono ativo. |
| Criar uma empresa num gabinete | Quem já é dono ou administrador de uma empresa desse gabinete. Fica dono da empresa nova. |
| Criar um utilizador | Quem é dono ou administrador de alguma empresa. |
| Alterar um utilizador | O próprio, mas sem se poder desativar. Outro utilizador só por quem administre **todas** as empresas onde ele tem acesso, e seja dono naquelas onde ele é dono, porque desativar vale em todas. |

**Respostas da API:**

- Uma empresa a que não tem acesso responde **404**, como no resto da API.
- Com acesso mas sem a função, responde **403**, com a explicação.

**Rotas e auditoria:**

- O alvo passou a `{accountId}` e `{memberId}` nas rotas. O `{userId}` fica
  reservado ao utilizador da sessão.
- Sem `userId` na rota, o carimbo de auditoria fica com o utilizador da
  sessão.
- O `GET` passou a indicar `canManage` (por empresa) e `canCreateCompanies`
  (por gabinete). A PWA usa-os para esconder o que o utilizador não pode
  fazer; quem garante é o servidor.

## Fora desta decisão

- **A função «Viewer» (só leitura) não é aplicada** dentro da empresa: um
  utilizador com essa função consegue lançar e alterar como os outros. É uma
  decisão à parte, porque toca em todos os endpoints de escrita.
- **A criação de empresa a partir de um SAF-T**
  (`POST /users/{userId}/saft/companies`) continua aberta a qualquer membro
  ativo do gabinete. Também aí quem cria fica dono.

## Validação

- `OrganizationAdminAccessTests` (PostgreSQL) cobre:
  - a falha antiga: um estranho e um contabilista a darem-se a função de dono;
  - o administrador a gerir outra pessoa, que antes dava 403;
  - o administrador sem poder mexer em donos;
  - a empresa sem poder ficar sem dono;
  - as regras de alteração de utilizadores;
  - a criação de empresas e utilizadores, com quem cria a ficar dono.
- `FoundationApiTests`:
  - uma empresa sem acesso responde 404;
  - o cadastro indica `canManage` e `canCreateCompanies`.
