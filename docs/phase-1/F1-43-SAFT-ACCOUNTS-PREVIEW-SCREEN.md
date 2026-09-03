# F1-43 — SAF-T: pré-visualização do plano de contas no ecrã

## Objetivo

Expor no ecrã de importação de SAF-T a reconciliação do plano de contas do F1-42:
depois de o ficheiro corresponder a uma empresa, mostrar quantas contas já
existem e quantas seriam novas, com a lista.

## Regras

Quando a inspeção do ficheiro encontra uma empresa (`ExistingCompany`), o ecrã
mostra o painel **Plano de contas do ficheiro** com o botão **Pré-visualizar
plano de contas**. Ao carregar:

- com ligação ao SQL Server, chama `POST .../companies/{companyId}/saft/accounts-preview`
  (o mesmo ficheiro no corpo);
- em modo de demonstração, o cliente extrai o bloco `GeneralLedgerAccounts`
  (`parseSaftAccountsFromXml`, namespace-agnóstico, a parar antes dos movimentos)
  e classifica cada conta contra o plano em memória da empresa ativa.

O resultado mostra as contagens (contas no ficheiro, já existentes, novas, contas
de movimento da empresa) e uma tabela ordenada por código com a situação **Já
existe** / **Nova**. Um ficheiro sem plano de contas mostra o aviso
correspondente. Nada é criado.

Sem alteração de esquema nem de backend (o endpoint é o do F1-42).

## Superfície de validação local

No ecrã **Importar SAF-T**, depois de escolher um ficheiro cujo NIF corresponda a
uma empresa, o botão **Pré-visualizar plano de contas** mostra as contagens e a
tabela.

## Validação efetuada

- verificação no browser contra a base `accountingAI`: ficheiro com as contas `11`, `6221` e `4210001` → «Contas no ficheiro 3», «Já existentes 2», «Novas 1», «Contas de movimento da empresa 44»; a tabela marca `11` e `6221` como **Já existe** e `4210001` como **Nova**;
- teste de interface: o mesmo fluxo em modo de demonstração classifica `11` como já existente e `99999999` como nova;
- `scripts/build.ps1 -SyntheticOnly` (suite .NET de 64 testes e smoke tests) sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e nove testes de interface e build de produção da PWA.

## Fora deste incremento

- criar as contas novas na empresa a partir do ficheiro;
- ler saldos de abertura e a categoria de agrupamento da taxonomia;
- pré-visualizar e importar os movimentos (`GeneralLedgerEntries`).
