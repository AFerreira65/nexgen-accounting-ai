# F1-11 — Lançamento ligado ao tipo de documento

## Objetivo

Passar o tipo de documento de simples prefixo da referência (F1-10) a relação
persistida do lançamento, para permitir consultas e relatórios por tipo de
documento.

Ficam disponíveis:

- `JournalEntry.DocumentTypeId` opcional, com chave estrangeira para `AccountingDocumentType` por `(CompanyId, DocumentTypeId)`;
- a leitura de movimentos devolve o código e o nome do tipo de documento;
- filtro dos movimentos por tipo de documento (`?documentType=`).

## Alteração de esquema

Migração `20260830234235_JournalEntryDocumentType`:

- coluna `DocumentTypeId` (`uniqueidentifier`, nula) em `accounting.JournalEntries`;
- chave alternativa `(CompanyId, Id)` em `accounting.AccountingDocumentTypes`;
- índice `(CompanyId, DocumentTypeId)` e chave estrangeira composta com `ON DELETE NO ACTION`.

É aditiva e não destrutiva; aplica-se automaticamente no arranque do perfil de
desenvolvimento e nos testes de migração.

## Regras

`JournalPostingService` guarda o `DocumentTypeId` quando o lançamento é criado ou
corrigido com um tipo de documento; sem tipo de documento, o campo fica nulo. A
numeração continua a usar o prefixo do tipo, como em F1-10.

## Superfície de validação local

- `GET .../accounting/movements` devolve `documentType` e `documentTypeName` (nulos quando não há tipo) e aceita `?documentType={código}` para filtrar;
- `POST` e `PUT .../accounting/movements` mantêm o campo opcional `documentTypeCode`.

Na PWA, a lista de movimentos mostra o código do tipo de documento junto do
documento de origem.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: a migração é aplicada (duas vezes) e consta do histórico; um lançamento criado com o tipo `FTV` fica com `DocumentTypeId` preenchido e a navegação devolve o código `FTV`;
- teste de API: a leitura de movimentos inclui `"documentType":null` para os lançamentos sem tipo e o filtro `?documentType=FTV` exclui os que não têm esse tipo;
- lint, catorze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- séries por tipo de documento e por exercício com contadores próprios persistidos;
- relatórios por tipo de documento (diário de vendas, diário de compras, etc.);
- estorno automático na anulação;
- verificação do catálogo de taxonomias contra o diploma.
