# ADR-0003 — Base partilhada com isolamento por empresa

- Estado: aceite para a F1-02C
- Data: 2026-08-30

## Contexto

Um gabinete contabilístico gere várias empresas e o mesmo utilizador pode trabalhar em mais do que uma. As preferências do utilizador podem variar por empresa, enquanto a identidade permanece única.

## Decisão

- usar uma base SQL Server partilhada por instalação;
- representar gabinete, empresa e utilizador como entidades distintas;
- associar utilizadores a empresas por `CompanyUsers(CompanyId, UserId)`;
- guardar preferências em `UserCompanySettings` com a mesma chave composta e uma relação um-para-um obrigatória com a associação;
- exigir `CompanyId` em todos os agregados contabilísticos futuros;
- selecionar a empresa através de contexto explícito da aplicação, nunca pelo NIF como fronteira de segurança;
- manter migrações automáticas apenas em desenvolvimento e execução explícita nos restantes ambientes.

## Consequências

- uma identidade pode ter função e preferências diferentes por empresa;
- a base impede preferências cruzadas ou órfãs;
- consultas e comandos contabilísticos terão de aplicar sempre o contexto de empresa;
- autorização, índices compostos, auditoria e testes de isolamento tornam-se requisitos permanentes;
- uma futura necessidade de base por gabinete exigirá uma nova decisão e estratégia de provisionamento.
