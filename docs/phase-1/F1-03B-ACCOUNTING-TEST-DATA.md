# F1-03B — Dados contabilísticos de teste na base de dados

## Estado

Implementado e validado localmente em 30 de agosto de 2026.

## Resultado

- criado o schema SQL Server `accounting` com `Accounts`, `Journals`, `JournalEntries` e `JournalEntryLines`;
- todas as entidades contabilísticas incluem `CompanyId` e as relações compostas impedem referências cruzadas entre empresas;
- o domínio só conclui movimentos com pelo menos duas linhas e total de débitos igual ao total de créditos;
- o seed de desenvolvimento é idempotente, não contém dados reais e cria três movimentos para a empresa do Continente e um movimento distinto para a empresa dos Açores;
- a API valida a associação ativa entre utilizador e empresa antes de consultar movimentos;
- dashboard, lista, detalhe, razão de conta e diário usam os dados devolvidos pela API;
- o fallback em memória permanece apenas para permitir visualizar a PWA quando a API local não está disponível e fica identificado como demonstração.

## Dados locais

Os registos usam identificadores determinísticos e designações fictícias. São criados apenas quando `Database:SeedDemoData` está ativo, atualmente no perfil `Development`. O segundo arranque não duplica contas, diários, movimentos ou linhas.

Na base local `accountingAI` foram confirmados através da API:

- Continente: `COMP-2026-00842`, `VEND-2026-00327` e `BANC-2026-00196`;
- Açores: `VEND-AZO-2026-00014`;
- todos os quatro movimentos com débito igual a crédito;
- ausência de movimentos da outra empresa em cada resposta.

## API local

O endpoint existe apenas em `Development`:

```text
GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/movements
```

Esta rota usa ainda o utilizador lógico de desenvolvimento. Não substitui autenticação, autorização de produção nem uma sessão real.

## Validação efetuada

- migração `AccountingLedgerTestData` aplicada incrementalmente à base local `accountingAI`;
- segundo arranque sem migrações pendentes e seed repetido sem duplicados;
- criação, migração, segundo arranque e remoção de uma base SQL Server LocalDB temporária;
- consulta real da API às duas empresas na base configurada;
- testes de regras do domínio para equilíbrio e validade das linhas;
- teste de API para isolamento dos conjuntos de dados por empresa;
- lint, testes e build de produção da PWA.

A migração e a API foram validadas tanto numa base temporária LocalDB como na instância SQL Server configurada. Isto continua a ser validação local, não staging nem produção.

## Fora deste incremento

- criação, alteração, aprovação e anulação de movimentos pela PWA;
- autenticação, MFA, sessões e autorização de produção;
- importação do plano de contas ou dos movimentos a partir de SAF-T;
- anexos/documentos, reconciliação bancária, fiscalidade, PHC e IA;
- volumes, concorrência, backup/restauro e SQL Server de staging.

O próximo incremento deve escolher entre identidade/autorização ou importação contabilística SAF-T para estas tabelas, mantendo sempre o filtro obrigatório por empresa.
