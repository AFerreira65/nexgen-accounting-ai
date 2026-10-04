# ADR-0002 — SQL Server e migrações versionadas

- Estado: substituído pelo [ADR-0013](ADR-0013-POSTGRESQL.md) (PostgreSQL) em 2026-10-04
- Data: 2026-08-29

## Contexto

O produto utilizará Microsoft SQL Server. O esquema tem de ser reproduzível, auditável e aplicável sem alterações manuais feitas diretamente numa base de dados.

## Decisão

- utilizar EF Core SQL Server para modelação e migrações versionadas;
- manter o histórico de migrações no schema `infrastructure`;
- não aplicar migrações automaticamente em produção por defeito;
- permitir aplicação explícita através da API em modo `--migrate-only`;
- validar idempotência local contra SQL Server LocalDB;
- fornecer connection strings apenas por configuração externa ou secret store.

## Consequências

- alterações de esquema passam a fazer parte da revisão de código;
- aplicar duas vezes a mesma versão não altera novamente o esquema;
- LocalDB é apenas um gate local e terá de ser complementado por SQL Server de staging antes de produção;
- a estratégia definitiva de base por gabinete pertence à F1-02 e não é implementada nesta decisão.
