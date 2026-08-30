# F1-02C — SQL Server Express multiempresa

## Estado

Implementada e validada na base local `accountingAI` em SQL Server Express em 30 de agosto de 2026.

## Modelo

- `organization.AccountingFirms`: gabinete ou organização contabilística;
- `organization.Companies`: empresas do gabinete, com NIF, moeda e região fiscal;
- `identity.UserAccounts`: identidade lógica do utilizador, sem implementar ainda autenticação;
- `identity.CompanyUsers`: associação e função do utilizador em cada empresa;
- `identity.UserCompanySettings`: cultura, fuso horário, exercício, tema, modo compacto e paginação próprios por utilizador e empresa;
- `infrastructure.__EFMigrationsHistory`: versões de schema aplicadas.

A chave de `UserCompanySettings` é `(CompanyId, UserId)` e referencia a mesma chave em `CompanyUsers`. Esta restrição impede configurações órfãs ou pertencentes a outra empresa.

## Arranque e migrações

No ambiente de desenvolvimento, `Database:ApplyMigrationsOnStartup` está ativo. Quando a base ou as tabelas ainda não existem e o login tem permissões suficientes, o arranque cria a base e aplica todas as migrações pendentes. A operação explícita permanece disponível:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\migrate.ps1
```

A connection string com login SQL está no secret store do utilizador Windows e não no repositório. A configuração versionada contém apenas uma alternativa por autenticação integrada.

## Validação efetuada

- criação da base `accountingAI` numa instância Express vazia;
- aplicação das três migrações versionadas;
- segundo arranque sem alterações de schema;
- seed local repetível de gabinete, empresa, utilizador, associação e preferências;
- teste de integração que comprova a rejeição de configurações sem associação à empresa;
- consulta read-only dos schemas, tabelas, migrações e dados demonstrativos.

## Limites e segurança

- o login administrativo foi usado apenas para o bootstrap local; antes de staging deverá existir um login técnico com privilégio mínimo;
- autenticação, sessões, passwords da aplicação, MFA e autorização HTTP ainda não estão implementados;
- isolamento por `CompanyId` no domínio e na API deverá ser aplicado a todos os módulos contabilísticos futuros;
- backup/restauro, volumetria e concorrência permanecem gates de staging.
