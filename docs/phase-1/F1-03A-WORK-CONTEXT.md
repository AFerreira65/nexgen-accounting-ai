# F1-03A — Contexto de trabalho multiempresa

## Estado

Implementado e validado localmente em 30 de agosto de 2026.

## Resultado

- a PWA obtém da API o utilizador de desenvolvimento e apenas as empresas onde tem uma associação ativa;
- o seletor no cabeçalho muda a empresa ativa e conserva a escolha no browser;
- exercício, tema, densidade das grelhas, cultura, fuso horário e paginação pertencem ao par `(CompanyId, UserId)`;
- o ecrã **Definições** lê e grava essas preferências na base `accountingAI`;
- a interface identifica se está ligada ao SQL Server ou a usar o fallback visual de demonstração;
- o seed idempotente inclui uma empresa do Continente e outra dos Açores para provar configurações diferentes do mesmo utilizador.

## API local

Os endpoints deste incremento existem apenas no ambiente `Development`:

- `GET /api/v1/development/work-context/{userId}`;
- `PUT /api/v1/development/work-context/{userId}/companies/{companyId}/settings`.

O `PUT` só aceita uma empresa ativa associada ao utilizador e reutiliza as validações do domínio. Esta API de desenvolvimento não representa autenticação: será substituída por contexto derivado da sessão quando identidade e autorização forem implementadas.

## Validação efetuada

- leitura real das duas empresas através da API ligada ao SQL Server Express;
- escrita real das preferências da empresa do Continente;
- confirmação de que o fuso horário dos Açores permaneceu independente;
- lint, três testes de interface e build de produção da PWA;
- build .NET sem avisos nem erros;
- 11 testes .NET, incluindo migração SQL executada duas vezes.

## Fora deste incremento

- login, MFA, recuperação de conta e gestão de sessões;
- autorização de produção e auditoria das mudanças de preferências;
- plano de contas, diários e movimentos persistidos;
- administração de empresas e convites a utilizadores.

O próximo incremento funcional deve introduzir identidade/autorização ou, se for priorizado o núcleo contabilístico, começar pelo plano de contas com todas as consultas obrigatoriamente filtradas pelo contexto de empresa.
