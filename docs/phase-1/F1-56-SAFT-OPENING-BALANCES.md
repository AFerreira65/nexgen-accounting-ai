# F1-56 — Importar os saldos de abertura do SAF-T

## Objetivo

Quando se traz uma empresa para a plataforma a partir de um SAF-T a meio da vida
(sem o exercício anterior no sistema), os saldos de abertura do plano de contas do
ficheiro (`OpeningDebitBalance`/`OpeningCreditBalance`) ficavam de fora. Este
incremento importa-os como um lançamento de abertura.

## Regras

- O leitor do plano de contas (`SaftAccountsReader`) passa a ler, por conta, o
  `OpeningDebitBalance` e o `OpeningCreditBalance` (0 quando ausentes). `SaftAccount`
  ganha `OpeningDebit`/`OpeningCredit`.
- `SaftOpeningBalancesImportService.ImportAsync(userId, companyId, fiscalYear,
  ficheiro)`:
  - considera apenas as contas **de movimento ativas** da empresa (as agregadoras
    têm o saldo somado das subcontas, não se lançam);
  - por conta, o saldo líquido é `OpeningDebit − OpeningCredit` arredondado a 2
    casas; contas com líquido nulo ficam de fora;
  - se nenhuma conta de movimento tiver saldo, devolve um resultado vazio;
  - se os saldos considerados não equilibrarem, recusa com uma mensagem em pt-PT;
  - caso contrário, lança pelo `JournalPostingService` um movimento no diário de
    abertura (tipo de documento `AB`, diário `ABERTURA`), datado de **1 de janeiro
    do exercício**, conferido, com uma linha «Saldo inicial» por conta;
  - é **idempotente**: se já existir um lançamento `AB` não anulado nesse
    exercício, devolve a sua referência com `alreadyImported = true` e não
    escreve.
- Endpoint só em `Development`:
  `POST /api/v1/development/users/{userId}/companies/{companyId}/saft/opening-balances-import?fiscalYear={ano}`
  — corpo `application/xml`; `404` se o utilizador não estiver associado à
  empresa; `400` (`ValidationProblem`, chave `saft`) se o ficheiro não for legível
  ou os saldos não equilibrarem.
- No ecrã de importação, o painel «Plano de contas do ficheiro» ganha o botão
  «Importar saldos iniciais» (só com ligação ao SQL Server), que usa o exercício
  do cabeçalho e mostra a referência da abertura criada, ou o aviso de que já
  existe.

Sem alteração de esquema. O lançamento `AB` fica de fora da data de bloqueio e da
guarda de exercício apurado, como qualquer abertura.

## Superfície de validação local

Escolher um SAF-T com plano de contas que traga `OpeningDebitBalance`/
`OpeningCreditBalance`, pré-visualizar o plano e usar «Importar saldos iniciais».
Aparece um lançamento no diário de abertura a 1 de janeiro; o balancete e a razão
das contas passam a mostrar o saldo de abertura. Repetir não duplica.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `AccountingAI.Infrastructure.Files.Tests`: o leitor devolve
  `OpeningDebit`/`OpeningCredit` das contas do ficheiro de exemplo (1500 / 0);
- `AccountingAI.IntegrationTests` (LocalDB descartável): importar saldos para uma
  empresa criada a partir do SAF-T cria um lançamento `AB` datado de 2026-01-01
  com três linhas equilibradas (débito 1500, crédito 1500), ignorando a conta
  agregadora; repetir devolve `AlreadyImported` com a mesma referência; sem
  associação devolve `null`; saldos que não equilibram lançam
  `InvalidOperationException`;
- verificação no browser contra a base `accountingAI`: com um SAF-T sintético para
  a empresa da ilha, «Importar saldos iniciais» cria «Abertura AB-2026-00001 …
  com 3 linhas (2500,00 €)» (`111` a débito, `121`/`221` a crédito, `11`
  agregadora ignorada), a razão da conta `111` passa a ter saldo de abertura de
  2500, e repetir mostra «Já existe abertura para 2026 (AB-2026-00001)»; o
  lançamento de teste foi removido da base no fim;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- mostrar os saldos de abertura na pré-visualização do plano de contas, antes de
  importar;
- reconciliar os saldos de abertura com os `ClosingDebitBalance`/
  `ClosingCreditBalance` do ficheiro ou com o balancete de fecho;
- importar saldos de abertura para contas que ainda não existam na empresa (hoje é
  preciso criar primeiro o plano).
