# ADR-0015 — A função «Consulta» só lê

- Estado: aceite (pedido do utilizador em 2026-10-05)
- Data: 2026-10-05

## Contexto

A função `Viewer` («Consulta») existia no modelo desde a Fase 1, mas nenhum
endpoint a verificava. Um utilizador com essa função lançava, alterava e
apagava como um contabilista. A falha ficou registada como «fora da
decisão» na [ADR-0014](ADR-0014-ORGANIZATION-PERMISSIONS.md).

## Decisão

- **Regra central.** O `CompanyRoleMiddleware` corre depois do
  `CurrentUserMiddleware`. Numa rota de empresa (com `{userId}` e
  `{companyId}`), recusa com **403** qualquer pedido que não seja GET, HEAD ou
  OPTIONS, quando a função ativa do utilizador nessa empresa é `Viewer`. A
  mensagem é em pt-PT e explica que o acesso é só de consulta.
- **Porquê central.** Há mais de 80 endpoints de escrita. Uma regra por
  omissão também apanha os que se criarem depois, sem depender de alguém se
  lembrar de a pôr em cada um.
- **Exceções explícitas.** Alguns pedidos usam POST ou PUT mas não alteram os
  dados da empresa. Cada um tem `.WithMetadata(new ViewerAllowedAttribute("motivo"))`,
  com o motivo escrito no próprio endpoint:

| Endpoint | Motivo |
|---|---|
| `POST .../irc-estimate` | Calcula; gravar é o `PUT .../irc-estimate/saved`. |
| `POST .../analyses/run` | Corre a análise sem a gravar. |
| `POST .../entry-templates/{código}/apply` | Calcula as linhas, sem lançar. |
| `POST .../fixed-assets/{código}/disposal/preview` | Pré-visualiza a alienação. |
| `POST .../invoice-qr/lookup` | Procura o documento. |
| `POST .../suggested-entries/classification/history` | Propõe, sem gravar. |
| `POST .../saft/accounts-preview`, `entries-analyze`, `entries-preview`, `sales-invoices-summary` | Analisam o ficheiro, sem importar. |
| `PUT /work-context/{userId}/companies/{companyId}/settings` | As preferências são do próprio utilizador. |

- **Ficam fechados**, mesmo sendo propostas:
  - a geração de relatórios de gestão, porque grava o relatório;
  - a proposta da IA para a classificação, porque gasta créditos da API.
- **Cadastro organizacional.** Tem as regras próprias da ADR-0014: só dono e
  administrador alteram.
- **PWA.** A barra lateral mostra a função em português e, com «Consulta»,
  avisa «só consulta, sem alterações». Os botões de escrita continuam
  visíveis e a recusa do servidor aparece como mensagem. Escondê-los ecrã a
  ecrã fica para quando houver utilizadores com essa função.

## Validação

`FoundationApiTests.AViewerCanReadButNotChangeTheCompany` corre numa base
própria, apagada no fim. O utilizador de demonstração passa a «Consulta» numa
das empresas e verifica-se que:

- consultar continua;
- criar um terceiro e apagar um documento são recusados (403, com a
  mensagem);
- procurar um QR e gravar as preferências continuam;
- na outra empresa, onde a função é outra, a recusa não se aplica.
