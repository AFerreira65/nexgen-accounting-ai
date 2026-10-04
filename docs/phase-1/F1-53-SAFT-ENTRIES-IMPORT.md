# F1-53 — Importar os movimentos do SAF-T

## Objetivo

Depois de pré-visualizar os movimentos de um SAF-T (F1-51), importá-los para a
empresa: criar os lançamentos do bloco `GeneralLedgerEntries`, com as linhas, nos
diários certos.

## Regras

- O leitor ganha `ReadTransactionsAsync`, que percorre o `GeneralLedgerEntries` em
  streaming e devolve cada `<Transaction>` (subárvore a subárvore, sem carregar o
  ficheiro todo) com o código e a descrição do diário, o `TransactionID`, a data, a
  descrição, o número de arquivo e as linhas (conta, descrição, débito ou crédito).
  Lê o `TransactionID` como elemento filho (SAF-T PT 1.04) ou, em alternativa, como
  atributo de `<Transaction>`.
- A importação usa o `TransactionID` do ficheiro como **referência** do lançamento.
  Reimportar o mesmo ficheiro não duplica: uma transação cuja referência já exista
  na empresa é saltada.
- Os diários referidos que não existam na empresa são **criados** (código e
  descrição do ficheiro; a descrição em falta usa o código).
- Cada lançamento fica **conferido**, com a descrição da transação (ou o
  `TransactionID` se vazia) e o número de arquivo como referência do documento (ou
  o `TransactionID` se vazio). As linhas sem descrição herdam a da transação.
- Uma transação é **saltada e reportada** (motivo em pt-PT) quando: não tem
  identificador ou data; não tem diário; tem menos de duas linhas; tem uma linha
  sem um único débito ou crédito positivo; o débito e o crédito não coincidem;
  refere uma conta que não existe, está inativa ou é agregadora; a data cai num
  exercício já apurado ou no período bloqueado da empresa.
- Toda a importação corre numa só transação de base de dados.
- Endpoint (só em `Development`):
  `POST /api/v1/development/users/{userId}/companies/{companyId}/saft/entries-import`
  — corpo `application/xml`, resposta com `importedCount`, `skippedCount`,
  `journalsCreated` e `skipped` (`transactionId` + `reason`); `404` se o utilizador
  não estiver associado à empresa; `400` (`ValidationProblem`, chave `saft`) se o
  ficheiro não for legível.
- No ecrã de importação, o painel «Movimentos do ficheiro» ganha o botão
  «Importar os N movimentos» (só com ligação ao SQL Server), que mostra quantos
  entraram, quantos diários foram criados e uma tabela dos movimentos saltados com
  o motivo; a seguir volta a pré-visualizar para atualizar a coluna «Situação» dos
  diários.

Sem alteração de esquema.

## Superfície de validação local

Escolher um SAF-T com bloco `GeneralLedgerEntries` de uma empresa acessível,
pré-visualizar os movimentos e usar «Importar os N movimentos». Os lançamentos
aparecem no livro diário e na razão das contas movimentadas; reimportar o mesmo
ficheiro não cria nada de novo.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `NexGenAccountingAI.Infrastructure.Files.Tests`: o leitor devolve as três transações do
  ficheiro de exemplo com diários, datas, contas e montantes; um ficheiro sem o
  bloco devolve lista vazia;
- `NexGenAccountingAI.IntegrationTests` (LocalDB descartável): importar cinco transações
  para uma empresa criada a partir do SAF-T cria dois lançamentos e o diário em
  falta, e salta as três inválidas (desequilíbrio, conta inexistente, uma só
  linha) com o motivo certo; reimportar não duplica (as válidas passam a «já
  existe»); sem associação à empresa devolve `null`; os lançamentos ficam
  conferidos, equilibrados e no diário correto;
- verificação no browser contra a base `accountingAI`: com um SAF-T sintético cujo
  NIF corresponde à empresa de demonstração, a pré-visualização mostra três
  transações, os totais e os diários (`GERAL` «já existe», o novo «novo»); a
  importação para um exercício aberto cria os lançamentos (confirmados no livro
  diário e na razão da conta) e o diário novo, e reimportar devolve zero; com o
  exercício de 2026 apurado, as três transações de 2026 são saltadas com «o
  exercício de 2026 está apurado» e nada é escrito;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- importar a moeda, a taxa de câmbio ou os dados de cliente/fornecedor das linhas;
- ligar cada lançamento a um tipo de documento da empresa (fica sem tipo);
- reconciliar os totais importados com o `NumberOfEntries`/`TotalDebit`/
  `TotalCredit` declarados no ficheiro;
- desfazer uma importação em bloco.
