# ADR-0013 — PostgreSQL como base de dados da aplicação

- Estado: aceite
- Data: 2026-10-04
- Substitui: [ADR-0002](ADR-0002-SQL-SERVER-MIGRATIONS.md) (na parte do motor; os princípios de migrações versionadas mantêm-se)

## Contexto

O NexGen ERP (`C:\nexgen erp ai`) vai integrar com esta aplicação e corre em PostgreSQL 16+. Manter dois motores (SQL Server aqui, PostgreSQL no ERP) duplica instalação, backups e operação. O ROADMAP do ERP exige ainda que **não haja base de dados partilhada**: a integração faz-se só por eventos (outbox) e REST, e o ERP nunca acede às tabelas da contabilidade.

## Decisão

- a base de dados da aplicação passa a ser PostgreSQL (EF Core + Npgsql), no **mesmo servidor** do ERP mas numa **base própria** (`accounting`, papel `accounting`; testes em bases descartáveis criadas pelo mesmo papel) — ver `scripts/postgres/local-setup.sql`;
- as 39 migrações SQL Server foram substituídas por uma única migração base (`PostgreSqlBaseline`); o histórico continua no schema `infrastructure` e os schemas de domínio (`accounting`, `organization`, `identity`, `reporting`) mantêm-se;
- concorrência otimista: `RowVersion` passa de `rowversion` a `uint` mapeado na coluna de sistema `xmin`;
- a reserva de números de série usa `pg_advisory_xact_lock` com `lock_timeout` de 15s (em vez de `sp_getapplock`);
- o PostgreSQL distingue maiúsculas por omissão: as pesquisas livres usam `ILIKE` (`TextSearch.ContainsPattern`) e os nomes únicos sem distinção de maiúsculas usam a collation ICU não determinística `case_insensitive`;
- o índice único do orçamento usa `NULLS NOT DISTINCT` (PostgreSQL 15+), preservando a regra de "um valor por conta, mês e centro de custo — ou sem centro";
- as bases PHC continuam em SQL Server, acedidas por ODBC: são sistemas externos e ficam fora desta decisão.

## Consequências

- requer PostgreSQL 15 ou superior (o ERP usa 16; localmente está instalado o 18);
- os dados existentes na base SQL Server `accountingAI` não migram automaticamente: as empresas voltam a ser criadas a partir dos SAF-T (ou com uma ferramenta de cópia, se vier a ser necessária);
- os testes de integração deixam de depender do LocalDB e correm em qualquer sistema com acesso a um PostgreSQL (`ACCOUNTINGAI_TEST_PG_CONNECTION`); a CI arranca o PostgreSQL que vem na imagem `windows-2025`;
- a autenticação integrada do Windows deixa de se aplicar: em produção a password do papel `accounting` vem da variável de ambiente `ConnectionStrings__Accounting` (ou `PGPASSWORD`), nunca de `appsettings*.json`.
