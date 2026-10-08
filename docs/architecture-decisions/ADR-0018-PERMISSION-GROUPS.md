# ADR-0018 — Grupos de permissões

- Estado: aceite (decisões do utilizador em 2026-10-07)
- Data: 2026-10-07

## Contexto

Só havia quatro funções por empresa (`Owner`, `Administrator`, `Accountant`,
`Viewer`), e só duas regras as usavam: dono e administrador gerem o cadastro
([ADR-0014](ADR-0014-ORGANIZATION-PERMISSIONS.md)) e `Viewer` não escreve
([ADR-0015](ADR-0015-VIEWER-READ-ONLY.md)). O utilizador pediu permissões por
utilizador — consultar, escrever, alterar, por ecrã, e enviar à AT e receber da
AT — com **grupos**, como no PHC: cria-se um grupo com as permissões e atribui-se
a cada utilizador. E uma qualidade de **Contabilista** que dá acesso a tudo e é
a única que vê as senhas dos envios que usam a senha do TOC (IES, Modelo 22).

## Decisões

1. **Os grupos são do gabinete** (`identity.PermissionGroups`, por `FirmId`) e
   servem todas as empresas dele.
2. **Granularidade por ecrã e ação.** O catálogo (`PermissionCatalog`) é fixo,
   em código, com chaves `recurso.ação`: por exemplo `accounts.create` («abrir
   contas»), `movements.create`, `movements.void`, `yearend.reopen`. Cada ação
   do menu tem a sua `view`.
3. **Cada utilizador tem um grupo por empresa**
   (`CompanyUser.PermissionGroupId`). Sem grupo, vale o predefinido da função:
   «Consulta» para `Viewer`, «Completo» para as outras. O dono e o
   administrador têm sempre todas as permissões.
4. **Uma permissão por comunicação com a AT:** `at.send_vat`, `at.send_ies`,
   `at.send_modelo22`, `at.receive_efatura`, `at.view` e `at.credentials`.
5. **Contabilista** (`UserAccount.IsAccountant`):
   - tem todas as permissões nas empresas em que não é `Viewer` (a função
     «Consulta» é um limite posto de propósito na empresa e trava-o);
   - as permissões marcadas `AccountantOnly` — `at.credentials`,
     `at.send_ies`, `at.send_modelo22` — **só as tem quem é contabilista**,
     mesmo que um grupo as inclua. A PWA esconde as senhas da AT e os
     painéis de envio a quem não as pode usar;
   - só um contabilista dá ou tira esta qualidade;
   - quem já usava a aplicação ficou contabilista na migração, para nada
     deixar de funcionar; os utilizadores novos entram sem ela.

## Grupos de origem

Criados por gabinete na primeira consulta e mantidos em sintonia com o
catálogo; não se alteram, copiam-se: **Consulta** (só consulta e exporta),
**Lançamentos** (consulta tudo e trabalha em movimentos, bancos, arquivo e
terceiros) e **Completo** (tudo menos o que é só do contabilista).

## Aplicação

- **Servidor:** o `CompanyPermissionMiddleware` (substitui o
  `CompanyRoleMiddleware` e o `ViewerAllowedAttribute`) procura, na tabela
  `CompanyPermissionRules`, a permissão que cada endpoint de empresa exige, e
  recusa com 403 se o utilizador não a tem. Um endpoint sem regra também é
  recusado; o teste `EveryCompanyEndpointHasAPermissionRule` percorre todos os
  endpoints da API e falha se um novo não tiver regra. `*` na tabela quer dizer
  «qualquer utilizador com acesso à empresa» (por exemplo, exportar uma tabela
  que o ecrã já mostra, ou gravar as próprias preferências).
- **Cadastro:** gerir grupos e atribuí-los exige ser dono ou administrador de uma
  empresa do gabinete (regras da ADR-0014).
- **PWA:** o contexto de trabalho traz as permissões da empresa ativa
  (`permissions`), o menu esconde o que o utilizador não pode consultar, e o
  ecrã «Grupos de permissões» (Administração) edita a matriz.

## Fora desta decisão

- Os ecrãs de escrita ainda mostram os botões a quem só consulta; a recusa do
  servidor aparece como mensagem. Escondê-los ecrã a ecrã fica para depois.
- As rotas sem empresa (`/users/{userId}/tasks`, o painel do gabinete, a
  inspeção de SAF-T) não passam por este controlo.
