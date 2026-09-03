# F1-48 — SAF-T: criar as contas novas do plano

## Objetivo

Completar o fluxo do plano de contas do SAF-T (F1-42/F1-43): a seguir à
pré-visualização, criar na empresa as contas do ficheiro que ainda não existem.

## Regras

`SaftAccountsImportService.ImportAsync` confirma o acesso do utilizador à empresa,
lê o bloco `GeneralLedgerAccounts` e cria as contas em falta, numa transação e por
ordem de código (mais curtos primeiro). Para cada conta:

- a conta-mãe é o código já existente (ou criado neste mesmo passo) mais longo que
  seja prefixo do código;
- `AllowsPosting` segue a regra F1-47: código com 1 ou 2 dígitos → integração; 3
  ou mais → movimento;
- sem descrição no ficheiro, o nome fica igual ao código.

As contas já existentes não são alteradas. Correr de novo com o mesmo ficheiro
cria zero contas.

O endpoint `POST .../companies/{companyId}/saft/accounts-import` (corpo = o XML)
devolve `{ createdCount, alreadyExistedCount, created: [...] }`, `404` sem acesso
e `400` para XML ilegível.

Na PWA, o painel **Plano de contas do ficheiro** ganha o botão **Criar as N
contas novas** (só com ligação ao SQL Server) que importa, recarrega o
provisionamento e volta a pré-visualizar.

Sem alteração de esquema.

## Superfície de validação local

Depois de pré-visualizar um SAF-T com contas novas, o botão **Criar as N contas
novas** cria-as; a pré-visualização passa a mostrar 0 novas e o número de contas
de movimento da empresa sobe.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: importar `["11" (existe), "13", "131", "11" (dup)]` numa empresa recém-provisionada cria 2 contas — `13` de integração com mãe `1`, `131` de movimento com mãe `13`; uma segunda importação cria 0; sem acesso devolve `null`;
- teste de API: `/foundation` indica a fase `F1-48`;
- verificação no browser contra a base `accountingAI`: um SAF-T com `62`, `627`, `627001`, `6221` mostra 2 novas; **Criar as 2 contas novas** cria `627` (mãe `62`) e `627001` (mãe `627`), ambas de movimento, e as contas de movimento da empresa passam de 44 para 46;
- suite .NET completa (65 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- usar `GroupingCategory` do ficheiro em vez do comprimento do código para decidir integração/movimento;
- importar saldos de abertura e a categoria de taxonomia das contas;
- importar contas de terceiros (`Customer`/`Supplier`) e os movimentos (`GeneralLedgerEntries`);
- desfazer/rever a importação.
